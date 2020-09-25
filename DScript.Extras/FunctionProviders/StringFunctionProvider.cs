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
            var search = var.GetParameter("search").String;
            var searchInStr = var.GetParameter("this").String;

            var index = searchInStr.IndexOf(search);

            var.ReturnVar.Int = index;
        }

        [ScriptMethod("substring", "lo", "hi")]
        public static void StringSubStringImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("this").String;
            var lo = var.GetParameter("lo").Int;
            var hi = var.GetParameter("hi").Int;

            var substr = str.Substring(lo, hi - lo);

            var.ReturnVar.String = substr;
        }

        [ScriptMethod("charAt", "pos")]
        public static void StringCharAtImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("this").String;
            var pos = var.GetParameter("pos").Int;

            var charStr = string.Empty;
            if (str.Length > pos)
            {
                charStr = str[pos].ToString();
            }

            var.ReturnVar.String = charStr;
        }

        [ScriptMethod("charCodeAt", "pos")]
        public static void StringCharCodeAtImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("this").String;
            var pos = var.GetParameter("pos").Int;

            var charCode = 0;
            if (str.Length > pos)
            {
                charCode = Convert.ToInt32(str[pos]);
            }

            var.ReturnVar.Int = charCode;
        }

        [ScriptMethod("fromCharCode", "char")]
        public static void StringFromCharCodeImpl(ScriptVar var, object userData)
        {
            var charVar = var.GetParameter("char").Int;

            var charAsChar = Convert.ToChar(charVar);

            var.ReturnVar.String = charAsChar.ToString();
        }

        [ScriptMethod("split", "sep")]
        public static void StringSplitImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("this").String;
            var sep = var.GetParameter("sep").String;

            var spltStrs = str.Split(new[] { sep }, StringSplitOptions.None);


            var.ReturnVar.SetArray();
            for (int x = 0; x < spltStrs.Length; x++)
            {
                var.ReturnVar.SetArrayIndex(x, new ScriptVar(spltStrs[x]));
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
