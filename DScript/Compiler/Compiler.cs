/*
Copyright (c) 2014 - 2020 Darren Horrocks

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Collections.Generic;
using DScript.Vm;

namespace DScript.Compiler
{
    /// <summary>
    /// Compiles DScript source into bytecode <see cref="Chunk"/>s using the same
    /// operator-precedence recursive descent as the tree-walking engine, but
    /// emitting opcodes instead of executing. Forward jumps are backpatched.
    ///
    /// A <c>canAssign</c> flag flows down the precedence chain: only an operand
    /// parsed at assignment precedence may become an assignment target, so the
    /// identifier/member code knows whether to emit a get or a set.
    /// </summary>
    public sealed partial class DScriptCompiler : IDisposable
    {
        private ScriptLex lexer;
        private Chunk chunk;
        private int _destructureCounter;
        private int _importCounter;
        private int _blockDepth;
        // Counter that is >0 while compiling the branch body of an if/else statement
        // that has no wrapping block. Used by Annex B.3.3 to detect function
        // declarations that need their own scope.
        private int _inIfBranch;
        // Names bound by let/const in the enclosing eval scope. Set only when
        // compiling an eval program; null otherwise.
        private System.Collections.Generic.HashSet<string> _evalLetConflicts;
        // Private names declared in the class currently being compiled.
        // Null when not inside a class body. Used to validate #name access.
        private System.Collections.Generic.HashSet<string> _currentClassPrivateNames;

        // Stack of const-propagation scopes (innermost first).  Each scope maps a
        // `const` identifier to the compile-time constant that was assigned to it.
        // A null entry acts as a parameter barrier: it prevents an outer const from
        // being substituted for a function parameter that shares the same name.
        // Null when EnableOptimizer is false (constant propagation is disabled).
        private Stack<Dictionary<string, ConstantValue>> _constScopes;

        // Walk from the innermost scope outward; return the first constant value
        // found for `name`.  A null entry stops the walk (parameter barrier).
        private bool TryGetPropagatedConst(string name, out ConstantValue value)
        {
            if (_constScopes != null)
            {
                foreach (var scope in _constScopes)
                {
                    if (scope.TryGetValue(name, out value))
                        return value != null; // null entry = barrier, stop
                }
            }
            value = null;
            return false;
        }

        // If exactly one Constant instruction was emitted starting at baseStart,
        // record its value in the innermost const scope so uses of `name` can be
        // substituted without a GetVar.
        private void TryRecordConstPropagation(string name, int baseStart)
        {
            if (!EnableOptimizer || _constScopes == null || _constScopes.Count == 0) return;
            if (chunk.Count - baseStart != 5) return;
            if ((OpCode)chunk.Code[baseStart] != OpCode.Constant) return;
            var constIdx = chunk.Code[baseStart + 1]
                           | (chunk.Code[baseStart + 2] << 8)
                           | (chunk.Code[baseStart + 3] << 16)
                           | (chunk.Code[baseStart + 4] << 24);
            _constScopes.Peek()[name] = chunk.Constants[constIdx];
        }

        /// <summary>
        /// When false, skips the new peephole passes (constant folding,
        /// BinaryIntConst specialisation, jump-chain collapsing, dead-code
        /// elimination) so only the original BinaryConst fusion runs.
        /// Intended for benchmarking and disassembly comparisons; always
        /// leave at the default (<c>true</c>) in production.
        /// </summary>
        public bool EnableOptimizer { get; set; } = true;

        public void Dispose()
        {
            lexer?.Dispose();
        }

        /// <summary>Compile a single expression to a chunk that returns its value.</summary>
        public Chunk CompileExpression(string source)
        {
            lexer = new ScriptLex(source);
            chunk = new Chunk { Name = "<expr>" };
            if (EnableOptimizer)
            {
                _constScopes = new Stack<Dictionary<string, ConstantValue>>();
                _constScopes.Push(new Dictionary<string, ConstantValue>());
            }

            CompileExpression();
            chunk.Emit(OpCode.Return);

            AnalyzeSlotsAndCaptures(chunk);

            // Lever A (AOT/closure build): rewrite slottable locals before the
            // optimizer, which then narrows/fuses only the remaining name-based ops.
            if (ScriptEngine.EnableLocalSlots) chunk.PromoteLocalSlots();

            if (EnableOptimizer)
            {
                chunk.CollapseJumpChains();
                chunk.FoldConstantBranches();
                chunk.EliminateDeadCode();
                chunk.FuseSuperInstructions();
                chunk.NarrowEncodePass();
                _constScopes = null;
            }
            return chunk;
        }

        /// <summary>Compile a full program (statement sequence) to a chunk.</summary>
        public Chunk CompileProgram(string source)
        {
            var isAsync = HasTopLevelAwait(source);

            lexer = new ScriptLex(source);
            chunk = new Chunk { Name = "<main>", IsAsync = isAsync };
            if (EnableOptimizer)
            {
                _constScopes = new Stack<Dictionary<string, ConstantValue>>();
                _constScopes.Push(new Dictionary<string, ConstantValue>());
            }

            ConsumeUseStrictIfPresent();

            while (lexer.TokenType != ScriptLex.LexTypes.Eof)
            {
                CompileStatement();
            }

            chunk.Emit(OpCode.PushUndefined);
            chunk.Emit(OpCode.Return);

            AnalyzeSlotsAndCaptures(chunk);

            // Lever A (AOT/closure build): rewrite slottable locals before the
            // optimizer, which then narrows/fuses only the remaining name-based ops.
            if (ScriptEngine.EnableLocalSlots) chunk.PromoteLocalSlots();

            if (EnableOptimizer)
            {
                chunk.CollapseJumpChains();
                chunk.FoldConstantBranches();
                chunk.EliminateDeadCode();
                chunk.FuseSuperInstructions();
                chunk.NarrowEncodePass();
                _constScopes = null;
            }
            return chunk;
        }

        /// <summary>
        /// Compile <paramref name="source"/> as an eval program. Like
        /// <see cref="CompileProgram"/> but uses eval-mode semantics (strict detection,
        /// let/const conflict tracking for Annex B.3.3).
        /// </summary>
        public Chunk CompileEvalProgram(string source)
        {
            lexer = new ScriptLex(source);
            chunk = new Chunk { Name = "<eval>" };
            if (EnableOptimizer)
            {
                _constScopes = new Stack<Dictionary<string, ConstantValue>>();
                _constScopes.Push(new Dictionary<string, ConstantValue>());
            }

            ConsumeUseStrictIfPresent();

            // Pre-scan for let/const bindings so Annex B.3.3 can avoid hoisting
            // function declarations that would conflict with them.
            _evalLetConflicts = CollectLetConstNames(source);

            // ECMAScript function hoisting: a top-level `function` binding is visible
            // with its closure value before the first statement runs (unlike `var`,
            // which is only visible-but-undefined). Declarations nested in blocks/if
            // branches are excluded here — they keep their existing Annex B.3.3
            // handling in CompileFunctionDeclaration, which assigns to the outer
            // binding only when the enclosing block actually executes.
            var hoistedFunctionStarts = HoistTopLevelFunctionDeclarations();

            // eval's completion value is the value of its last top-level expression
            // statement (ECMA-262 Script/eval completion-value semantics). This is a
            // simplified subset: the value only survives if that expression statement
            // is literally the final statement in the source — it is not threaded
            // through intervening control-flow statements the way a fully spec-
            // compliant implementation would.
            var completionValuePending = false;

            while (lexer.TokenType != ScriptLex.LexTypes.Eof)
            {
                if (hoistedFunctionStarts != null &&
                    lexer.TokenType == ScriptLex.LexTypes.RFunction &&
                    hoistedFunctionStarts.Contains(lexer.TokenStart))
                {
                    SetLine();
                    SkipHoistedFunctionDeclaration();
                    continue;
                }

                if (StartsExpressionStatement(lexer.TokenType))
                {
                    SetLine();
                    CompileExpression();
                    MatchStatementTerminator();

                    if (lexer.TokenType == ScriptLex.LexTypes.Eof)
                        completionValuePending = true;
                    else
                        chunk.Emit(OpCode.Pop);

                    continue;
                }

                CompileStatement();
            }

            if (!completionValuePending) chunk.Emit(OpCode.PushUndefined);
            chunk.Emit(OpCode.Return);

            AnalyzeSlotsAndCaptures(chunk);

            if (ScriptEngine.EnableLocalSlots) chunk.PromoteLocalSlots();

            if (EnableOptimizer)
            {
                chunk.CollapseJumpChains();
                chunk.FoldConstantBranches();
                chunk.EliminateDeadCode();
                chunk.FuseSuperInstructions();
                chunk.NarrowEncodePass();
                _constScopes = null;
            }
            _evalLetConflicts = null;
            return chunk;
        }

        // Collect all top-level let/const binding names from a source string.
        // Used by CompileEvalProgram to detect Annex B.3.3 conflicts.
        private static System.Collections.Generic.HashSet<string> CollectLetConstNames(string source)
        {
            var names = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            try
            {
                using var scan = new ScriptLex(source);
                while (scan.TokenType != ScriptLex.LexTypes.Eof)
                {
                    if (scan.TokenType is ScriptLex.LexTypes.RLet or ScriptLex.LexTypes.RConst)
                    {
                        scan.Match(scan.TokenType);
                        if (scan.TokenType == ScriptLex.LexTypes.Id)
                            names.Add(scan.TokenString);
                    }
                    scan.Match(scan.TokenType);
                }
            }
            catch { /* ignore scanner errors in the pre-pass */ }
            return names;
        }

        // True for a token that starts CompileStatement's default (expression
        // statement) branch — i.e. every statement-leading token NOT explicitly
        // handled by one of its other cases. Kept in sync with that switch.
        private static bool StartsExpressionStatement(ScriptLex.LexTypes t) => t is not (
            (ScriptLex.LexTypes)'{' or (ScriptLex.LexTypes)';' or
            ScriptLex.LexTypes.RVar or ScriptLex.LexTypes.RConst or ScriptLex.LexTypes.RLet or
            ScriptLex.LexTypes.RClass or ScriptLex.LexTypes.RIf or ScriptLex.LexTypes.RWhile or
            ScriptLex.LexTypes.RDo or ScriptLex.LexTypes.RFor or ScriptLex.LexTypes.RReturn or
            ScriptLex.LexTypes.RBreak or ScriptLex.LexTypes.RContinue or ScriptLex.LexTypes.RSwitch or
            ScriptLex.LexTypes.RFunction or ScriptLex.LexTypes.RAsync or ScriptLex.LexTypes.RExport or
            ScriptLex.LexTypes.RImport or ScriptLex.LexTypes.RThrow or ScriptLex.LexTypes.RTry
        ) && t != ScriptLex.LexTypes.Eof;

        // Pre-scan the upcoming token stream (from the current lexer position to
        // EOF) for `function` declarations that sit at depth 0 — i.e. directly in
        // the eval program's top-level statement list, not nested inside a block,
        // if-branch, loop body, etc. For each one found, compile it immediately
        // (via a throwaway clone positioned at its start) so the closure is
        // created and assigned before any other top-level statement runs. Returns
        // the set of token-start offsets that were hoisted, so the main compile
        // loop can recognise and skip them when it reaches them in source order;
        // null if there was nothing to hoist.
        private System.Collections.Generic.HashSet<int> HoistTopLevelFunctionDeclarations()
        {
            var starts = new List<int>();
            var scan = lexer.CloneToEnd(lexer.TokenStart);
            var depth = 0;
            while (scan.TokenType != ScriptLex.LexTypes.Eof)
            {
                if (depth == 0 && scan.TokenType == ScriptLex.LexTypes.RFunction)
                    starts.Add(scan.TokenStart);

                if (scan.TokenType is (ScriptLex.LexTypes)'(' or (ScriptLex.LexTypes)'[' or (ScriptLex.LexTypes)'{')
                    depth++;
                else if (scan.TokenType is (ScriptLex.LexTypes)')' or (ScriptLex.LexTypes)']' or (ScriptLex.LexTypes)'}')
                { if (depth > 0) depth--; }

                scan.GetNextToken();
            }

            if (starts.Count == 0) return null;

            var hoisted = new System.Collections.Generic.HashSet<int>();
            var saved = lexer;
            foreach (var start in starts)
            {
                lexer = saved.CloneToEnd(start);
                SetLine();
                CompileFunctionDeclaration();
                hoisted.Add(start);
            }
            lexer = saved;
            return hoisted;
        }

        // Consume the tokens of a function declaration already handled by
        // HoistTopLevelFunctionDeclarations without re-emitting it — its closure
        // creation and assignment already ran before the first statement.
        private void SkipHoistedFunctionDeclaration()
        {
            var savedChunk = chunk;
            chunk = new Chunk { Name = "<hoisted>" };
            CompileFunctionDeclaration();
            chunk = savedChunk;
        }

        // ----- Lever A: local-slot analysis (phase A1, metadata only) ----------

        // Record `name` as a lexical local of the function currently being compiled,
        // assigning it the next free frame slot. Idempotent per (chunk, name): the first
        // declaration wins, later re-declarations/shadows in nested blocks reuse it for
        // the function-level summary (per-occurrence resolution is a phase-A2 concern).
        private void RecordLocalSlot(string name)
        {
            if (chunk.SlotMap.ContainsKey(name)) return;
            chunk.SlotMap[name] = chunk.SlotCount++;
        }

        // Assign slots 0..N-1 to a function's parameters (call once after the parameter
        // list is parsed, before the body, so params occupy the lowest slots).
        private static void AssignParameterSlots(Chunk fnChunk)
        {
            foreach (var p in fnChunk.Parameters)
                if (!fnChunk.SlotMap.ContainsKey(p))
                    fnChunk.SlotMap[p] = fnChunk.SlotCount++;
        }

        // Post-compile pass over the chunk tree: mark a function's slots captured when a
        // nested function (any depth) references the same name, and disable slotting for
        // functions that can introduce bindings dynamically (direct eval). Conservative —
        // over-marking a capture only costs a cell; it is never unsafe.
        private static void AnalyzeSlotsAndCaptures(Chunk c)
        {
            foreach (var child in c.Functions)
                AnalyzeSlotsAndCaptures(child);

            // Direct eval (or a parameter/var literally named eval) can add bindings at
            // runtime; fall back to the name-based path for the whole function.
            if (c.Names.Contains("eval"))
                c.SlotEligible = false;

            if (c.SlotMap.Count == 0) return;

            // Union of every descendant function's referenced names.
            var referenced = new HashSet<string>();
            CollectDescendantNames(c, referenced);

            foreach (var kv in c.SlotMap)
                if (referenced.Contains(kv.Key))
                    c.CapturedSlots.Add(kv.Value);
        }

        private static void CollectDescendantNames(Chunk c, HashSet<string> into)
        {
            foreach (var child in c.Functions)
            {
                foreach (var n in child.Names) into.Add(n);
                CollectDescendantNames(child, into);
            }
        }

        // ----- strict-mode directive detection ---------------------------------

        private void ConsumeUseStrictIfPresent()
        {
            if (lexer.TokenType != ScriptLex.LexTypes.Str) return;
            if (lexer.TokenString != "use strict") return;
            lexer.Match(ScriptLex.LexTypes.Str);
            if (lexer.TokenType == (ScriptLex.LexTypes)';')
                lexer.Match((ScriptLex.LexTypes)';');
            chunk.IsStrict = true;
        }

        // ----- precedence chain (mirrors ScriptEngine.* tiers) --------------

        private void CompileBase()
        {
            CompileTernary(true);
        }

        // Decide whether a just-compiled function/method/arrow body needs its
        // `arguments` object materialised at call time. It does if it references
        // `arguments` (its name is in the name table — conservatively including any
        // property named `arguments`), uses `eval` (which could access it
        // dynamically), or already had the flag set by a nested arrow. An arrow has
        // no own `arguments`, so its use is attributed to the enclosing scope.
        // Called as each function chunk is finalised; `enclosing` is the chunk being
        // restored to. Over-setting is always safe; under-setting would lose access.
        private static void FinalizeArgumentsUsage(Chunk fnChunk, Chunk enclosing)
        {
            if (fnChunk.Names.Contains("arguments") || fnChunk.Names.Contains("eval"))
                fnChunk.UsesArguments = true;

            if (fnChunk.IsArrow && fnChunk.UsesArguments && enclosing != null)
                enclosing.UsesArguments = true;
        }

        // The comma (sequence) operator — the lowest-precedence Expression production:
        //   Expression : AssignmentExpression ( ',' AssignmentExpression )*
        // Each operand is evaluated left-to-right; all but the last result are
        // discarded and the value of the whole expression is the last operand.
        //
        // This is only valid where the grammar allows a full Expression (expression
        // statements, parenthesised groupings, return/throw operands, the for-header
        // and the if/while/switch conditions). Contexts where ',' is a separator —
        // argument lists, array/object literals, variable declarators, arrow params —
        // keep calling CompileBase (AssignmentExpression) so the comma is not consumed
        // here.
        private void CompileExpression()
        {
            CompileBase();
            while (lexer.TokenType == (ScriptLex.LexTypes)',')
            {
                chunk.Emit(OpCode.Pop);            // discard the preceding operand's value
                lexer.Match((ScriptLex.LexTypes)',');
                CompileBase();                     // next AssignmentExpression
            }
        }

        // Consume the semicolon that terminates a statement, applying Automatic
        // Semicolon Insertion (ASI): when there is no explicit ';', a statement is
        // still terminated if the next token is '}' or end-of-input, or if a line
        // terminator precedes it. Otherwise this surfaces the normal "expected ;"
        // error. Use only for statement terminators — never for the syntactic ';'
        // separators inside a C-style for-header.
        private void MatchStatementTerminator()
        {
            if (lexer.TokenType == (ScriptLex.LexTypes)';')
            {
                lexer.Match((ScriptLex.LexTypes)';');
                return;
            }

            if (lexer.TokenType == (ScriptLex.LexTypes)'}' ||
                lexer.TokenType == ScriptLex.LexTypes.Eof ||
                lexer.NewlineBeforeToken)
                return; // ASI — leave the terminating token for the enclosing context

            lexer.Match((ScriptLex.LexTypes)';'); // no ASI applies: report the error
        }

        private void CompileTernary(bool canAssign)
        {
            CompileLogic(canAssign);

            if (lexer.TokenType != (ScriptLex.LexTypes)'?') return;

            lexer.Match((ScriptLex.LexTypes)'?');

            var toElse = chunk.EmitJump(OpCode.JumpIfFalse);
            CompileBase();                       // then arm
            var toEnd = chunk.EmitJump(OpCode.Jump);

            chunk.PatchJump(toElse);
            lexer.Match((ScriptLex.LexTypes)':');
            CompileBase();                       // else arm

            chunk.PatchJump(toEnd);
        }

        private void CompileLogic(bool canAssign)
        {
            CompileCondition(canAssign);

            while (lexer.TokenType is
                   (ScriptLex.LexTypes)'&' or
                   (ScriptLex.LexTypes)'|' or
                   (ScriptLex.LexTypes)'^' or
                   ScriptLex.LexTypes.AndAnd or
                   ScriptLex.LexTypes.OrOr or
                   ScriptLex.LexTypes.NullCoalesce)
            {
                var op = lexer.TokenType;
                lexer.Match(op);

                switch (op)
                {
                    case ScriptLex.LexTypes.AndAnd:
                    {
                        var end = chunk.EmitJump(OpCode.JumpIfFalseOrPop);
                        CompileCondition(false);
                        chunk.PatchJump(end);
                        break;
                    }
                    case ScriptLex.LexTypes.OrOr:
                    {
                        var end = chunk.EmitJump(OpCode.JumpIfTrueOrPop);
                        CompileCondition(false);
                        chunk.PatchJump(end);
                        break;
                    }
                    case ScriptLex.LexTypes.NullCoalesce:
                    {
                        // a ?? b — return a if a is not null/undefined, else return b.
                        // JumpIfNullOrUndefined: pop; if null/undef push undefined + jump; else push back.
                        // Non-null path: push a back, Jump → end.
                        // Null path (at rhs): discard the pushed undefined, compile b.
                        var toRhs = chunk.EmitJump(OpCode.JumpIfNullOrUndefined);
                        var toEnd = chunk.EmitJump(OpCode.Jump);
                        chunk.PatchJump(toRhs);
                        chunk.Emit(OpCode.Pop); // discard the undefined JumpIfNullOrUndefined pushed
                        CompileCondition(false);
                        chunk.PatchJump(toEnd);
                        break;
                    }
                    default:
                        CompileCondition(false);
                        chunk.Emit(OpCode.Binary, (int)op);
                        break;
                }
            }
        }

        private void CompileCondition(bool canAssign)
        {
            CompileShift(canAssign);

            while (lexer.TokenType is
                   ScriptLex.LexTypes.Equal or
                   ScriptLex.LexTypes.NEqual or
                   ScriptLex.LexTypes.TypeEqual or
                   ScriptLex.LexTypes.NTypeEqual or
                   ScriptLex.LexTypes.LEqual or
                   ScriptLex.LexTypes.GEqual or
                   ScriptLex.LexTypes.RInstanceOf or
                   ScriptLex.LexTypes.RIn or
                   (ScriptLex.LexTypes)'>' or
                   (ScriptLex.LexTypes)'<')
            {
                var op = lexer.TokenType;
                lexer.Match(op);
                var operandStart = chunk.Count;
                CompileShift(false);

                switch (op)
                {
                    case ScriptLex.LexTypes.RInstanceOf:
                        chunk.Emit(OpCode.InstanceOf);
                        break;
                    case ScriptLex.LexTypes.RIn:
                        chunk.Emit(OpCode.In);
                        break;
                    default:
                        EmitBinary((int)op, operandStart);
                        break;
                }
            }
        }

        // Emit a Binary op. Applies optimizations in priority order:
        //   1. Fuse lone Constant right operand → BinaryConst (pool-indexed)     [always on]
        //   2. If left is also Constant → fold entirely to single Constant        [EnableOptimizer]
        //   3. If right constant is an int → upgrade to BinaryIntConst (inline)  [EnableOptimizer]
        private void EmitBinary(int op, int operandStart)
        {
            if (!chunk.TryFuseConstantBinary(operandStart, op))
            {
                chunk.Emit(OpCode.Binary, op);
                return;
            }
            if (!EnableOptimizer) return;
            if (chunk.TryFoldBinaryConst()) return;
            chunk.TryUpgradeBinaryConstToInt();
        }

        private void CompileShift(bool canAssign)
        {
            CompileAdditive(canAssign);

            while (lexer.TokenType is
                   ScriptLex.LexTypes.LShift or
                   ScriptLex.LexTypes.RShift or
                   ScriptLex.LexTypes.RShiftUnsigned)
            {
                var op = lexer.TokenType;
                lexer.Match(op);
                CompileAdditive(false);
                chunk.Emit(OpCode.Shift, (int)op);
            }
        }

        private void CompileAdditive(bool canAssign)
        {
            CompileTerm(canAssign);

            while (lexer.TokenType is (ScriptLex.LexTypes)'+' or (ScriptLex.LexTypes)'-')
            {
                var op = lexer.TokenType;
                lexer.Match(op);
                var operandStart = chunk.Count;
                CompileTerm(false);
                EmitBinary((int)op, operandStart);
            }
        }

        private void CompileTerm(bool canAssign)
        {
            CompileExponent(canAssign);

            while (lexer.TokenType is
                   (ScriptLex.LexTypes)'*' or
                   (ScriptLex.LexTypes)'/' or
                   (ScriptLex.LexTypes)'%')
            {
                var op = lexer.TokenType;
                lexer.Match(op);
                var operandStart = chunk.Count;
                CompileExponent(false);
                EmitBinary((int)op, operandStart);
            }
        }

        private void CompileExponent(bool canAssign)
        {
            // ES2016: unary operators may not appear immediately before ** without parens.
            // Detect a prefix-unary token now, before consuming it via CompileUnary.
            var hasUnaryPrefix = lexer.TokenType is
                (ScriptLex.LexTypes)'!' or
                (ScriptLex.LexTypes)'~' or
                (ScriptLex.LexTypes)'-' or
                (ScriptLex.LexTypes)'+' or
                ScriptLex.LexTypes.RTypeOf or
                ScriptLex.LexTypes.RVoid or
                ScriptLex.LexTypes.PlusPlus or
                ScriptLex.LexTypes.MinusMinus or
                ScriptLex.LexTypes.RDelete;

            CompileUnary(canAssign);

            if (lexer.TokenType == ScriptLex.LexTypes.Power)
            {
                if (hasUnaryPrefix)
                    throw new ScriptException(
                        "Unary operator used immediately before **. Wrap the left-hand side in parentheses.");
                lexer.Match(ScriptLex.LexTypes.Power);
                var operandStart = chunk.Count;
                CompileExponent(false); // right-associative: recurse
                EmitBinary((int)ScriptLex.LexTypes.Power, operandStart);
            }
        }

        private void CompileUnary(bool canAssign)
        {
            switch (lexer.TokenType)
            {
                case (ScriptLex.LexTypes)'!':
                    lexer.Match((ScriptLex.LexTypes)'!');
                    CompileUnary(false);
                    chunk.Emit(OpCode.Not);
                    break;
                case (ScriptLex.LexTypes)'~':
                    lexer.Match((ScriptLex.LexTypes)'~');
                    CompileUnary(false);
                    chunk.Emit(OpCode.BitNot);
                    break;
                case (ScriptLex.LexTypes)'-':
                    lexer.Match((ScriptLex.LexTypes)'-');
                    CompileUnary(false);
                    chunk.Emit(OpCode.Negate);
                    break;
                case (ScriptLex.LexTypes)'+':
                    lexer.Match((ScriptLex.LexTypes)'+');
                    CompileUnary(false);
                    chunk.Emit(OpCode.ToNumber);
                    break;
                case ScriptLex.LexTypes.RTypeOf:
                    lexer.Match(ScriptLex.LexTypes.RTypeOf);
                    CompileUnary(false);
                    chunk.Emit(OpCode.Typeof);
                    break;
                case ScriptLex.LexTypes.RVoid:
                    // void evaluates its operand for side effects, then yields undefined.
                    lexer.Match(ScriptLex.LexTypes.RVoid);
                    CompileUnary(false);
                    chunk.Emit(OpCode.Pop);
                    chunk.Emit(OpCode.PushUndefined);
                    break;
                case ScriptLex.LexTypes.PlusPlus:
                case ScriptLex.LexTypes.MinusMinus:
                    CompilePrefixIncrement();
                    break;
                case ScriptLex.LexTypes.RDelete:
                    CompileDelete();
                    break;
                default:
                    CompileFactor(canAssign);
                    break;
            }
        }

        // ++a / --a on a simple variable: a = a +/- 1, value is the new value.
        private void CompilePrefixIncrement()
        {
            var op = lexer.TokenType == ScriptLex.LexTypes.PlusPlus ? (ScriptLex.LexTypes)'+' : (ScriptLex.LexTypes)'-';
            lexer.Match(lexer.TokenType);

            var name = lexer.TokenString;
            lexer.Match(ScriptLex.LexTypes.Id);

            chunk.Emit(OpCode.GetVar, chunk.AddName(name));
            EmitConstantInt(1);
            chunk.Emit(OpCode.Binary, (int)op);
            chunk.Emit(OpCode.SetVar, chunk.AddName(name)); // leaves the new value
        }

        private void EmitConstantInt(int value)
        {
            chunk.Emit(OpCode.Constant, chunk.AddConstant(ConstantValue.Int(value)));
        }

        // Record the current lexer line and column into the active chunk so all bytes
        // emitted for the upcoming statement/expression carry a source location.
        private void SetLine() => chunk.SetCurrentLine(lexer.LineNumber, lexer.ColumnNumber);

        // delete obj.prop / obj[key] : the final member segment is removed.
        private void CompileDelete()
        {
            lexer.Match(ScriptLex.LexTypes.RDelete);

            var name = lexer.TokenString;
            lexer.Match(ScriptLex.LexTypes.Id);
            chunk.Emit(OpCode.GetVar, chunk.AddName(name));

            if (lexer.TokenType is not ((ScriptLex.LexTypes)'.' or (ScriptLex.LexTypes)'['))
            {
                if (chunk.IsStrict)
                    throw new ScriptException($"SyntaxError: Cannot delete variable '{name}' in strict mode");
                // delete of a bare variable is unsupported; yields false
                chunk.Emit(OpCode.Pop);
                chunk.Emit(OpCode.PushFalse);
                return;
            }

            while (true)
            {
                if (lexer.TokenType == (ScriptLex.LexTypes)'.')
                {
                    lexer.Match((ScriptLex.LexTypes)'.');
                    var idx = chunk.AddName(lexer.TokenString);
                    lexer.Match(ScriptLex.LexTypes.Id);

                    if (lexer.TokenType is (ScriptLex.LexTypes)'.' or (ScriptLex.LexTypes)'[')
                    {
                        chunk.Emit(OpCode.GetProp, idx);
                    }
                    else
                    {
                        chunk.Emit(OpCode.DeleteProp, idx);
                        return;
                    }
                }
                else
                {
                    lexer.Match((ScriptLex.LexTypes)'[');
                    CompileBase();
                    lexer.Match((ScriptLex.LexTypes)']');

                    if (lexer.TokenType is (ScriptLex.LexTypes)'.' or (ScriptLex.LexTypes)'[')
                    {
                        chunk.Emit(OpCode.GetIndex);
                    }
                    else
                    {
                        chunk.Emit(OpCode.DeleteIndex);
                        return;
                    }
                }
            }
        }

        // Generic depth-tracking lookahead. Clones the lexer and scans forward,
        // tracking bracket depth for all six pairs: (, [, { / ), ], }.
        // Callbacks are evaluated BEFORE the depth update for the current token.
        // Returns true when isMatch fires, false when isStop fires or EOF is reached.
        private bool LookaheadScan(
            Func<ScriptLex.LexTypes, int, bool> isMatch,
            Func<ScriptLex.LexTypes, int, bool> isStop = null)
        {
            var clone = lexer.CloneToEnd(lexer.TokenStart);
            var depth = 0;
            while (clone.TokenType != ScriptLex.LexTypes.Eof)
            {
                var t = clone.TokenType;
                if (isStop?.Invoke(t, depth) == true) return false;
                if (isMatch(t, depth)) return true;
                if (t is (ScriptLex.LexTypes)'(' or (ScriptLex.LexTypes)'[' or (ScriptLex.LexTypes)'{') depth++;
                else if (t is (ScriptLex.LexTypes)')' or (ScriptLex.LexTypes)']' or (ScriptLex.LexTypes)'}')
                { if (depth > 0) depth--; }
                clone.GetNextToken();
            }
            return false;
        }

        // Temporarily swap in a fresh lexer for `source`, call `body()`, then restore.
        private void CompileInSubLexer(string source, Action body)
        {
            var saved = lexer;
            using var sub = new ScriptLex(source);
            lexer = sub;
            body();
            lexer = saved;
        }

        // Scan for a top-level `await` token — one that appears outside any
        // function/class body. Tracks nesting by counting `function`/`class`
        // keywords to enter a nested scope and `}` tokens to exit.
        // Arrow functions are not counted because they use no keyword at their
        // open — so `await` inside an arrow body counts as top-level. This is
        // an intentional conservative choice: if you use arrow bodies at the
        // top level, the wrapper is added which is safe. If that's too broad
        // for a future use-case, a full AST pass would be required.
        private static bool HasTopLevelAwait(string source)
        {
            int depth = 0;
            using var lex = new ScriptLex(source);
            while (lex.TokenType != ScriptLex.LexTypes.Eof)
            {
                var t = lex.TokenType;
                if (t == ScriptLex.LexTypes.RFunction || t == ScriptLex.LexTypes.RClass)
                    depth++;
                else if (t == (ScriptLex.LexTypes)'{' && depth > 0)
                    depth++;
                else if (t == (ScriptLex.LexTypes)'}' && depth > 0)
                    depth--;
                else if (t == ScriptLex.LexTypes.RAwait && depth == 0)
                    return true;
                lex.GetNextToken();
            }
            return false;
        }
    }
}
