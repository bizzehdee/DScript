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
        private ScriptVarLink Base(ref bool execute)
        {
            var leftHandSide = Ternary(ref execute);

            if (currentLexer.TokenType == (ScriptLex.LexTypes)'=' ||
                currentLexer.TokenType == ScriptLex.LexTypes.PlusEqual ||
                currentLexer.TokenType == ScriptLex.LexTypes.MinusEqual ||
                currentLexer.TokenType == ScriptLex.LexTypes.SlashEqual ||
                currentLexer.TokenType == ScriptLex.LexTypes.PercentEqual)
            {
                if (execute && leftHandSide.Owned == false)
                {
                    if (leftHandSide.Name.Length > 0)
                    {
                        var leftHandSideReal = Root.AddChildNoDup(leftHandSide.Name, leftHandSide.Var);
                        leftHandSide = leftHandSideReal;
                    }
                    else
                    {
                        //?wtf?
                        System.Diagnostics.Trace.TraceWarning("Trying to assign to an unnamed type...");
                    }
                }

                var op = currentLexer.TokenType;
                currentLexer.Match(op);

                var rightHandSide = Base(ref execute);

                if (execute)
                {
                    switch (op)
                    {
                        case (ScriptLex.LexTypes)'=':
                            {
                                leftHandSide.ReplaceWith(rightHandSide);
                            }
                            break;
                        case ScriptLex.LexTypes.PlusEqual:
                            {
                                var res = leftHandSide.Var.MathsOp(rightHandSide.Var, (ScriptLex.LexTypes)'+');
                                leftHandSide.ReplaceWith(res);
                            }
                            break;
                        case ScriptLex.LexTypes.MinusEqual:
                            {
                                var res = leftHandSide.Var.MathsOp(rightHandSide.Var, (ScriptLex.LexTypes)'-');
                                leftHandSide.ReplaceWith(res);
                            }
                            break;
                        case ScriptLex.LexTypes.SlashEqual:
                            {
                                var res = leftHandSide.Var.MathsOp(rightHandSide.Var, (ScriptLex.LexTypes)'/');
                                leftHandSide.ReplaceWith(res);
                            }
                            break;
                        case ScriptLex.LexTypes.PercentEqual:
                            {
                                var res = leftHandSide.Var.MathsOp(rightHandSide.Var, (ScriptLex.LexTypes)'%');
                                leftHandSide.ReplaceWith(res);
                            }
                            break;
                        default:
                            throw new ScriptException("Base broke");
                    }
                }

            }

            return leftHandSide;
        }
    }
}
