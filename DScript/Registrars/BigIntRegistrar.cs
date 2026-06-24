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

using System.Numerics;

namespace DScript.Registrars
{
    internal static class BigIntRegistrar
    {
        internal static void Register(ScriptEngine engine)
        {
            // BigInt(value) — factory function, not a constructor.
            // new BigInt() throws TypeError.
            var bigIntCtor = ScriptVar.CreateNativeFunction();
            bigIntCtor.AddChild("value", ScriptVar.CreateUndefined());
            bigIntCtor.SetCallback((scope, _) =>
            {
                var thisVar = scope.FindChild("this")?.Var;
                if (thisVar != null && thisVar.IsObject)
                    throw new ScriptException("TypeError: BigInt is not a constructor");

                var val = scope.FindChild("value")?.Var ?? ScriptVar.CreateUndefined();
                BigInteger result;

                if (val.IsBigInt)
                    result = val.BigIntData;
                else if (val.IsInt)
                    result = new BigInteger(val.Int);
                else if (val.IsDouble)
                {
                    var d = val.Float;
                    if (d != System.Math.Truncate(d))
                        throw new ScriptException("RangeError: The number is not safe to convert to a BigInt because it is not an integer");
                    result = new BigInteger((long)d);
                }
                else if (val.IsString)
                {
                    var s = val.String.Trim();
                    if (!BigInteger.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out result))
                        throw new ScriptException($"SyntaxError: Cannot convert {s} to a BigInt");
                }
                else if (val.IsNull || val.IsUndefined)
                    throw new ScriptException("TypeError: Cannot convert null or undefined to a BigInt");
                else
                    result = val.Bool ? BigInteger.One : BigInteger.Zero;

                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(ScriptVar.CreateBigInt(result));
            }, null);

            engine.Root.AddChild("BigInt", bigIntCtor);
        }
    }
}
