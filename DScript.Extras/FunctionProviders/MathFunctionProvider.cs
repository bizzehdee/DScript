using System;
using System.Collections.Generic;
using System.Text;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("Math")]
    public static class MathFunctionProvider
    {
        [ScriptMethod("min", "val1", "val2")]
        public static void MathMinImpl(ScriptVar var, object userData)
        {
            var val1 = var.GetParameter("val1").GetDouble();
            var val2 = var.GetParameter("val2").GetDouble();

            var returnVal = Math.Min(val1, val2);

            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("max", "val1", "val2")]
        public static void MathMaxImpl(ScriptVar var, object userData)
        {
            var val1 = var.GetParameter("val1").GetDouble();
            var val2 = var.GetParameter("val2").GetDouble();

            var returnVal = Math.Max(val1, val2);

            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("floor", "val")]
        public static void Floor(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val").GetDouble();
            var returnVal = Math.Floor(val);
            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("ceil", "val")]
        public static void Ceil(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val").GetDouble();
            var returnVal = Math.Ceiling(val);
            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("round", "val")]
        public static void Round(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val").GetDouble();
            var returnVal = Math.Round(val);
            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("abs", "val")]
        public static void MathAbsImpl(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val").GetDouble();
            var returnVal = Math.Abs(val);
            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("acos", "val")]
        public static void MathACosImpl(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val").GetDouble();
            var returnVal = Math.Acos(val);
            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("asin", "val")]
        public static void MathASinImpl(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val").GetDouble();
            var returnVal = Math.Asin(val);
            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("atan", "val")]
        public static void MathATanImpl(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val").GetDouble();
            var returnVal = Math.Atan(val);
            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("atan2", "y", "x")]
        public static void MathATan2Impl(ScriptVar var, object userData)
        {
            var y = var.GetParameter("y").GetDouble();
            var x = var.GetParameter("x").GetDouble();
            var returnVal = Math.Atan2(y, x);
            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("cos", "val")]
        public static void MathCosImpl(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val").GetDouble();
            var returnVal = Math.Cos(val);
            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("cosh", "val")]
        public static void MathCoshImpl(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val").GetDouble();
            var returnVal = Math.Cosh(val);
            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("exp", "val")]
        public static void MathExpImpl(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val").GetDouble();
            var returnVal = Math.Exp(val);
            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("log", "val")]
        public static void MathLogImpl(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val").GetDouble();
            var returnVal = Math.Log(val);
            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("pow", "x", "y")]
        public static void MathPowImpl(ScriptVar var, object userData)
        {

            var y = var.GetParameter("y").GetDouble();
            var x = var.GetParameter("x").GetDouble();

            var returnVal = Math.Pow(x, y);
            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("sin", "val")]
        public static void MathSinImpl(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val").GetDouble();
            var returnVal = Math.Sin(val);
            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("sinh", "val")]
        public static void MathSinhImpl(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val").GetDouble();
            var returnVal = Math.Sinh(val);
            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("sqrt", "val")]
        public static void MathSqrtImpl(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val").GetDouble();
            var returnVal = Math.Sqrt(val);
            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("tan", "val")]
        public static void MathTanImpl(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val").GetDouble();
            var returnVal = Math.Tan(val);
            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("tanh", "val")]
        public static void MathTanhImpl(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val").GetDouble();
            var returnVal = Math.Tanh(val);
            var.ReturnVar.SetDouble(returnVal);
        }

        [ScriptMethod("rand")]
        [ScriptMethod("random")]
        public static void MathRandomImpl(ScriptVar var, object userData)
        {
            var random = new Random();
            var randomNumber = random.NextDouble();
            var.GetReturnVar().SetDouble(randomNumber);
        }


        [ScriptMethod("randomInt", "min", "max")]
        [ScriptMethod("randInt", "min", "max")]
        public static void MathRandomIntImpl(ScriptVar var, object userData)
        {
            var random = new Random();

            var min = var.GetParameter("min").GetInt();
            var max = var.GetParameter("max").GetInt();

            var randomInt = random.Next(min, max);

            var.GetReturnVar().SetInt(randomInt);
        }

        [ScriptProperty("PI")]
        public static void MathPiImpl(ScriptVar var, object userData)
        {
            var.SetDouble(Math.PI);
        }

        [ScriptProperty("E")]
        public static void MathEImpl(ScriptVar var, object userData)
        {
            var.SetDouble(Math.E);
        }

        [ScriptProperty("SQRT2")]
        public static void MathSqrt2Impl(ScriptVar var, object userData)
        {
            var.SetDouble(Math.Sqrt(2));
        }

        [ScriptProperty("SQRT1_2")]
        public static void MathSqrt12Impl(ScriptVar var, object userData)
        {
            var.SetDouble(Math.Sqrt(0.5));
        }

        [ScriptProperty("LN2")]
        public static void MathLn2Impl(ScriptVar var, object userData)
        {
            var.SetDouble(Math.Log(2));
        }

        [ScriptProperty("LN10")]
        public static void MathLn10Impl(ScriptVar var, object userData)
        {
            var.SetDouble(Math.Log(10));
        }

        [ScriptProperty("LOG2E")]
        public static void MathLog2EImpl(ScriptVar var, object userData)
        {
            var.SetDouble(Math.Log(Math.E, 2));
        }

        [ScriptProperty("LOG10E")]
        public static void MathLog10EImpl(ScriptVar var, object userData)
        {
            var.SetDouble(Math.Log10(Math.E));
        }
    }
}
