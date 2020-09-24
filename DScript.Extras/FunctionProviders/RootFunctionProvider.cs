using System;
using System.Collections.Generic;
using System.Text;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("root")]
    public static class RootFunctionProvider
    {
        [ScriptMethod("eval", "str", AppearAtRoot = true)]
        public static void EvalImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var script = var.GetParameter("str").GetString();
            var returnVal = engine.EvalComplex(script);
            var.SetReturnVar(returnVal.Var);
        }

        [ScriptMethod("exec", "str", AppearAtRoot = true)]
        public static void ExecImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var script = var.GetParameter("str").GetString();
            engine.Execute(script);
        }

        [ScriptMethod("trace", AppearAtRoot = true)]
        public static void TraceImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            engine.Root.Trace(0, null);
        }

        [ScriptMethod("charToInt", AppearAtRoot = true)]
        public static void CharToIntImpl(ScriptVar var, object userData)
        {
            var charStr = var.GetParameter("char").GetString();
            var charParam = charStr[0];
            var charAsInt = Convert.ToInt32(charParam);
            var.GetReturnVar().SetInt(charAsInt);
        }
    }
}
