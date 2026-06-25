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

using System.Globalization;
using System.Numerics;
using System.Text;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("BigInt")]
    public static class BigIntFunctionProvider
    {
        // Resolve the BigInt value of `this`. Bare BigInt primitives carry their
        // value directly; anything else coerces to zero.
        private static BigInteger ThisValue(ScriptVar var)
        {
            var self = var.GetParameter("this");
            return self.IsBigInt ? self.BigIntData : BigInteger.Zero;
        }

        [ScriptMethod("toString", "radix")]
        public static void BigIntToStringImpl(ScriptVar var, object userData)
        {
            var value = ThisValue(var);
            var radixVar = var.GetParameter("radix");
            var radix = radixVar.IsUndefined ? 10 : radixVar.Int;
            if (radix == 10)
            {
                var.ReturnVar.String = value.ToString(CultureInfo.InvariantCulture);
                return;
            }
            if (radix < 2 || radix > 36) { var.ReturnVar.String = "NaN"; return; }
            var.ReturnVar.String = ToRadixString(value, radix);
        }

        [ScriptMethod("valueOf")]
        public static void BigIntValueOfImpl(ScriptVar var, object userData)
        {
            var.ReturnVar = ScriptVar.CreateBigInt(ThisValue(var));
        }

        [ScriptMethod("toLocaleString")]
        public static void BigIntToLocaleStringImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.String = ThisValue(var).ToString(CultureInfo.InvariantCulture);
        }

        // Convert a BigInteger to a string in any base 2..36 (JS BigInt supports the
        // full range; BigInteger.ToString only does decimal and hex).
        private static string ToRadixString(BigInteger value, int radix)
        {
            if (value.IsZero) return "0";
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            var negative = value.Sign < 0;
            var n = negative ? BigInteger.Negate(value) : value;
            var sb = new StringBuilder();
            var bigRadix = new BigInteger(radix);
            while (n > BigInteger.Zero)
            {
                n = BigInteger.DivRem(n, bigRadix, out var rem);
                sb.Insert(0, digits[(int)rem]);
            }
            if (negative) sb.Insert(0, '-');
            return sb.ToString();
        }
    }
}
