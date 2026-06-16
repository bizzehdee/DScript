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

            while (currentLexer.TokenType is
                   ScriptLex.LexTypes.Equal or
                   ScriptLex.LexTypes.NEqual or
                   ScriptLex.LexTypes.TypeEqual or
                   ScriptLex.LexTypes.NTypeEqual or
                   ScriptLex.LexTypes.LEqual or
                   ScriptLex.LexTypes.GEqual or
                   ScriptLex.LexTypes.RInstanceOf or
                   (ScriptLex.LexTypes)'>' or
                   (ScriptLex.LexTypes)'<'
                )
            {
                var op = currentLexer.TokenType;
                currentLexer.Match(op);
                var b = Shift(ref execute);

                if (!execute) continue;

                if (op == ScriptLex.LexTypes.RInstanceOf)
                {
                    // `a instanceof B` is true when B appears anywhere on a's
                    // prototype chain (instances link to their constructor via
                    // the `prototype` child created by `new`).
                    var isInstance = false;
                    var proto = a.Var.FindChild(ScriptVar.PrototypeClassName);
                    while (proto != null)
                    {
                        if (ReferenceEquals(proto.Var, b.Var))
                        {
                            isInstance = true;
                            break;
                        }
                        proto = proto.Var.FindChild(ScriptVar.PrototypeClassName);
                    }

                    CreateLink(ref a, new ScriptVar(isInstance));
                    continue;
                }

                var res = a.Var.MathsOp(b.Var, op);
                CreateLink(ref a, res);
            }

            return a;
        }
    }
}
