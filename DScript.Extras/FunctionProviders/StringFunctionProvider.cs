using System;
using System.Collections.Generic;
using System.Text;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("String")]
    public static class StringFunctionProvider
    {
        [ScriptMethod("indexOf", "search")]
        public static void StringIndexOfImpl(ScriptVar var, object userData)
        {
            var search = var.GetParameter("search").GetString();
            var searchInStr = var.GetParameter("this").GetString();

            var index = searchInStr.IndexOf(search);

            var.GetReturnVar().SetInt(index);
        }

        [ScriptMethod("substring", "lo", "hi")]
        public static void StringSubStringImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("this").GetString();
            var lo = var.GetParameter("lo").GetInt();
            var hi = var.GetParameter("hi").GetInt();

            var substr = str.Substring(lo, hi - lo);

            var.GetReturnVar().SetString(substr);
        }

        [ScriptMethod("charAt", "pos")]
        public static void StringCharAtImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("this").GetString();
            var pos = var.GetParameter("pos").GetInt();

            var charStr = string.Empty;
            if (str.Length > pos)
            {
                charStr = str[pos].ToString();
            }

            var.GetReturnVar().SetString(charStr);
        }

        [ScriptMethod("charCodeAt", "pos")]
        public static void StringCharCodeAtImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("this").GetString();
            var pos = var.GetParameter("pos").GetInt();

            var charCode = 0;
            if (str.Length > pos)
            {
                charCode = Convert.ToInt32(str[pos]);
            }

            var.GetReturnVar().SetInt(charCode);
        }

        [ScriptMethod("fromCharCode", "char")]
        public static void StringFromCharCodeImpl(ScriptVar var, object userData)
        {
            var charVar = var.GetParameter("char").GetInt();

            var charAsChar = Convert.ToChar(charVar);

            var.GetReturnVar().SetString(charAsChar.ToString());
        }

        [ScriptMethod("split", "sep")]
        public static void StringSplitImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("this").GetString();
            var sep = var.GetParameter("sep").GetString();

            var spltStrs = str.Split(new[] { sep }, StringSplitOptions.None);


            var.GetReturnVar().SetArray();
            for (int x = 0; x < spltStrs.Length; x++)
            {
                var.GetReturnVar().SetArrayIndex(x, new ScriptVar(spltStrs[x]));
            }

        }

        //trim
        //concat
        //toUpperCase
        //toLowerCase
        //replace
        //substr
        //search
        //lastIndexOf
    }
}
