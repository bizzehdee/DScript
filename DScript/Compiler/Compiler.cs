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

            CompileBase();
            chunk.Emit(OpCode.Return);

            if (EnableOptimizer)
            {
                chunk.CollapseJumpChains();
                chunk.EliminateDeadCode();
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

            while (lexer.TokenType != ScriptLex.LexTypes.Eof)
            {
                CompileStatement();
            }

            chunk.Emit(OpCode.PushUndefined);
            chunk.Emit(OpCode.Return);

            if (EnableOptimizer)
            {
                chunk.CollapseJumpChains();
                chunk.EliminateDeadCode();
                chunk.NarrowEncodePass();
                _constScopes = null;
            }
            return chunk;
        }

        // ----- precedence chain (mirrors ScriptEngine.* tiers) --------------

        private void CompileBase()
        {
            CompileTernary(true);
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
            CompileUnary(canAssign);

            while (lexer.TokenType is
                   (ScriptLex.LexTypes)'*' or
                   (ScriptLex.LexTypes)'/' or
                   (ScriptLex.LexTypes)'%')
            {
                var op = lexer.TokenType;
                lexer.Match(op);
                var operandStart = chunk.Count;
                CompileUnary(false);
                EmitBinary((int)op, operandStart);
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
