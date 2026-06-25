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
    [ScriptClass("Number")]
    public static class NumberFunctionProvider
    {
        private const double MaxSafeInteger = 9007199254740991.0;

        // Returns false (and leaves d as 0) when x is not a numeric type,
        // allowing callers to early-return false without duplicating the type check.
        private static bool TryGetNumericFloat(ScriptVar x, out double d)
        {
            d = 0.0;
            if (!x.IsInt && !x.IsDouble) return false;
            d = x.Float;
            return true;
        }

        // ── Static type-test methods ──────────────────────────────────────────

        [ScriptMethod("isInteger", "x")]
        public static void NumberIsIntegerImpl(ScriptVar var, object userData)
        {
            if (!TryGetNumericFloat(var.GetParameter("x"), out var d)) { var.ReturnVar.Int = 0; return; }
            var.ReturnVar.Int = (!double.IsNaN(d) && !double.IsInfinity(d) && Math.Floor(d) == d) ? 1 : 0;
        }

        [ScriptMethod("isFinite", "x")]
        public static void NumberIsFiniteImpl(ScriptVar var, object userData)
        {
            if (!TryGetNumericFloat(var.GetParameter("x"), out var d)) { var.ReturnVar.Int = 0; return; }
            var.ReturnVar.Int = double.IsFinite(d) ? 1 : 0;
        }

        [ScriptMethod("isNaN", "x")]
        public static void NumberIsNaNImpl(ScriptVar var, object userData)
        {
            if (!TryGetNumericFloat(var.GetParameter("x"), out var d)) { var.ReturnVar.Int = 0; return; }
            var.ReturnVar.Int = double.IsNaN(d) ? 1 : 0;
        }

        [ScriptMethod("isSafeInteger", "x")]
        public static void NumberIsSafeIntegerImpl(ScriptVar var, object userData)
        {
            if (!TryGetNumericFloat(var.GetParameter("x"), out var d)) { var.ReturnVar.Int = 0; return; }
            var.ReturnVar.Int = (!double.IsNaN(d) && !double.IsInfinity(d) &&
                                 Math.Floor(d) == d && Math.Abs(d) <= MaxSafeInteger) ? 1 : 0;
        }

        // ── Static constants ──────────────────────────────────────────────────

        [ScriptProperty("MAX_SAFE_INTEGER")]
        public static void NumberMaxSafeIntegerImpl(ScriptVar var, object userData)
        {
            var.Float = MaxSafeInteger;
        }

        [ScriptProperty("MIN_SAFE_INTEGER")]
        public static void NumberMinSafeIntegerImpl(ScriptVar var, object userData)
        {
            var.Float = -MaxSafeInteger;
        }

        [ScriptProperty("MAX_VALUE")]
        public static void NumberMaxValueImpl(ScriptVar var, object userData)
        {
            var.Float = double.MaxValue;
        }

        [ScriptProperty("MIN_VALUE")]
        public static void NumberMinValueImpl(ScriptVar var, object userData)
        {
            var.Float = double.Epsilon;
        }

        [ScriptProperty("EPSILON")]
        public static void NumberEpsilonImpl(ScriptVar var, object userData)
        {
            var.Float = 2.220446049250313e-16;
        }

        [ScriptProperty("POSITIVE_INFINITY")]
        public static void NumberPositiveInfinityImpl(ScriptVar var, object userData)
        {
            var.Float = double.PositiveInfinity;
        }

        [ScriptProperty("NEGATIVE_INFINITY")]
        public static void NumberNegativeInfinityImpl(ScriptVar var, object userData)
        {
            var.Float = double.NegativeInfinity;
        }

        [ScriptProperty("NaN")]
        public static void NumberNaNImpl(ScriptVar var, object userData)
        {
            var.Float = double.NaN;
        }

        // ── Instance methods (called on a number value via "this") ────────────

        [ScriptMethod("toFixed", "digits")]
        public static void NumberToFixedImpl(ScriptVar var, object userData)
        {
            var d = var.GetParameter("this").Float;
            var digitsVar = var.GetParameter("digits");
            var digits = digitsVar.IsUndefined ? 0 : Math.Clamp(digitsVar.Int, 0, 100);
            var.ReturnVar.String = d.ToString("F" + digits, CultureInfo.InvariantCulture);
        }

        [ScriptMethod("toString", "radix")]
        public static void NumberToStringImpl(ScriptVar var, object userData)
        {
            var d = var.GetParameter("this").Float;
            var radixVar = var.GetParameter("radix");
            var radix = radixVar.IsUndefined ? 10 : radixVar.Int;
            if (radix == 10)
            {
                // Reuse the engine's ECMAScript number formatter (same exponential
                // threshold as implicit string coercion) by reading a double's String.
                var.ReturnVar.String = ScriptVar.FromDouble(d).String;
                return;
            }
            if (radix < 2 || radix > 36) { var.ReturnVar.String = "NaN"; return; }
            var.ReturnVar.String = ToRadixString((long)d, radix);
        }

        // Convert an integer to a string in any base 2..36 (JS supports the full
        // range; .NET's Convert.ToString only handles 2/8/10/16).
        private static string ToRadixString(long value, int radix)
        {
            if (value == 0) return "0";
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            var negative = value < 0;
            var n = negative ? -value : value;
            var sb = new System.Text.StringBuilder();
            while (n > 0)
            {
                sb.Insert(0, digits[(int)(n % radix)]);
                n /= radix;
            }
            if (negative) sb.Insert(0, '-');
            return sb.ToString();
        }

        [ScriptMethod("toExponential", "digits")]
        public static void NumberToExponentialImpl(ScriptVar var, object userData)
        {
            var d = var.GetParameter("this").Float;
            var digitsVar = var.GetParameter("digits");
            var digits = digitsVar.IsUndefined ? 6 : Math.Clamp(digitsVar.Int, 0, 100);
            var.ReturnVar.String = d.ToString("E" + digits, CultureInfo.InvariantCulture).ToLower();
        }

        [ScriptMethod("toPrecision", "digits")]
        public static void NumberToPrecisionImpl(ScriptVar var, object userData)
        {
            var d = var.GetParameter("this").Float;
            var digitsVar = var.GetParameter("digits");
            if (digitsVar.IsUndefined)
            {
                var.ReturnVar.String = d.ToString(CultureInfo.InvariantCulture);
                return;
            }
            var digits = Math.Clamp(digitsVar.Int, 1, 100);
            var.ReturnVar.String = d.ToString("G" + digits, CultureInfo.InvariantCulture);
        }
    }
}
