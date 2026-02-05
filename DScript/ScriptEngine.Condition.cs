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

namespace DScript
{
    public sealed partial class ScriptEngine
    {
        private ScriptVarLink Condition(ref bool execute)
        {
            var a = Shift(ref execute);

            while (currentLexer.TokenType == ScriptLex.LexTypes.Equal ||
                currentLexer.TokenType == ScriptLex.LexTypes.NEqual ||
                currentLexer.TokenType == ScriptLex.LexTypes.TypeEqual ||
                currentLexer.TokenType == ScriptLex.LexTypes.NTypeEqual ||
                currentLexer.TokenType == ScriptLex.LexTypes.LEqual ||
                currentLexer.TokenType == ScriptLex.LexTypes.GEqual ||
                currentLexer.TokenType == (ScriptLex.LexTypes)'>' ||
                currentLexer.TokenType == (ScriptLex.LexTypes)'<'
                )
            {
                var op = currentLexer.TokenType;
                currentLexer.Match(op);
                var b = Shift(ref execute);

                if (execute)
                {
                    var res = a.Var.MathsOp(b.Var, op);
                    CreateLink(ref a, res);
                }
            }

            return a;
        }
    }
}
