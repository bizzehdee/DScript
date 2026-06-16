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
    public sealed partial class DScriptCompiler
    {
        // Phase 2 covers literals, parentheses and object/array literals.
        // Identifier/member/call/new/function forms are added in later phases.
        private void CompileFactor()
        {
            switch (lexer.TokenType)
            {
                case (ScriptLex.LexTypes)'(':
                    lexer.Match((ScriptLex.LexTypes)'(');
                    CompileBase();
                    lexer.Match((ScriptLex.LexTypes)')');
                    return;

                case ScriptLex.LexTypes.RTrue:
                    lexer.Match(ScriptLex.LexTypes.RTrue);
                    chunk.Emit(OpCode.PushTrue);
                    return;

                case ScriptLex.LexTypes.RFalse:
                    lexer.Match(ScriptLex.LexTypes.RFalse);
                    chunk.Emit(OpCode.PushFalse);
                    return;

                case ScriptLex.LexTypes.RNull:
                    lexer.Match(ScriptLex.LexTypes.RNull);
                    chunk.Emit(OpCode.PushNull);
                    return;

                case ScriptLex.LexTypes.RUndefined:
                    lexer.Match(ScriptLex.LexTypes.RUndefined);
                    chunk.Emit(OpCode.PushUndefined);
                    return;

                case ScriptLex.LexTypes.Int:
                {
                    // reuse the engine's int parsing (handles 0x / 0 octal)
                    var value = new ScriptVar(lexer.TokenString, ScriptVar.Flags.Integer).Int;
                    lexer.Match(ScriptLex.LexTypes.Int);
                    chunk.Emit(OpCode.Constant, chunk.AddConstant(ConstantValue.Int(value)));
                    return;
                }

                case ScriptLex.LexTypes.Float:
                {
                    var value = new ScriptVar(lexer.TokenString, ScriptVar.Flags.Double).Float;
                    lexer.Match(ScriptLex.LexTypes.Float);
                    chunk.Emit(OpCode.Constant, chunk.AddConstant(ConstantValue.Double(value)));
                    return;
                }

                case ScriptLex.LexTypes.Str:
                {
                    var value = lexer.TokenString;
                    lexer.Match(ScriptLex.LexTypes.Str);
                    chunk.Emit(OpCode.Constant, chunk.AddConstant(ConstantValue.String(value)));
                    return;
                }

                case ScriptLex.LexTypes.RegExp:
                {
                    var value = lexer.TokenString;
                    lexer.Match(ScriptLex.LexTypes.RegExp);
                    chunk.Emit(OpCode.Constant, chunk.AddConstant(ConstantValue.Regex(value)));
                    return;
                }

                case (ScriptLex.LexTypes)'{':
                    CompileObjectLiteral();
                    return;

                case (ScriptLex.LexTypes)'[':
                    CompileArrayLiteral();
                    return;
            }

            // unexpected token for the current (phase-limited) grammar
            lexer.Match(ScriptLex.LexTypes.Eof);
        }

        private void CompileObjectLiteral()
        {
            lexer.Match((ScriptLex.LexTypes)'{');
            chunk.Emit(OpCode.NewObject);

            while (lexer.TokenType != (ScriptLex.LexTypes)'}')
            {
                var name = lexer.TokenString;

                if (lexer.TokenType == ScriptLex.LexTypes.Str)
                {
                    lexer.Match(ScriptLex.LexTypes.Str);
                }
                else
                {
                    lexer.Match(ScriptLex.LexTypes.Id);
                }

                lexer.Match((ScriptLex.LexTypes)':');

                CompileBase();                                    // value
                chunk.Emit(OpCode.InitProp, chunk.AddName(name)); // obj.name = value, obj kept

                if (lexer.TokenType != (ScriptLex.LexTypes)'}')
                {
                    lexer.Match((ScriptLex.LexTypes)',');
                }
            }

            lexer.Match((ScriptLex.LexTypes)'}');
        }

        private void CompileArrayLiteral()
        {
            lexer.Match((ScriptLex.LexTypes)'[');
            chunk.Emit(OpCode.NewArray);

            var index = 0;
            while (lexer.TokenType != (ScriptLex.LexTypes)']')
            {
                CompileBase();                          // element value
                chunk.Emit(OpCode.InitElem, index);     // arr[index] = value, arr kept

                if (lexer.TokenType != (ScriptLex.LexTypes)']')
                {
                    lexer.Match((ScriptLex.LexTypes)',');
                }

                index++;
            }

            lexer.Match((ScriptLex.LexTypes)']');
        }
    }
}
