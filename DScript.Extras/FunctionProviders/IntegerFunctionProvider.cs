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

using System;
using System.Globalization;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("Integer")]
    public static class IntegerFunctionProvider
    {
        [ScriptMethod("parseInt", "str", "radix")]
        [ScriptMethod("parseInt", "str", "radix", AppearAtRoot = true)]
        public static void IntParseIntImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("str").String.Trim();
            var radixVar = var.GetParameter("radix");
            var radix = radixVar.IsUndefined ? 0 : radixVar.Int;

            var index = 0;
            var sign = 1;
            if (index < str.Length && (str[index] == '+' || str[index] == '-'))
            {
                if (str[index] == '-') sign = -1;
                index++;
            }

            //honour an explicit 0x prefix when the radix is unspecified or 16
            if ((radix == 0 || radix == 16) &&
                index + 1 < str.Length && str[index] == '0' && (str[index + 1] == 'x' || str[index + 1] == 'X'))
            {
                index += 2;
                radix = 16;
            }

            if (radix == 0) radix = 10;

            if (radix < 2 || radix > 36)
            {
                var.ReturnVar.Float = double.NaN;
                return;
            }

            long result = 0;
            var any = false;
            for (; index < str.Length; index++)
            {
                var c = str[index];
                int digit;
                if (c >= '0' && c <= '9') digit = c - '0';
                else if (c >= 'a' && c <= 'z') digit = c - 'a' + 10;
                else if (c >= 'A' && c <= 'Z') digit = c - 'A' + 10;
                else break;

                if (digit >= radix) break;

                result = (result * radix) + digit;
                any = true;
            }

            //no valid leading digits -> NaN, matching JavaScript
            if (!any)
            {
                var.ReturnVar.Float = double.NaN;
                return;
            }

            var.ReturnVar.Int = (int)(sign * result);
        }

        [ScriptMethod("isNaN", "val", AppearAtRoot = true)]
        public static void IsNaNImpl(ScriptVar var, object userData)
        {
            var value = ToNumber(var.GetParameter("val"));
            var.ReturnVar.Int = double.IsNaN(value) ? 1 : 0;
        }

        [ScriptMethod("isFinite", "val", AppearAtRoot = true)]
        public static void IsFiniteImpl(ScriptVar var, object userData)
        {
            var value = ToNumber(var.GetParameter("val"));
            var.ReturnVar.Int = (!double.IsNaN(value) && !double.IsInfinity(value)) ? 1 : 0;
        }

        private static double ToNumber(ScriptVar value)
        {
            if (value.IsInt) return value.Int;
            if (value.IsDouble) return value.Float;
            if (value.IsNull) return 0;

            if (value.IsString &&
                double.TryParse(value.String, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return double.NaN;
        }

        [ScriptMethod("parseFloat", "str")]
        [ScriptMethod("parseFloat", "str", AppearAtRoot = true)]
        public static void IntParseFloatImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("str").String;

            if (double.TryParse(str, out var intResult) == false)
            {
                intResult = 0;
            }

            var.ReturnVar.Float = intResult;
        }

        [ScriptMethod("valueOf", "str")]
        public static void IntValueOfImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("str").String;

            var intResult = Convert.ToInt32(str[0]);

            var.ReturnVar.Int = intResult;
        }
    }
}
