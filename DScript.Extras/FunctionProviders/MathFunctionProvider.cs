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
    }
}
