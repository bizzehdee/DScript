﻿/*
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
        private ScriptVarLink Expression(ref bool execute)
        {
            var negate = false;
            if (currentLexer.TokenType == (ScriptLex.LexTypes)'-')
            {
                currentLexer.Match((ScriptLex.LexTypes)'-');
                negate = true;
            }

            var a = Term(ref execute);

            if (negate)
            {
                var zero = new ScriptVar(0);
                var res = zero.MathsOp(a.Var, (ScriptLex.LexTypes)'-');

                if (a.Owned)
                {
                    a = new ScriptVarLink(res, null);
                }
                else
                {
                    a.ReplaceWith(res);
                }
            }

            while (currentLexer.TokenType == (ScriptLex.LexTypes)'+' ||
                   currentLexer.TokenType == (ScriptLex.LexTypes)'-' ||
                   currentLexer.TokenType == ScriptLex.LexTypes.PlusPlus ||
                   currentLexer.TokenType == ScriptLex.LexTypes.MinusMinus)
            {
                var op = currentLexer.TokenType;
                currentLexer.Match(op);

                if (op == ScriptLex.LexTypes.PlusPlus || op == ScriptLex.LexTypes.MinusMinus)
                {
                    if (execute)
                    {
                        var one = new ScriptVar(1);
                        var res = a.Var.MathsOp(one, (ScriptLex.LexTypes)(op == ScriptLex.LexTypes.PlusPlus ? '+' : '-'));
                        var oldVal = new ScriptVarLink(a.Var, null);

                        a.ReplaceWith(res);
                        a = oldVal;
                    }
                }
                else
                {
                    ScriptVarLink b = Term(ref execute);
                    if (execute)
                    {
                        var res = a.Var.MathsOp(b.Var, op);

                        if (a.Owned)
                        {
                            a = new ScriptVarLink(res, null);
                        }
                        else
                        {
                            a.ReplaceWith(res);
                        }

                    }
                }
            }

            return a;
        }
    }
}
