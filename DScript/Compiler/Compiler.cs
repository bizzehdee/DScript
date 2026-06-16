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

using DScript.Vm;

namespace DScript.Compiler
{
    /// <summary>
    /// Compiles DScript source into bytecode <see cref="Chunk"/>s. It walks the
    /// token stream with the same operator-precedence recursive descent as the
    /// tree-walking engine, but emits opcodes instead of executing. Forward jumps
    /// are backpatched.
    /// </summary>
    public sealed partial class DScriptCompiler
    {
        private ScriptLex lexer;
        private Chunk chunk;

        /// <summary>
        /// Compile a single expression to a chunk that leaves its value via
        /// <see cref="OpCode.Return"/>. Used for expression evaluation and tests.
        /// </summary>
        public Chunk CompileExpression(string source)
        {
            lexer = new ScriptLex(source);
            chunk = new Chunk { Name = "<expr>" };

            CompileBase();
            chunk.Emit(OpCode.Return);

            return chunk;
        }

        // ----- precedence chain (mirrors ScriptEngine.* tiers) --------------

        // Assignment tier. Statement-level assignment is added in a later phase;
        // for now this simply forwards to the ternary tier.
        private void CompileBase()
        {
            CompileTernary();
        }

        private void CompileTernary()
        {
            CompileLogic();

            if (lexer.TokenType != (ScriptLex.LexTypes)'?') return;

            lexer.Match((ScriptLex.LexTypes)'?');

            // condition is on the stack; jump to the else arm when falsy
            var toElse = chunk.EmitJump(OpCode.JumpIfFalse);
            CompileBase();                       // then arm leaves its value
            var toEnd = chunk.EmitJump(OpCode.Jump);

            chunk.PatchJump(toElse);
            lexer.Match((ScriptLex.LexTypes)':');
            CompileBase();                       // else arm leaves its value

            chunk.PatchJump(toEnd);
        }

        private void CompileLogic()
        {
            CompileCondition();

            while (lexer.TokenType is
                   (ScriptLex.LexTypes)'&' or
                   (ScriptLex.LexTypes)'|' or
                   (ScriptLex.LexTypes)'^' or
                   ScriptLex.LexTypes.AndAnd or
                   ScriptLex.LexTypes.OrOr)
            {
                var op = lexer.TokenType;
                lexer.Match(op);

                switch (op)
                {
                    case ScriptLex.LexTypes.AndAnd:
                    {
                        // short-circuit: if left is falsy, keep it as the result
                        var end = chunk.EmitJump(OpCode.JumpIfFalseOrPop);
                        CompileCondition();
                        chunk.PatchJump(end);
                        break;
                    }
                    case ScriptLex.LexTypes.OrOr:
                    {
                        var end = chunk.EmitJump(OpCode.JumpIfTrueOrPop);
                        CompileCondition();
                        chunk.PatchJump(end);
                        break;
                    }
                    default:
                        CompileCondition();
                        chunk.Emit(OpCode.Binary, (int)op);
                        break;
                }
            }
        }

        private void CompileCondition()
        {
            CompileShift();

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
                CompileShift();

                switch (op)
                {
                    case ScriptLex.LexTypes.RInstanceOf:
                        chunk.Emit(OpCode.InstanceOf);
                        break;
                    case ScriptLex.LexTypes.RIn:
                        chunk.Emit(OpCode.In);
                        break;
                    default:
                        chunk.Emit(OpCode.Binary, (int)op);
                        break;
                }
            }
        }

        private void CompileShift()
        {
            CompileAdditive();

            while (lexer.TokenType is
                   ScriptLex.LexTypes.LShift or
                   ScriptLex.LexTypes.RShift or
                   ScriptLex.LexTypes.RShiftUnsigned)
            {
                var op = lexer.TokenType;
                lexer.Match(op);
                CompileAdditive();
                chunk.Emit(OpCode.Shift, (int)op);
            }
        }

        private void CompileAdditive()
        {
            CompileTerm();

            while (lexer.TokenType is (ScriptLex.LexTypes)'+' or (ScriptLex.LexTypes)'-')
            {
                var op = lexer.TokenType;
                lexer.Match(op);
                CompileTerm();
                chunk.Emit(OpCode.Binary, (int)op);
            }
        }

        private void CompileTerm()
        {
            CompileUnary();

            while (lexer.TokenType is
                   (ScriptLex.LexTypes)'*' or
                   (ScriptLex.LexTypes)'/' or
                   (ScriptLex.LexTypes)'%')
            {
                var op = lexer.TokenType;
                lexer.Match(op);
                CompileUnary();
                chunk.Emit(OpCode.Binary, (int)op);
            }
        }

        private void CompileUnary()
        {
            switch (lexer.TokenType)
            {
                case (ScriptLex.LexTypes)'!':
                    lexer.Match((ScriptLex.LexTypes)'!');
                    CompileUnary();
                    chunk.Emit(OpCode.Not);
                    break;
                case (ScriptLex.LexTypes)'~':
                    lexer.Match((ScriptLex.LexTypes)'~');
                    CompileUnary();
                    chunk.Emit(OpCode.BitNot);
                    break;
                case (ScriptLex.LexTypes)'-':
                    lexer.Match((ScriptLex.LexTypes)'-');
                    CompileUnary();
                    chunk.Emit(OpCode.Negate);
                    break;
                case (ScriptLex.LexTypes)'+':
                    lexer.Match((ScriptLex.LexTypes)'+');
                    CompileUnary();
                    chunk.Emit(OpCode.ToNumber);
                    break;
                case ScriptLex.LexTypes.RTypeOf:
                    lexer.Match(ScriptLex.LexTypes.RTypeOf);
                    CompileUnary();
                    chunk.Emit(OpCode.Typeof);
                    break;
                default:
                    CompileFactor();
                    break;
            }
        }
    }
}
