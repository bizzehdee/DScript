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

using System.Collections.Generic;
using DScript.Vm;

namespace DScript.Compiler
{
    public sealed partial class DScriptCompiler
    {
        private sealed class LoopContext
        {
            public readonly bool IsSwitch;
            public List<int> BreakJumps { get; } = [];
            public List<int> ContinueJumps { get; } = [];

            public LoopContext(bool isSwitch = false) => IsSwitch = isSwitch;
        }

        private Stack<LoopContext> loops = new();
        private int forInCounter;

        // One entry per enclosing try-with-finally block. Tracks pending return/goto
        // jumps (LeaveTry operand offsets) that must be backpatched to the finally PC
        // once it is known. Pushed in CompileTry, popped after the finally block.
        private sealed class FinallyContext
        {
            public readonly List<int> ReturnJumps = []; // LeaveTry operand offsets for `return`
        }
        private Stack<FinallyContext> finallyStack = new();

        private void CompileStatement()
        {
            SetLine();
            switch (lexer.TokenType)
            {
                case (ScriptLex.LexTypes)'{':
                    CompileBlock();
                    break;
                case (ScriptLex.LexTypes)';':
                    lexer.Match((ScriptLex.LexTypes)';');
                    break;
                case ScriptLex.LexTypes.RVar:
                case ScriptLex.LexTypes.RConst:
                case ScriptLex.LexTypes.RLet:
                    CompileVarDeclaration();
                    break;
                case ScriptLex.LexTypes.RClass:
                    CompileClass();
                    break;
                case ScriptLex.LexTypes.RIf:
                    CompileIf();
                    break;
                case ScriptLex.LexTypes.RWhile:
                    CompileWhile();
                    break;
                case ScriptLex.LexTypes.RDo:
                    CompileDoWhile();
                    break;
                case ScriptLex.LexTypes.RFor:
                    CompileFor();
                    break;
                case ScriptLex.LexTypes.RReturn:
                    CompileReturn();
                    break;
                case ScriptLex.LexTypes.RBreak:
                    lexer.Match(ScriptLex.LexTypes.RBreak);
                    lexer.Match((ScriptLex.LexTypes)';');
                    if (loops.Count > 0) loops.Peek().BreakJumps.Add(chunk.EmitJump(OpCode.Jump));
                    break;
                case ScriptLex.LexTypes.RContinue:
                    lexer.Match(ScriptLex.LexTypes.RContinue);
                    lexer.Match((ScriptLex.LexTypes)';');
                    // `continue` skips switch contexts and targets the nearest real loop.
                    foreach (var loopCtx in loops)
                    {
                        if (!loopCtx.IsSwitch) { loopCtx.ContinueJumps.Add(chunk.EmitJump(OpCode.Jump)); break; }
                    }
                    break;
                case ScriptLex.LexTypes.RSwitch:
                    CompileSwitch();
                    break;
                case ScriptLex.LexTypes.RFunction:
                    CompileFunctionDeclaration();
                    break;
                case ScriptLex.LexTypes.RAsync:
                    CompileAsyncFunctionDeclaration();
                    break;
                case ScriptLex.LexTypes.RExport:
                    CompileExport();
                    break;
                case ScriptLex.LexTypes.RImport:
                    CompileImport();
                    break;
                case ScriptLex.LexTypes.RThrow:
                    CompileThrow();
                    break;
                case ScriptLex.LexTypes.RTry:
                    CompileTry();
                    break;
                default:
                    // expression statement: evaluate and discard the value
                    CompileBase();
                    chunk.Emit(OpCode.Pop);
                    lexer.Match((ScriptLex.LexTypes)';');
                    break;
            }
        }

        private void CompileBlock()
        {
            lexer.Match((ScriptLex.LexTypes)'{');

            // If the block contains any `let` or `const` declarations at the top
            // level of this block (depth 0), we need a dedicated scope frame so
            // those bindings are invisible outside the braces.
            var needsBlockScope = PeekHasLetOrConst();
            if (needsBlockScope) chunk.Emit(OpCode.EnterBlock);

            // Push a propagation scope so `const` declarations inside this block
            // are visible within it but not after the closing brace.
            _constScopes?.Push(new Dictionary<string, ConstantValue>());

            while (lexer.TokenType != (ScriptLex.LexTypes)'}' && lexer.TokenType != ScriptLex.LexTypes.Eof)
            {
                CompileStatement();
            }

            _constScopes?.Pop();

            if (needsBlockScope) chunk.Emit(OpCode.LeaveBlock);
            lexer.Match((ScriptLex.LexTypes)'}');
        }

        // Lookahead: does the current block (starting just after '{') contain any
        // `let` or `const` declaration at depth 0 (not nested in inner braces)?
        private bool PeekHasLetOrConst()
        {
            var clone = lexer.CloneToEnd(lexer.TokenStart);
            var depth = 0;
            while (clone.TokenType != ScriptLex.LexTypes.Eof)
            {
                var t = clone.TokenType;
                if (t == (ScriptLex.LexTypes)'}' && depth == 0) break;
                if (depth == 0 && (t == ScriptLex.LexTypes.RLet || t == ScriptLex.LexTypes.RConst))
                    return true;
                if (t == (ScriptLex.LexTypes)'{') depth++;
                else if (t == (ScriptLex.LexTypes)'}') depth--;
                clone.GetNextToken();
            }
            return false;
        }

        private void CompileVarDeclaration()
        {
            var readOnly = lexer.TokenType == ScriptLex.LexTypes.RConst;
            var isLet    = lexer.TokenType == ScriptLex.LexTypes.RLet;
            lexer.Match(lexer.TokenType);

            while (lexer.TokenType != (ScriptLex.LexTypes)';')
            {
                if (lexer.TokenType == (ScriptLex.LexTypes)'[')
                {
                    // Array destructuring: const [a, b, c] = expr
                    CompileArrayDestructuring(readOnly, isLet);
                }
                else if (lexer.TokenType == (ScriptLex.LexTypes)'{')
                {
                    // Object destructuring: const { x, y } = expr
                    CompileObjectDestructuring(readOnly, isLet);
                }
                else
                {
                    var name = lexer.TokenString;
                    lexer.Match(ScriptLex.LexTypes.Id);
                    var nameIndex = chunk.AddName(name);

                    // `const` → DeclareConst (read-only binding in innermost scope)
                    // `let`   → DeclareLocal (mutable binding in innermost scope, no hoisting)
                    // `var`   → DeclareVar   (mutable binding hoisted past block scopes)
                    OpCode declOp = readOnly ? OpCode.DeclareConst
                                             : isLet ? OpCode.DeclareLocal
                                                     : OpCode.DeclareVar;
                    chunk.Emit(declOp, nameIndex);

                    if (lexer.TokenType == (ScriptLex.LexTypes)'=')
                    {
                        lexer.Match((ScriptLex.LexTypes)'=');
                        var baseStart = chunk.Count;
                        CompileBase();
                        // Record the constant value for propagation if the initialiser
                        // compiled to exactly one Constant instruction.
                        if (readOnly) TryRecordConstPropagation(name, baseStart);
                        chunk.Emit(OpCode.SetVar, nameIndex);
                        chunk.Emit(OpCode.Pop);
                    }
                }

                if (lexer.TokenType != (ScriptLex.LexTypes)';')
                {
                    lexer.Match((ScriptLex.LexTypes)',');
                }
            }

            lexer.Match((ScriptLex.LexTypes)';');
        }

        // Array destructuring: [a, b, ...rest] = expr
        // Compiles the RHS, then for each binding emits a GetIndex + declare + assign
        private void CompileArrayDestructuring(bool readOnly, bool isLet)
        {
            // Collect bindings
            var bindings = new System.Collections.Generic.List<(string Name, bool IsRest, string DefaultSrc)>();

            lexer.Match((ScriptLex.LexTypes)'[');
            while (lexer.TokenType != (ScriptLex.LexTypes)']')
            {
                if (lexer.TokenType == ScriptLex.LexTypes.Ellipsis)
                {
                    lexer.Match(ScriptLex.LexTypes.Ellipsis);
                    var restName = lexer.TokenString;
                    lexer.Match(ScriptLex.LexTypes.Id);
                    bindings.Add((restName, true, null));
                    break; // rest must be last
                }

                var name = lexer.TokenString;
                lexer.Match(ScriptLex.LexTypes.Id);

                string defaultSrc = null;
                if (lexer.TokenType == (ScriptLex.LexTypes)'=')
                {
                    lexer.Match((ScriptLex.LexTypes)'=');
                    defaultSrc = ReadDefaultExpression();
                }

                bindings.Add((name, false, defaultSrc));
                if (lexer.TokenType != (ScriptLex.LexTypes)']') lexer.Match((ScriptLex.LexTypes)',');
            }
            lexer.Match((ScriptLex.LexTypes)']');
            lexer.Match((ScriptLex.LexTypes)'=');

            // Use a temporary variable to hold the RHS array
            var tmpName = "$destr" + _destructureCounter++;
            var tmpIdx  = chunk.AddName(tmpName);
            chunk.Emit(OpCode.DeclareVar, tmpIdx);
            CompileBase();  // RHS value
            chunk.Emit(OpCode.SetVar, tmpIdx);
            chunk.Emit(OpCode.Pop);

            OpCode declOp = readOnly ? OpCode.DeclareConst : isLet ? OpCode.DeclareLocal : OpCode.DeclareVar;

            for (var i = 0; i < bindings.Count; i++)
            {
                var (name, isRest, defaultSrc) = bindings[i];
                var nameIdx = chunk.AddName(name);
                chunk.Emit(declOp, nameIdx);

                if (isRest)
                {
                    // Collect tmp[i], tmp[i+1], ... into a new array
                    var restArrName = "$restArr" + _destructureCounter++;
                    var counterName = "$rc" + _destructureCounter++;
                    var restArrIdx  = chunk.AddName(restArrName);
                    var counterIdx  = chunk.AddName(counterName);

                    // restArr = []
                    chunk.Emit(OpCode.DeclareVar, restArrIdx);
                    chunk.Emit(OpCode.NewArray);
                    chunk.Emit(OpCode.SetVar, restArrIdx);
                    chunk.Emit(OpCode.Pop);

                    // counter = i (the index to start from)
                    chunk.Emit(OpCode.DeclareVar, counterIdx);
                    EmitConstantInt(i);
                    chunk.Emit(OpCode.SetVar, counterIdx);
                    chunk.Emit(OpCode.Pop);

                    // while (counter < tmp.length):
                    var loopTop = chunk.Count;
                    chunk.Emit(OpCode.GetVar, counterIdx);
                    chunk.Emit(OpCode.GetVar, tmpIdx);
                    chunk.Emit(OpCode.GetProp, chunk.AddName("length"));
                    chunk.Emit(OpCode.Binary, (int)(ScriptLex.LexTypes)'<');
                    var loopExit = chunk.EmitJump(OpCode.JumpIfFalse);

                    //   restArr[restArr.length] = tmp[counter]
                    chunk.Emit(OpCode.GetVar, restArrIdx);   // restArr
                    chunk.Emit(OpCode.Dup);                   // restArr, restArr
                    chunk.Emit(OpCode.GetProp, chunk.AddName("length")); // restArr, len
                    chunk.Emit(OpCode.GetVar, tmpIdx);        // restArr, len, tmp
                    chunk.Emit(OpCode.GetVar, counterIdx);    // restArr, len, tmp, counter
                    chunk.Emit(OpCode.GetIndex);              // restArr, len, tmp[counter]
                    chunk.Emit(OpCode.SetPropDynamic);        // restArr (restArr[len] = tmp[counter])
                    chunk.Emit(OpCode.Pop);                   // (discard restArr)

                    //   counter++
                    chunk.Emit(OpCode.GetVar, counterIdx);
                    EmitConstantInt(1);
                    chunk.Emit(OpCode.Binary, (int)(ScriptLex.LexTypes)'+');
                    chunk.Emit(OpCode.SetVar, counterIdx);
                    chunk.Emit(OpCode.Pop);

                    chunk.Emit(OpCode.Jump, loopTop);
                    chunk.PatchJump(loopExit);

                    // name = restArr
                    chunk.Emit(OpCode.GetVar, restArrIdx);
                    chunk.Emit(OpCode.SetVar, nameIdx);
                    chunk.Emit(OpCode.Pop);
                }
                else
                {
                    // Regular binding: name = tmp[i]
                    chunk.Emit(OpCode.GetVar, tmpIdx);
                    EmitConstantInt(i);
                    chunk.Emit(OpCode.GetIndex);
                    chunk.Emit(OpCode.SetVar, nameIdx);
                    chunk.Emit(OpCode.Pop);

                    if (defaultSrc != null)
                    {
                        // If the element was undefined, apply default (same pattern as EmitDefaultParamGuards)
                        chunk.Emit(OpCode.GetVar, nameIdx);
                        var skipDefault = chunk.EmitJump(OpCode.JumpIfDefined);
                        var savedLexer = lexer;
                        using var defLexer = new ScriptLex(defaultSrc);
                        lexer = defLexer;
                        CompileBase();
                        lexer = savedLexer;
                        chunk.Emit(OpCode.SetVar, nameIdx);
                        chunk.Emit(OpCode.Pop);
                        chunk.PatchJump(skipDefault);
                    }
                }
            }
        }

        // Object destructuring: { x, y: z, w = 5 } = expr
        private void CompileObjectDestructuring(bool readOnly, bool isLet)
        {
            var bindings = new System.Collections.Generic.List<(string Key, string Name, string DefaultSrc)>();

            lexer.Match((ScriptLex.LexTypes)'{');
            while (lexer.TokenType != (ScriptLex.LexTypes)'}')
            {
                var key = lexer.TokenString;
                if (lexer.TokenType == ScriptLex.LexTypes.Str)
                    lexer.Match(ScriptLex.LexTypes.Str);
                else
                    lexer.Match(ScriptLex.LexTypes.Id);

                var bindName = key; // default: same name as key
                if (lexer.TokenType == (ScriptLex.LexTypes)':')
                {
                    lexer.Match((ScriptLex.LexTypes)':');
                    bindName = lexer.TokenString;
                    lexer.Match(ScriptLex.LexTypes.Id);
                }

                string defaultSrc = null;
                if (lexer.TokenType == (ScriptLex.LexTypes)'=')
                {
                    lexer.Match((ScriptLex.LexTypes)'=');
                    defaultSrc = ReadDefaultExpression();
                }

                bindings.Add((key, bindName, defaultSrc));
                if (lexer.TokenType != (ScriptLex.LexTypes)'}') lexer.Match((ScriptLex.LexTypes)',');
            }
            lexer.Match((ScriptLex.LexTypes)'}');
            lexer.Match((ScriptLex.LexTypes)'=');

            // Temp var to hold the RHS
            var tmpName = "$destr" + _destructureCounter++;
            var tmpIdx  = chunk.AddName(tmpName);
            chunk.Emit(OpCode.DeclareVar, tmpIdx);
            CompileBase();
            chunk.Emit(OpCode.SetVar, tmpIdx);
            chunk.Emit(OpCode.Pop);

            OpCode declOp = readOnly ? OpCode.DeclareConst : isLet ? OpCode.DeclareLocal : OpCode.DeclareVar;

            foreach (var (key, name, defaultSrc) in bindings)
            {
                var nameIdx = chunk.AddName(name);
                chunk.Emit(declOp, nameIdx);

                // Assign tmp.key to name (may be undefined)
                chunk.Emit(OpCode.GetVar, tmpIdx);
                chunk.Emit(OpCode.GetProp, chunk.AddName(key));
                chunk.Emit(OpCode.SetVar, nameIdx);
                chunk.Emit(OpCode.Pop);

                if (defaultSrc != null)
                {
                    // If the property was undefined, apply default (same pattern as EmitDefaultParamGuards)
                    chunk.Emit(OpCode.GetVar, nameIdx);
                    var skipDefault = chunk.EmitJump(OpCode.JumpIfDefined);
                    var savedLexer = lexer;
                    using var defLexer = new ScriptLex(defaultSrc);
                    lexer = defLexer;
                    CompileBase();
                    lexer = savedLexer;
                    chunk.Emit(OpCode.SetVar, nameIdx);
                    chunk.Emit(OpCode.Pop);
                    chunk.PatchJump(skipDefault);
                }
            }
        }

        private void CompileIf()
        {
            lexer.Match(ScriptLex.LexTypes.RIf);
            lexer.Match((ScriptLex.LexTypes)'(');
            CompileBase();
            lexer.Match((ScriptLex.LexTypes)')');

            var toElse = chunk.EmitJump(OpCode.JumpIfFalse);
            CompileStatement();

            if (lexer.TokenType == ScriptLex.LexTypes.RElse)
            {
                var toEnd = chunk.EmitJump(OpCode.Jump);
                chunk.PatchJump(toElse);
                lexer.Match(ScriptLex.LexTypes.RElse);
                CompileStatement();
                chunk.PatchJump(toEnd);
            }
            else
            {
                chunk.PatchJump(toElse);
            }
        }

        private void CompileWhile()
        {
            lexer.Match(ScriptLex.LexTypes.RWhile);
            lexer.Match((ScriptLex.LexTypes)'(');

            var condStart = chunk.Count;
            CompileBase();
            lexer.Match((ScriptLex.LexTypes)')');

            var exitJump = chunk.EmitJump(OpCode.JumpIfFalse);

            loops.Push(new LoopContext());
            CompileStatement();
            chunk.Emit(OpCode.Jump, condStart);

            chunk.PatchJump(exitJump);

            var ctx = loops.Pop();
            PatchJumps(ctx.BreakJumps, chunk.Count);
            PatchJumps(ctx.ContinueJumps, condStart);
        }

        private void CompileDoWhile()
        {
            lexer.Match(ScriptLex.LexTypes.RDo);

            var bodyStart = chunk.Count;
            loops.Push(new LoopContext());
            CompileStatement();

            var condStart = chunk.Count;
            lexer.Match(ScriptLex.LexTypes.RWhile);
            lexer.Match((ScriptLex.LexTypes)'(');
            CompileBase();
            lexer.Match((ScriptLex.LexTypes)')');
            lexer.Match((ScriptLex.LexTypes)';');

            chunk.Emit(OpCode.JumpIfTrue, bodyStart);

            var ctx = loops.Pop();
            PatchJumps(ctx.BreakJumps, chunk.Count);
            PatchJumps(ctx.ContinueJumps, condStart);
        }

        private void CompileReturn()
        {
            lexer.Match(ScriptLex.LexTypes.RReturn);

            if (lexer.TokenType != (ScriptLex.LexTypes)';')
                CompileBase();
            else
                chunk.Emit(OpCode.PushUndefined);

            if (finallyStack.Count > 0)
            {
                // Save the return value off-stack so the finally body cannot corrupt it,
                // then unwind through the innermost enclosing finally. LeaveFinally will
                // chain through any further finally blocks and perform the actual Return.
                chunk.Emit(OpCode.SaveReturn);
                var operandOffset = chunk.EmitJump(OpCode.LeaveTry);
                finallyStack.Peek().ReturnJumps.Add(operandOffset);
            }
            else
            {
                // Tail-call peephole: if the just-compiled return expression ended with
                // a Call or CallMethod, upgrade it to TailCall / TailCallMethod in place.
                // The VM then returns the result immediately without re-entering the
                // dispatch loop, saving one frame of overhead. The Return emitted below
                // becomes dead code and is eliminated by EliminateDeadCode.
                chunk.TryUpgradeLastCallToTailCall();
                chunk.Emit(OpCode.Return);
            }

            lexer.Match((ScriptLex.LexTypes)';');
        }

        private void CompileFor()
        {
            lexer.Match(ScriptLex.LexTypes.RFor);
            lexer.Match((ScriptLex.LexTypes)'(');

            if (IsForIn())
            {
                CompileForIn();
                return;
            }

            if (IsForOf())
            {
                CompileForOf();
                return;
            }

            // C-style: init ; condition ; increment
            CompileStatement(); // init (consumes its ';')

            var condStart = chunk.Count;
            if (lexer.TokenType != (ScriptLex.LexTypes)';')
            {
                CompileBase();
            }
            else
            {
                chunk.Emit(OpCode.PushTrue);
            }
            lexer.Match((ScriptLex.LexTypes)';');

            var exitJump = chunk.EmitJump(OpCode.JumpIfFalse);
            var bodyJump = chunk.EmitJump(OpCode.Jump);

            var incrStart = chunk.Count;
            if (lexer.TokenType != (ScriptLex.LexTypes)')')
            {
                CompileBase();
                chunk.Emit(OpCode.Pop);
            }
            chunk.Emit(OpCode.Jump, condStart);
            lexer.Match((ScriptLex.LexTypes)')');

            chunk.PatchJump(bodyJump);
            loops.Push(new LoopContext());
            CompileStatement(); // body
            chunk.Emit(OpCode.Jump, incrStart);

            chunk.PatchJump(exitJump);

            var ctx = loops.Pop();
            PatchJumps(ctx.BreakJumps, chunk.Count);
            PatchJumps(ctx.ContinueJumps, incrStart);
        }

        private bool IsForIn()
        {
            var lookahead = lexer.CloneToEnd(lexer.TokenStart);
            var depth = 0;
            while (lookahead.TokenType != ScriptLex.LexTypes.Eof)
            {
                var t = lookahead.TokenType;
                if (depth == 0)
                {
                    if (t == (ScriptLex.LexTypes)';' || t == (ScriptLex.LexTypes)')') return false;
                    if (t == ScriptLex.LexTypes.RIn) return true;
                }

                if (t is (ScriptLex.LexTypes)'(' or (ScriptLex.LexTypes)'[' or (ScriptLex.LexTypes)'{') depth++;
                else if (t is (ScriptLex.LexTypes)')' or (ScriptLex.LexTypes)']' or (ScriptLex.LexTypes)'}') depth--;

                lookahead.GetNextToken();
            }

            return false;
        }

        private void CompileForIn()
        {
            if (lexer.TokenType is ScriptLex.LexTypes.RVar or ScriptLex.LexTypes.RConst or ScriptLex.LexTypes.RLet)
            {
                lexer.Match(lexer.TokenType);
            }

            var loopVar = chunk.AddName(lexer.TokenString);
            lexer.Match(ScriptLex.LexTypes.Id);
            lexer.Match(ScriptLex.LexTypes.RIn);

            CompileBase();              // object
            chunk.Emit(OpCode.EnumKeys); // -> keys array
            lexer.Match((ScriptLex.LexTypes)')');

            chunk.Emit(OpCode.DeclareVar, loopVar);

            var keysVar = chunk.AddName($"$forin_keys_{forInCounter}");
            var idxVar = chunk.AddName($"$forin_idx_{forInCounter}");
            forInCounter++;

            // store keys snapshot
            chunk.Emit(OpCode.DeclareVar, keysVar);
            chunk.Emit(OpCode.SetVar, keysVar);
            chunk.Emit(OpCode.Pop);

            // idx = 0
            chunk.Emit(OpCode.DeclareVar, idxVar);
            EmitConstantInt(0);
            chunk.Emit(OpCode.SetVar, idxVar);
            chunk.Emit(OpCode.Pop);

            var condStart = chunk.Count;
            chunk.Emit(OpCode.GetVar, idxVar);
            chunk.Emit(OpCode.GetVar, keysVar);
            chunk.Emit(OpCode.GetProp, chunk.AddName("length"));
            chunk.Emit(OpCode.Binary, (int)(ScriptLex.LexTypes)'<');
            var exitJump = chunk.EmitJump(OpCode.JumpIfFalse);

            // loopVar = keys[idx]
            chunk.Emit(OpCode.GetVar, keysVar);
            chunk.Emit(OpCode.GetVar, idxVar);
            chunk.Emit(OpCode.GetIndex);
            chunk.Emit(OpCode.SetVar, loopVar);
            chunk.Emit(OpCode.Pop);

            loops.Push(new LoopContext());
            CompileStatement(); // body

            var incrStart = chunk.Count;
            chunk.Emit(OpCode.GetVar, idxVar);
            EmitConstantInt(1);
            chunk.Emit(OpCode.Binary, (int)(ScriptLex.LexTypes)'+');
            chunk.Emit(OpCode.SetVar, idxVar);
            chunk.Emit(OpCode.Pop);
            chunk.Emit(OpCode.Jump, condStart);

            chunk.PatchJump(exitJump);

            var ctx = loops.Pop();
            PatchJumps(ctx.BreakJumps, chunk.Count);
            PatchJumps(ctx.ContinueJumps, incrStart);
        }

        private void CompileSwitch()
        {
            lexer.Match(ScriptLex.LexTypes.RSwitch);
            lexer.Match((ScriptLex.LexTypes)'(');
            CompileBase();                  // discriminant → [D]
            lexer.Match((ScriptLex.LexTypes)')');
            lexer.Match((ScriptLex.LexTypes)'{');

            // Use a switch-flavoured context so that `continue` inside the body
            // skips over this frame and targets the enclosing loop instead.
            var ctx = new LoopContext(isSwitch: true);
            loops.Push(ctx);

            // When `default:` appears anywhere (not necessarily last), we defer
            // its body: save a clone of the lexer at that point, skip past the
            // body tokens now, and replay after all `case` tests have been emitted.
            ScriptLex defaultBodyLexer = null;

            while (lexer.TokenType is ScriptLex.LexTypes.RCase or ScriptLex.LexTypes.RDefault)
            {
                if (lexer.TokenType == ScriptLex.LexTypes.RCase)
                {
                    lexer.Match(ScriptLex.LexTypes.RCase);
                    chunk.Emit(OpCode.Dup);          // [D, D]
                    CompileBase();                   // [D, D, V]
                    lexer.Match((ScriptLex.LexTypes)':');
                    chunk.Emit(OpCode.Binary, (int)ScriptLex.LexTypes.Equal);  // [D, cmp]
                    var skipBody = chunk.EmitJump(OpCode.JumpIfFalse);          // [D]

                    // Case matched: discard the discriminant copy, run body.
                    chunk.Emit(OpCode.Pop);                    // []
                    CompileSwitchBody();                       // statements until next case/default/}
                    ctx.BreakJumps.Add(chunk.EmitJump(OpCode.Jump)); // end-of-case → end

                    chunk.PatchJump(skipBody);                 // no-match: fall to next test
                }
                else
                {
                    lexer.Match(ScriptLex.LexTypes.RDefault);
                    lexer.Match((ScriptLex.LexTypes)':');
                    // Clone lexer here so we can compile the body after all cases.
                    defaultBodyLexer = lexer.CloneToEnd(lexer.TokenStart);
                    SkipSwitchBody();                          // advance main lexer past body
                }
            }

            // No case matched: pop discriminant, then run default body (if any).
            chunk.Emit(OpCode.Pop);                            // []
            if (defaultBodyLexer != null)
            {
                var savedLexer = lexer;
                lexer = defaultBodyLexer;
                CompileSwitchBody();
                lexer = savedLexer;
            }

            lexer.Match((ScriptLex.LexTypes)'}');

            // Patch all explicit `break` jumps and implicit end-of-case jumps here.
            PatchJumps(ctx.BreakJumps, chunk.Count);
            loops.Pop();
        }

        // Compile zero or more statements up to the next `case`, `default`, `}`, or EOF.
        private void CompileSwitchBody()
        {
            while (lexer.TokenType is not ScriptLex.LexTypes.RCase
                                   and not ScriptLex.LexTypes.RDefault
                                   and not (ScriptLex.LexTypes)'}'
                                   and not ScriptLex.LexTypes.Eof)
            {
                CompileStatement();
            }
        }

        // Advance the lexer past a switch body without emitting bytecode.
        // Stops when `case`, `default`, or `}` is reached at nesting depth 0.
        private void SkipSwitchBody()
        {
            var depth = 0;
            while (lexer.TokenType != ScriptLex.LexTypes.Eof)
            {
                var t = lexer.TokenType;
                if (depth == 0 && t is ScriptLex.LexTypes.RCase
                                    or ScriptLex.LexTypes.RDefault
                                    or (ScriptLex.LexTypes)'}')
                    break;
                if (t == (ScriptLex.LexTypes)'{') depth++;
                else if (t == (ScriptLex.LexTypes)'}') depth--;
                lexer.GetNextToken();
            }
        }

        private void CompileThrow()
        {
            lexer.Match(ScriptLex.LexTypes.RThrow);

            if (lexer.TokenType != (ScriptLex.LexTypes)';')
            {
                CompileBase();
            }
            else
            {
                chunk.Emit(OpCode.PushUndefined);
            }

            chunk.Emit(OpCode.Throw);
            lexer.Match((ScriptLex.LexTypes)';');
        }

        private void CompileTry()
        {
            lexer.Match(ScriptLex.LexTypes.RTry);

            var hasFinally = lexer.TokenType == ScriptLex.LexTypes.RTry
                ? false // handled below after peeking at both catch/finally
                : PeekHasFinally();

            // Push a finally context so nested `return` can find this finally's PC.
            // The context's ReturnJumps will be backpatched once finallyPC is known.
            var finallyCtx = hasFinally ? new FinallyContext() : null;
            if (hasFinally) finallyStack.Push(finallyCtx);

            // EnterTry — three operand slots emitted as placeholders now, backpatched later.
            var enterAt = chunk.Emit(OpCode.EnterTry, -1, -1, -1);
            var catchPCSlot   = enterAt + 1;  // offset of catchPC operand
            var finallyPCSlot = enterAt + 5;  // offset of finallyPC operand
            var catchVarSlot  = enterAt + 9;  // offset of catchVarIdx operand

            // --- try body (inline in same chunk) ---
            CompileBlock();

            // LeaveTry: normal exit from try body. Dest (finally or after) is backpatched.
            var leaveTryDest = chunk.EmitJump(OpCode.LeaveTry);

            // --- catch block ---
            int leaveCatchDest = -1;
            if (lexer.TokenType == ScriptLex.LexTypes.RCatch)
            {
                chunk.PatchJumpTo(catchPCSlot, chunk.Count); // EnterTry.catchPC
                lexer.Match(ScriptLex.LexTypes.RCatch);
                if (lexer.TokenType == (ScriptLex.LexTypes)'(')
                {
                    lexer.Match((ScriptLex.LexTypes)'(');
                    if (lexer.TokenType == ScriptLex.LexTypes.Id)
                    {
                        var catchVarIdx = chunk.AddName(lexer.TokenString);
                        chunk.PatchJumpTo(catchVarSlot, catchVarIdx); // EnterTry.catchVarIdx
                        lexer.Match(ScriptLex.LexTypes.Id);
                    }
                    lexer.Match((ScriptLex.LexTypes)')');
                }

                CompileBlock();

                leaveCatchDest = chunk.EmitJump(OpCode.LeaveCatch);
            }

            // --- finally block ---
            if (lexer.TokenType == ScriptLex.LexTypes.RFinally)
            {
                var finallyPC = chunk.Count;
                chunk.PatchJumpTo(finallyPCSlot, finallyPC); // EnterTry.finallyPC
                chunk.PatchJump(leaveTryDest);               // LeaveTry → finally
                if (leaveCatchDest >= 0) chunk.PatchJump(leaveCatchDest); // LeaveCatch → finally

                // Backpatch any `return` statements compiled inside the try body.
                if (finallyCtx != null)
                {
                    foreach (var slot in finallyCtx.ReturnJumps)
                        chunk.PatchJumpTo(slot, finallyPC);
                    finallyStack.Pop();
                }

                lexer.Match(ScriptLex.LexTypes.RFinally);
                CompileBlock();
                chunk.Emit(OpCode.LeaveFinally);
            }
            else
            {
                // No finally: LeaveTry and LeaveCatch both jump to after.
                chunk.PatchJump(leaveTryDest);
                if (leaveCatchDest >= 0) chunk.PatchJump(leaveCatchDest);
                if (finallyCtx != null) finallyStack.Pop();
            }
        }

        // Quick lookahead to decide if the try has a finally clause (so we know
        // before compiling the try body whether to push a FinallyContext).
        private bool PeekHasFinally()
        {
            var clone = lexer.CloneToEnd(lexer.TokenStart);
            var depth = 0;
            while (clone.TokenType != ScriptLex.LexTypes.Eof)
            {
                var t = clone.TokenType;
                if (depth == 0 && t == ScriptLex.LexTypes.RFinally) return true;
                if (depth == 0 && t == ScriptLex.LexTypes.RTry) depth++;
                if (t is (ScriptLex.LexTypes)'{') depth++;
                else if (t is (ScriptLex.LexTypes)'}') { if (depth > 0) depth--; }
                clone.GetNextToken();
            }
            return false;
        }

        private void CompileFunctionDeclaration()
        {
            lexer.Match(ScriptLex.LexTypes.RFunction);
            bool isGenerator = lexer.TokenType == (ScriptLex.LexTypes)'*';
            if (isGenerator) lexer.Match((ScriptLex.LexTypes)'*');
            var name = lexer.TokenString;
            lexer.Match(ScriptLex.LexTypes.Id);
            var nameIndex = chunk.AddName(name);

            var idx = CompileFunctionRest(name, isGenerator);

            chunk.Emit(OpCode.DeclareVar, nameIndex);
            chunk.Emit(OpCode.MakeClosure, idx);  // captures the current environment
            chunk.MakesClosure = true;
            chunk.Emit(OpCode.SetVar, nameIndex);
            chunk.Emit(OpCode.Pop);
        }

        private void CompileAsyncFunctionDeclaration()
        {
            lexer.Match(ScriptLex.LexTypes.RAsync);
            lexer.Match(ScriptLex.LexTypes.RFunction);
            var name = lexer.TokenString;
            lexer.Match(ScriptLex.LexTypes.Id);
            var nameIndex = chunk.AddName(name);

            var idx = CompileFunctionRest(name, isGenerator: false, isAsync: true);

            chunk.Emit(OpCode.DeclareVar, nameIndex);
            chunk.Emit(OpCode.MakeClosure, idx);
            chunk.MakesClosure = true;
            chunk.Emit(OpCode.SetVar, nameIndex);
            chunk.Emit(OpCode.Pop);
        }

        private bool IsForOf()
        {
            var lookahead = lexer.CloneToEnd(lexer.TokenStart);
            var depth = 0;
            while (lookahead.TokenType != ScriptLex.LexTypes.Eof)
            {
                var t = lookahead.TokenType;
                if (depth == 0)
                {
                    if (t == (ScriptLex.LexTypes)';' || t == (ScriptLex.LexTypes)')') return false;
                    if (t == ScriptLex.LexTypes.ROf) return true;
                }

                if (t is (ScriptLex.LexTypes)'(' or (ScriptLex.LexTypes)'[' or (ScriptLex.LexTypes)'{') depth++;
                else if (t is (ScriptLex.LexTypes)')' or (ScriptLex.LexTypes)']' or (ScriptLex.LexTypes)'}') depth--;

                lookahead.GetNextToken();
            }

            return false;
        }

        private void CompileForOf()
        {
            if (lexer.TokenType is ScriptLex.LexTypes.RVar or ScriptLex.LexTypes.RConst or ScriptLex.LexTypes.RLet)
                lexer.Match(lexer.TokenType);

            var loopVar = chunk.AddName(lexer.TokenString);
            lexer.Match(ScriptLex.LexTypes.Id);
            lexer.Match(ScriptLex.LexTypes.ROf);

            CompileBase();                   // push the iterable
            chunk.Emit(OpCode.GetIterator);  // normalise to an iterator with .next()
            lexer.Match((ScriptLex.LexTypes)')');

            // store the iterator in a hidden var
            var iterVar = chunk.AddName($"$forof_iter_{forInCounter}");
            forInCounter++;

            chunk.Emit(OpCode.DeclareVar, loopVar);
            chunk.Emit(OpCode.DeclareVar, iterVar);
            chunk.Emit(OpCode.SetVar, iterVar);
            chunk.Emit(OpCode.Pop);

            // while (true) { ForOfStep <exit>; loopVar = value; pop; body }
            var loopTop = chunk.Count;

            chunk.Emit(OpCode.GetVar, iterVar);
            var exitJump = chunk.EmitJump(OpCode.ForOfStep); // pops iter; if done, jumps to exit; else pushes value

            // loopVar = value (left on stack by ForOfStep)
            chunk.Emit(OpCode.SetVar, loopVar);
            chunk.Emit(OpCode.Pop);

            loops.Push(new LoopContext());
            CompileStatement(); // body

            var incrStart = chunk.Count;
            chunk.Emit(OpCode.Jump, loopTop);
            chunk.PatchJump(exitJump);

            var ctx = loops.Pop();
            PatchJumps(ctx.BreakJumps, chunk.Count);
            PatchJumps(ctx.ContinueJumps, incrStart);
        }

        private void PatchJumps(List<int> jumps, int target)
        {
            foreach (var operandOffset in jumps)
            {
                chunk.PatchJumpTo(operandOffset, target);
            }
        }

        // ---- export statement -----------------------------------------------

        private void CompileExport()
        {
            lexer.Match(ScriptLex.LexTypes.RExport);

            if (lexer.TokenType == ScriptLex.LexTypes.RDefault)
            {
                // export default <expr>;
                lexer.Match(ScriptLex.LexTypes.RDefault);

                // Compile the expression into a temp var, then assign to __exports__.default
                var tmpName = "$exportDefault" + _importCounter++;
                var tmpIdx = chunk.AddName(tmpName);
                var exportsIdx = chunk.AddName("__exports__");
                var defaultIdx = chunk.AddName("default");

                chunk.Emit(OpCode.DeclareVar, tmpIdx);
                CompileBase();                          // push expr value
                chunk.Emit(OpCode.SetVar, tmpIdx);      // store; leaves value on stack
                chunk.Emit(OpCode.Pop);

                chunk.Emit(OpCode.GetVar, exportsIdx);  // push __exports__ (obj)
                chunk.Emit(OpCode.GetVar, tmpIdx);       // push value
                chunk.Emit(OpCode.SetProp, defaultIdx);  // __exports__.default = value
                chunk.Emit(OpCode.Pop);

                lexer.Match((ScriptLex.LexTypes)';');
            }
            else if (lexer.TokenType is ScriptLex.LexTypes.RVar
                                     or ScriptLex.LexTypes.RConst
                                     or ScriptLex.LexTypes.RLet)
            {
                // export var/let/const name = expr;
                // Peek ahead to collect variable names before compiling the declaration.
                var names = CollectVarDeclNames();
                CompileVarDeclaration();

                var exportsIdx = chunk.AddName("__exports__");
                foreach (var name in names)
                {
                    var nameIdx = chunk.AddName(name);
                    chunk.Emit(OpCode.GetVar, exportsIdx);  // obj
                    chunk.Emit(OpCode.GetVar, nameIdx);      // value
                    chunk.Emit(OpCode.SetProp, nameIdx);     // __exports__.name = value
                    chunk.Emit(OpCode.Pop);
                }
            }
            else if (lexer.TokenType == ScriptLex.LexTypes.RFunction)
            {
                // export function f() {}
                // Peek the function name before consuming it.
                var lookahead = lexer.CloneToEnd(lexer.TokenStart);
                lookahead.Match(ScriptLex.LexTypes.RFunction);
                // Skip optional generator star
                if (lookahead.TokenType == (ScriptLex.LexTypes)'*') lookahead.Match((ScriptLex.LexTypes)'*');
                var funcName = lookahead.TokenString;

                CompileFunctionDeclaration();

                var exportsIdx = chunk.AddName("__exports__");
                var nameIdx = chunk.AddName(funcName);
                chunk.Emit(OpCode.GetVar, exportsIdx);
                chunk.Emit(OpCode.GetVar, nameIdx);
                chunk.Emit(OpCode.SetProp, nameIdx);
                chunk.Emit(OpCode.Pop);
            }
        }

        // Lookahead: collect the declared variable names from a var/let/const statement
        // that starts at the current lexer position (token is var/let/const).
        private List<string> CollectVarDeclNames()
        {
            var names = new List<string>();
            var clone = lexer.CloneToEnd(lexer.TokenStart);
            clone.Match(clone.TokenType); // skip var/let/const

            while (clone.TokenType != (ScriptLex.LexTypes)';' && clone.TokenType != ScriptLex.LexTypes.Eof)
            {
                if (clone.TokenType == ScriptLex.LexTypes.Id)
                {
                    names.Add(clone.TokenString);
                    clone.Match(ScriptLex.LexTypes.Id);

                    // Skip past the initialiser (if any) to the next comma or semicolon.
                    int depth = 0;
                    while (clone.TokenType != ScriptLex.LexTypes.Eof)
                    {
                        var t = clone.TokenType;
                        if (t is (ScriptLex.LexTypes)'(' or (ScriptLex.LexTypes)'[' or (ScriptLex.LexTypes)'{') depth++;
                        else if (t is (ScriptLex.LexTypes)')' or (ScriptLex.LexTypes)']' or (ScriptLex.LexTypes)'}') depth--;
                        else if (depth == 0 && (t == (ScriptLex.LexTypes)',' || t == (ScriptLex.LexTypes)';')) break;
                        clone.GetNextToken();
                    }

                    if (clone.TokenType == (ScriptLex.LexTypes)',')
                        clone.Match((ScriptLex.LexTypes)',');
                }
                else
                {
                    // Destructuring or unexpected — skip.
                    clone.GetNextToken();
                }
            }
            return names;
        }

        // ---- import statement -----------------------------------------------

        private void CompileImport()
        {
            lexer.Match(ScriptLex.LexTypes.RImport);

            // Dynamic import expression used as a statement: import(specifier)
            if (lexer.TokenType == (ScriptLex.LexTypes)'(')
            {
                lexer.Match((ScriptLex.LexTypes)'(');
                CompileBase();  // specifier
                lexer.Match((ScriptLex.LexTypes)')');
                chunk.Emit(OpCode.DynamicImport);
                // statement — discard Promise result, continue member chain if any
                // (callers may chain .then() etc.)
                CompileMemberChain(false);
                chunk.Emit(OpCode.Pop);
                return;
            }

            // import.meta used as a statement (rare but valid): import.meta.url
            if (lexer.TokenType == (ScriptLex.LexTypes)'.')
            {
                lexer.Match((ScriptLex.LexTypes)'.');
                if (lexer.TokenString != "meta")
                    throw new JITException("Expected 'meta' after 'import.'");
                lexer.Match(ScriptLex.LexTypes.Id);
                chunk.Emit(OpCode.PushImportMeta);
                CompileMemberChain(false);
                chunk.Emit(OpCode.Pop);
                return;
            }

            if (lexer.TokenType == (ScriptLex.LexTypes)'*')
            {
                // import * as ns from "path"
                lexer.Match((ScriptLex.LexTypes)'*');
                // "as" is treated as a contextual identifier
                if (lexer.TokenType == ScriptLex.LexTypes.Id && lexer.TokenString == "as")
                    lexer.Match(ScriptLex.LexTypes.Id);
                var nsName = lexer.TokenString;
                lexer.Match(ScriptLex.LexTypes.Id);
                lexer.Match(ScriptLex.LexTypes.RFrom);
                var path = lexer.TokenString;
                lexer.Match(ScriptLex.LexTypes.Str);
                lexer.Match((ScriptLex.LexTypes)';');

                var nsIdx = chunk.AddName(nsName);
                chunk.Emit(OpCode.DeclareVar, nsIdx);
                EmitRequireCall(path);
                chunk.Emit(OpCode.SetVar, nsIdx);
                chunk.Emit(OpCode.Pop);
            }
            else if (lexer.TokenType == (ScriptLex.LexTypes)'{')
            {
                // import { x, y as z } from "path"
                var bindings = new List<(string key, string local)>();
                lexer.Match((ScriptLex.LexTypes)'{');
                while (lexer.TokenType != (ScriptLex.LexTypes)'}')
                {
                    var key = lexer.TokenString;
                    lexer.Match(ScriptLex.LexTypes.Id);
                    var local = key;
                    if (lexer.TokenType == ScriptLex.LexTypes.Id && lexer.TokenString == "as")
                    {
                        lexer.Match(ScriptLex.LexTypes.Id);
                        local = lexer.TokenString;
                        lexer.Match(ScriptLex.LexTypes.Id);
                    }
                    bindings.Add((key, local));
                    if (lexer.TokenType != (ScriptLex.LexTypes)'}')
                        lexer.Match((ScriptLex.LexTypes)',');
                }
                lexer.Match((ScriptLex.LexTypes)'}');
                lexer.Match(ScriptLex.LexTypes.RFrom);
                var path = lexer.TokenString;
                lexer.Match(ScriptLex.LexTypes.Str);
                lexer.Match((ScriptLex.LexTypes)';');

                // var $mod = require("path");
                var modTmpName = "$importMod" + _importCounter++;
                var modTmpIdx = chunk.AddName(modTmpName);
                chunk.Emit(OpCode.DeclareVar, modTmpIdx);
                EmitRequireCall(path);
                chunk.Emit(OpCode.SetVar, modTmpIdx);
                chunk.Emit(OpCode.Pop);

                foreach (var (key, local) in bindings)
                {
                    var localIdx = chunk.AddName(local);
                    var keyIdx = chunk.AddName(key);
                    chunk.Emit(OpCode.DeclareVar, localIdx);
                    chunk.Emit(OpCode.GetVar, modTmpIdx);
                    chunk.Emit(OpCode.GetProp, keyIdx);
                    chunk.Emit(OpCode.SetVar, localIdx);
                    chunk.Emit(OpCode.Pop);
                }
            }
            else if (lexer.TokenType == ScriptLex.LexTypes.Id)
            {
                // import defaultExport from "path"
                var localName = lexer.TokenString;
                lexer.Match(ScriptLex.LexTypes.Id);
                lexer.Match(ScriptLex.LexTypes.RFrom);
                var path = lexer.TokenString;
                lexer.Match(ScriptLex.LexTypes.Str);
                lexer.Match((ScriptLex.LexTypes)';');

                var localIdx = chunk.AddName(localName);
                var defaultIdx = chunk.AddName("default");
                chunk.Emit(OpCode.DeclareVar, localIdx);
                EmitRequireCall(path);
                chunk.Emit(OpCode.GetProp, defaultIdx);
                chunk.Emit(OpCode.SetVar, localIdx);
                chunk.Emit(OpCode.Pop);
            }
        }

        // Emit bytecode equivalent to: require("<path>")  (result left on stack)
        private void EmitRequireCall(string path)
        {
            var requireIdx = chunk.AddName("require");
            var pathConstIdx = chunk.AddConstant(ConstantValue.String(path));
            chunk.Emit(OpCode.GetVar, requireIdx);
            chunk.Emit(OpCode.Constant, pathConstIdx);
            chunk.Emit(OpCode.Call, 1);
        }
    }
}
