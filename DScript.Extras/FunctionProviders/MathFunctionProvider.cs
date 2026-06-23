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

using System;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("Math")]
    public static class MathFunctionProvider
    {
        // Static Random instance for better randomness and performance
        private static readonly Random Random = new Random();
        
        [ScriptMethod("min", "val1", "val2")]
        public static void MathMinImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Min(var.GetParameter("val1").Float, var.GetParameter("val2").Float);
        }

        [ScriptMethod("max", "val1", "val2")]
        public static void MathMaxImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Max(var.GetParameter("val1").Float, var.GetParameter("val2").Float);
        }

        [ScriptMethod("floor", "val")]
        public static void Floor(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Floor(var.GetParameter("val").Float);
        }

        [ScriptMethod("ceil", "val")]
        public static void Ceil(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Ceiling(var.GetParameter("val").Float);
        }

        [ScriptMethod("round", "val")]
        public static void Round(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Round(var.GetParameter("val").Float);
        }

        [ScriptMethod("abs", "val")]
        public static void MathAbsImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Abs(var.GetParameter("val").Float);
        }

        [ScriptMethod("acos", "val")]
        public static void MathACosImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Acos(var.GetParameter("val").Float);
        }

        [ScriptMethod("asin", "val")]
        public static void MathASinImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Asin(var.GetParameter("val").Float);
        }

        [ScriptMethod("atan", "val")]
        public static void MathATanImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Atan(var.GetParameter("val").Float);
        }

        [ScriptMethod("atan2", "y", "x")]
        public static void MathATan2Impl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Atan2(var.GetParameter("y").Float, var.GetParameter("x").Float);
        }

        [ScriptMethod("cos", "val")]
        public static void MathCosImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Cos(var.GetParameter("val").Float);
        }

        [ScriptMethod("cosh", "val")]
        public static void MathCoshImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Cosh(var.GetParameter("val").Float);
        }

        [ScriptMethod("exp", "val")]
        public static void MathExpImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Exp(var.GetParameter("val").Float);
        }

        [ScriptMethod("log", "val")]
        public static void MathLogImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Log(var.GetParameter("val").Float);
        }

        [ScriptMethod("pow", "x", "y")]
        public static void MathPowImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Pow(var.GetParameter("x").Float, var.GetParameter("y").Float);
        }

        [ScriptMethod("sin", "val")]
        public static void MathSinImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Sin(var.GetParameter("val").Float);
        }

        [ScriptMethod("sinh", "val")]
        public static void MathSinhImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Sinh(var.GetParameter("val").Float);
        }

        [ScriptMethod("sqrt", "val")]
        public static void MathSqrtImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Sqrt(var.GetParameter("val").Float);
        }

        [ScriptMethod("tan", "val")]
        public static void MathTanImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Tan(var.GetParameter("val").Float);
        }

        [ScriptMethod("tanh", "val")]
        public static void MathTanhImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Tanh(var.GetParameter("val").Float);
        }

        [ScriptMethod("rand")]
        [ScriptMethod("random")]
        public static void MathRandomImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Random.NextDouble();
        }


        [ScriptMethod("randomInt", "min", "max")]
        [ScriptMethod("randInt", "min", "max")]
        public static void MathRandomIntImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = Random.Next(var.GetParameter("min").Int, var.GetParameter("max").Int);
        }

        [ScriptProperty("PI")]
        public static void MathPiImpl(ScriptVar var, object userData)
        {
            var.Float = Math.PI;
        }

        [ScriptProperty("E")]
        public static void MathEImpl(ScriptVar var, object userData)
        {
            var.Float = Math.E;
        }

        [ScriptProperty("SQRT2")]
        public static void MathSqrt2Impl(ScriptVar var, object userData)
        {
            var.Float = Math.Sqrt(2);
        }

        [ScriptProperty("SQRT1_2")]
        public static void MathSqrt12Impl(ScriptVar var, object userData)
        {
            var.Float = Math.Sqrt(0.5);
        }

        [ScriptProperty("LN2")]
        public static void MathLn2Impl(ScriptVar var, object userData)
        {
            var.Float = Math.Log(2);
        }

        [ScriptProperty("LN10")]
        public static void MathLn10Impl(ScriptVar var, object userData)
        {
            var.Float = Math.Log(10);
        }

        [ScriptProperty("LOG2E")]
        public static void MathLog2EImpl(ScriptVar var, object userData)
        {
            var.Float = Math.Log(Math.E, 2);
        }

        [ScriptProperty("LOG10E")]
        public static void MathLog10EImpl(ScriptVar var, object userData)
        {
            var.Float = Math.Log10(Math.E);
        }

        [ScriptMethod("trunc", "val")]
        public static void MathTruncImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Truncate(var.GetParameter("val").Float);
        }

        [ScriptMethod("sign", "val")]
        public static void MathSignImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = Math.Sign(var.GetParameter("val").Float);
        }

        [ScriptMethod("hypot", "a0", "a1", "a2", "a3", "a4")]
        public static void MathHypotImpl(ScriptVar var, object userData)
        {
            var sum = 0.0;
            var a0 = var.GetParameter("a0");
            if (a0.IsArray)
            {
                // Array form: Math.hypot([3, 4])
                var len = a0.GetArrayLength();
                for (var i = 0; i < len; i++) { var v = a0.GetArrayIndex(i).Float; sum += v * v; }
            }
            else
            {
                // Variadic form: Math.hypot(3, 4, ...)
                foreach (var name in new[] { "a0", "a1", "a2", "a3", "a4" })
                {
                    var v = var.GetParameter(name);
                    if (v.IsUndefined) break;
                    sum += v.Float * v.Float;
                }
            }
            var.ReturnVar.Float = Math.Sqrt(sum);
        }

        [ScriptMethod("log2", "val")]
        public static void MathLog2Impl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Log2(var.GetParameter("val").Float);
        }

        [ScriptMethod("log10", "val")]
        public static void MathLog10Impl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Log10(var.GetParameter("val").Float);
        }

        [ScriptMethod("cbrt", "val")]
        public static void MathCbrtImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = Math.Cbrt(var.GetParameter("val").Float);
        }

        [ScriptMethod("clamp", "x", "lo", "hi")]
        public static void MathClampImpl(ScriptVar var, object userData)
        {
            var x  = var.GetParameter("x").Float;
            var lo = var.GetParameter("lo").Float;
            var hi = var.GetParameter("hi").Float;
            var.ReturnVar.Float = Math.Clamp(x, lo, hi);
        }

        [ScriptMethod("fround", "val")]
        public static void MathFroundImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = (double)(float)var.GetParameter("val").Float;
        }

        [ScriptMethod("imul", "a", "b")]
        public static void MathImulImpl(ScriptVar var, object userData)
        {
            var a = (int)var.GetParameter("a").Float;
            var b = (int)var.GetParameter("b").Float;
            var.ReturnVar.Int = unchecked(a * b);
        }
    }
}
