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
                    CompileVarDeclaration();
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
            while (lexer.TokenType != (ScriptLex.LexTypes)'}' && lexer.TokenType != ScriptLex.LexTypes.Eof)
            {
                CompileStatement();
            }
            lexer.Match((ScriptLex.LexTypes)'}');
        }

        private void CompileVarDeclaration()
        {
            var readOnly = lexer.TokenType == ScriptLex.LexTypes.RConst;
            lexer.Match(lexer.TokenType);

            while (lexer.TokenType != (ScriptLex.LexTypes)';')
            {
                var name = lexer.TokenString;
                lexer.Match(ScriptLex.LexTypes.Id);
                var nameIndex = chunk.AddName(name);

                chunk.Emit(readOnly ? OpCode.DeclareConst : OpCode.DeclareVar, nameIndex);

                if (lexer.TokenType == (ScriptLex.LexTypes)'=')
                {
                    lexer.Match((ScriptLex.LexTypes)'=');
                    CompileBase();
                    chunk.Emit(OpCode.SetVar, nameIndex);
                    chunk.Emit(OpCode.Pop);
                }

                if (lexer.TokenType != (ScriptLex.LexTypes)';')
                {
                    lexer.Match((ScriptLex.LexTypes)',');
                }
            }

            lexer.Match((ScriptLex.LexTypes)';');
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
            if (lexer.TokenType is ScriptLex.LexTypes.RVar or ScriptLex.LexTypes.RConst)
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
                lexer.Match((ScriptLex.LexTypes)'(');
                var catchVarIdx = chunk.AddName(lexer.TokenString);
                chunk.PatchJumpTo(catchVarSlot, catchVarIdx); // EnterTry.catchVarIdx
                lexer.Match(ScriptLex.LexTypes.Id);
                lexer.Match((ScriptLex.LexTypes)')');

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
            var name = lexer.TokenString;
            lexer.Match(ScriptLex.LexTypes.Id);
            var nameIndex = chunk.AddName(name);

            var idx = CompileFunctionRest(name);

            chunk.Emit(OpCode.DeclareVar, nameIndex);
            chunk.Emit(OpCode.MakeClosure, idx);  // captures the current environment
            chunk.MakesClosure = true;
            chunk.Emit(OpCode.SetVar, nameIndex);
            chunk.Emit(OpCode.Pop);
        }

        private void PatchJumps(List<int> jumps, int target)
        {
            foreach (var operandOffset in jumps)
            {
                chunk.PatchJumpTo(operandOffset, target);
            }
        }
    }
}
