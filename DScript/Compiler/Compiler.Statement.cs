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
            public List<int> BreakJumps { get; } = [];
            public List<int> ContinueJumps { get; } = [];
        }

        private readonly Stack<LoopContext> loops = new();
        private int forInCounter;

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
                    if (loops.Count > 0) loops.Peek().ContinueJumps.Add(chunk.EmitJump(OpCode.Jump));
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
            {
                CompileBase();
            }
            else
            {
                chunk.Emit(OpCode.PushUndefined);
            }

            chunk.Emit(OpCode.Return);
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
            CompileBase();              // discriminant stays on the stack
            lexer.Match((ScriptLex.LexTypes)')');
            lexer.Match((ScriptLex.LexTypes)'{');

            var endJumps = new List<int>();

            while (lexer.TokenType is ScriptLex.LexTypes.RCase or ScriptLex.LexTypes.RDefault)
            {
                if (lexer.TokenType == ScriptLex.LexTypes.RCase)
                {
                    lexer.Match(ScriptLex.LexTypes.RCase);
                    chunk.Emit(OpCode.Dup);          // duplicate discriminant
                    CompileBase();                   // case value
                    lexer.Match((ScriptLex.LexTypes)':');
                    chunk.Emit(OpCode.Binary, (int)ScriptLex.LexTypes.Equal);
                    var skip = chunk.EmitJump(OpCode.JumpIfFalse);
                    CompileStatement();              // case body (single statement)
                    lexer.Match(ScriptLex.LexTypes.RBreak);
                    lexer.Match((ScriptLex.LexTypes)';');
                    endJumps.Add(chunk.EmitJump(OpCode.Jump));
                    chunk.PatchJump(skip);
                }
                else
                {
                    lexer.Match(ScriptLex.LexTypes.RDefault);
                    lexer.Match((ScriptLex.LexTypes)':');
                    CompileStatement();
                    lexer.Match(ScriptLex.LexTypes.RBreak);
                    lexer.Match((ScriptLex.LexTypes)';');
                    endJumps.Add(chunk.EmitJump(OpCode.Jump));
                }
            }

            lexer.Match((ScriptLex.LexTypes)'}');

            PatchJumps(endJumps, chunk.Count);
            chunk.Emit(OpCode.Pop); // discard the discriminant
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
            var tryIndex = CompileSubBlock("<try>");

            var catchIndex = -1;
            var catchParamIndex = -1;
            var finallyIndex = -1;

            if (lexer.TokenType == ScriptLex.LexTypes.RCatch)
            {
                lexer.Match(ScriptLex.LexTypes.RCatch);
                lexer.Match((ScriptLex.LexTypes)'(');
                catchParamIndex = chunk.AddName(lexer.TokenString);
                lexer.Match(ScriptLex.LexTypes.Id);
                lexer.Match((ScriptLex.LexTypes)')');
                catchIndex = CompileSubBlock("<catch>");
            }

            if (lexer.TokenType == ScriptLex.LexTypes.RFinally)
            {
                lexer.Match(ScriptLex.LexTypes.RFinally);
                finallyIndex = CompileSubBlock("<finally>");
            }

            chunk.Emit(OpCode.Try, tryIndex, catchIndex, finallyIndex, catchParamIndex);
        }

        // Compile a brace-delimited block into a nested chunk; returns its index
        // in the enclosing chunk's function table.
        private int CompileSubBlock(string name)
        {
            var sub = new Chunk { Name = name };
            var saved = chunk;
            chunk = sub;
            CompileBlock();
            chunk.Emit(OpCode.PushUndefined);
            chunk.Emit(OpCode.Return);
            chunk = saved;
            // a try/catch/finally body runs in the enclosing function's
            // environment, so a closure created there captures that frame too
            chunk.MakesClosure |= sub.MakesClosure;
            return saved.AddFunction(sub);
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
