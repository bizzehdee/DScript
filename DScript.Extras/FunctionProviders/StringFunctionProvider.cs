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
using System.Text.RegularExpressions;

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

            var charStr = (str.Length > pos) ? str[pos].ToString() : string.Empty;
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

            var spltStrs = str.Split(sep);

            var.ReturnVar.SetArray();
            for (var x = 0; x < spltStrs.Length; x++)
            {
                var.ReturnVar.SetArrayIndex(x, new ScriptVar(spltStrs[x]));
            }

        }

        [ScriptMethod("match", "regex")]
        public static void StringMatchImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("this").String;
            var regex = (Regex)var.GetParameter("regex").GetData();

            var.ReturnVar.SetArray();

            var match = regex.Match(str);

            if (match.Success)
            {
                var idx = 0;
                foreach (Group m in match.Groups)
                {
                    var.ReturnVar.SetArrayIndex(idx++, new ScriptVar(m.Value));
                }
            }
            else
            {
                var.ReturnVar.SetArrayIndex(0, new ScriptVar(""));
            }
        }

        [ScriptMethod("trim")]
        public static void StringTrimImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("this").String;

            var trimStr = str.Trim();

            var.ReturnVar.String = trimStr;
        }

        [ScriptMethod("concat", "str1")]
        public static void StringConcatImpl(ScriptVar var, object userData)
        {
            var strThis = var.GetParameter("this").String;
            var str1 = var.GetParameter("str1").String;

            var trimStr = string.Join("", strThis, str1);

            var.ReturnVar.String = trimStr;
        }

        [ScriptMethod("toUpperCase")]
        public static void StringToUpperImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("this").String;

            var upperStr = str.ToUpper();

            var.ReturnVar.String = upperStr;
        }

        [ScriptMethod("toLowerCase")]
        public static void StringToLowerImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("this").String;

            var lowerStr = str.ToLower();

            var.ReturnVar.String = lowerStr;
        }

        [ScriptMethod("replace", "what", "with")]
        public static void StringReplaceImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("this").String;
            var what = var.GetParameter("what").String;
            var with = var.GetParameter("with").String;

            var replacedStr = str.Replace(what, with);

            var.ReturnVar.String = replacedStr;
        }

        [ScriptMethod("substr", "start", "length")]
        public static void StringSubStr2Impl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("this").String;
            var start = var.GetParameter("start").Int;
            var length = var.GetParameter("length").Int;

            var subStr = str.Substring(start, length);

            var.ReturnVar.String = subStr;
        }

        [ScriptMethod("lastIndexOf", "searchString", "position")]
        public static void StringLastIndexOfImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("this").String;
            var searchString = var.GetParameter("searchString").String;
            var position = var.GetParameter("position").Int;

            var lastIndex = str.LastIndexOf(searchString, position);

            var.ReturnVar.Int = lastIndex;
        }
    }
}
