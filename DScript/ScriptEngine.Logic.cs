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
    public partial class ScriptEngine
    {
        private ScriptVarLink Logic(ref bool execute)
        {
            var a = Condition(ref execute);

            ScriptVarLink b;

            while (currentLexer.TokenType == (ScriptLex.LexTypes)'&' ||
                currentLexer.TokenType == (ScriptLex.LexTypes)'|' ||
                currentLexer.TokenType == (ScriptLex.LexTypes)'^' ||
                currentLexer.TokenType == ScriptLex.LexTypes.AndAnd ||
                currentLexer.TokenType == ScriptLex.LexTypes.OrOr)
            {
                var noExecute = false;
                var op = currentLexer.TokenType;
                currentLexer.Match(op);

                var shortcut = false;
                var isBool = false;

                if (op == ScriptLex.LexTypes.AndAnd)
                {
                    op = (ScriptLex.LexTypes)'&';
                    shortcut = !a.Var.GetBool();
                    isBool = true;
                }
                else if (op == ScriptLex.LexTypes.OrOr)
                {
                    op = (ScriptLex.LexTypes)'|';
                    shortcut = a.Var.GetBool();
                    isBool = true;
                }

                if (shortcut)
                {
                    b = Condition(ref noExecute);
                } 
                else
                {
                    b = Condition(ref execute);
                }

                if (execute && !shortcut)
                {
                    if (isBool)
                    {
                        var newA = new ScriptVar(a.Var.GetBool());
                        var newB = new ScriptVar(b.Var.GetBool());

                        CreateLink(ref a, newA);
                        CreateLink(ref b, newB);
                    }

                    var res = a.Var.MathsOp(b.Var, op);

                    CreateLink(ref a, res);
                }
            }

            return a;
        }
    }
}
