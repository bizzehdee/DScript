using System;
using System.Collections.Generic;
using System.Text;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("Integer")]
    public static class IntegerFunctionProvider
    {
        [ScriptMethod("parseInt", "str")]
        [ScriptMethod("parseInt", "str", AppearAtRoot = true)]
        public static void IntParseIntImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("str").String;

            if (int.TryParse(str, out int intResult) == false)
            {
                intResult = 0;
            }

            var.ReturnVar.Int = intResult;
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
