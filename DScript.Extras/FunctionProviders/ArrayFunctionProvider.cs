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
using System.Collections.Generic;
using System.Text;

namespace DScript.Extras.FunctionProviders
{

    [ScriptClass("Array")]
    public static class ArrayFunctionProvider
    {
        [ScriptMethod("contains", "obj")]
        public static void ArrayContainsImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var v = var.GetParameter("this").FirstChild;

            var contains = false;
            while (v != null)
            {
                if (v.Var.Equal(obj))
                {
                    contains = true;
                    break;
                }
                v = v.Next;
            }

            var.ReturnVar.Int = (contains ? 1 : 0);
        }

        [ScriptMethod("remove", "obj")]
        public static void ArrayRemoveImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var v = var.GetParameter("this").FirstChild;
            var removed = new List<int>();

            while (v != null)
            {
                if (v.Var.Equal(obj))
                {
                    removed.Add(v.GetIntName());
                }

                v = v.Next;
            }

            v = var.GetParameter("this").FirstChild;
            while (v != null)
            {
                var n = v.GetIntName();
                var newn = n;
                for (var i = 0; i < removed.Count; i++)
                {
                    if (n >= removed[i])
                    {
                        newn--;
                    }
                }

                if (newn != n)
                {
                    v.SetIntName(newn);
                }

                v = v.Next;
            }
        }

        [ScriptMethod("join", "separator")]
        public static void ArrayJoinImpl(ScriptVar var, object userData)
        {
            var builder = new StringBuilder();

            var separator = var.GetParameter("separator").String;
            var arr = var.GetParameter("this");

            var arrayLength = arr.GetArrayLength();
            for (var x = 0; x < arrayLength; x++)
            {
                if (x > 0)
                {
                    builder.Append(separator);
                }

                var str = arr.GetArrayIndex(x).String;
                builder.Append(str);

            }

            var.ReturnVar.String = builder.ToString();
        }

        [ScriptMethod("push", "val")]
        public static void ArrayPushImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var val = var.GetParameter("val");

            arr.SetArrayIndex(arr.GetArrayLength(), val);

            var.ReturnVar.Int = arr.GetArrayLength();
        }

        [ScriptMethod("pop")]
        public static void ArrayPopImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var len = arr.GetArrayLength();

            if (len == 0)
            {
                var.ReturnVar.SetUndefined();
                return;
            }

            var link = arr.FindChild((len - 1).ToString());
            if (link == null)
            {
                var.ReturnVar.SetUndefined();
                return;
            }

            var.ReturnVar = link.Var;
            arr.RemoveLink(link);
        }

        [ScriptMethod("shift")]
        public static void ArrayShiftImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var len = arr.GetArrayLength();

            if (len == 0)
            {
                var.ReturnVar.SetUndefined();
                return;
            }

            var first = arr.FindChild("0");
            if (first != null)
            {
                var.ReturnVar = first.Var;
                arr.RemoveLink(first);
            }
            else
            {
                var.ReturnVar.SetUndefined();
            }

            //shift the remaining indices down by one
            var link = arr.FirstChild;
            while (link != null)
            {
                if (int.TryParse(link.Name, out var n))
                {
                    link.SetIntName(n - 1);
                }
                link = link.Next;
            }
        }

        [ScriptMethod("unshift", "val")]
        public static void ArrayUnshiftImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var val = var.GetParameter("val");

            if (!val.IsUndefined)
            {
                //make room at index 0
                var link = arr.FirstChild;
                while (link != null)
                {
                    if (int.TryParse(link.Name, out var n))
                    {
                        link.SetIntName(n + 1);
                    }
                    link = link.Next;
                }

                arr.SetArrayIndex(0, val);
            }

            var.ReturnVar.Int = arr.GetArrayLength();
        }

        [ScriptMethod("indexOf", "obj")]
        public static void ArrayIndexOfImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var obj = var.GetParameter("obj");
            var len = arr.GetArrayLength();

            var found = -1;
            for (var x = 0; x < len; x++)
            {
                if (arr.GetArrayIndex(x).Equal(obj))
                {
                    found = x;
                    break;
                }
            }

            var.ReturnVar.Int = found;
        }

        [ScriptMethod("slice", "start", "end")]
        public static void ArraySliceImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var len = arr.GetArrayLength();

            var startVar = var.GetParameter("start");
            var endVar = var.GetParameter("end");

            var start = startVar.IsUndefined ? 0 : startVar.Int;
            var end = endVar.IsUndefined ? len : endVar.Int;

            //negative indices count from the end; everything is clamped to range
            if (start < 0) start = Math.Max(len + start, 0);
            if (end < 0) end = Math.Max(len + end, 0);
            if (start > len) start = len;
            if (end > len) end = len;

            var.ReturnVar.SetArray();

            var idx = 0;
            for (var x = start; x < end; x++)
            {
                var.ReturnVar.SetArrayIndex(idx++, arr.GetArrayIndex(x).DeepCopy());
            }
        }

        [ScriptMethod("reverse")]
        public static void ArrayReverseImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var len = arr.GetArrayLength();

            var values = new List<ScriptVar>();
            for (var x = 0; x < len; x++)
            {
                values.Add(arr.GetArrayIndex(x).DeepCopy());
            }

            for (var x = 0; x < len; x++)
            {
                arr.SetArrayIndex(x, values[len - 1 - x]);
            }

            var.ReturnVar = arr;
        }

        [ScriptMethod("sort")]
        public static void ArraySortImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var len = arr.GetArrayLength();

            var values = new List<ScriptVar>();
            for (var x = 0; x < len; x++)
            {
                values.Add(arr.GetArrayIndex(x).DeepCopy());
            }

            //default ordering is lexicographic, matching JavaScript's default sort
            values.Sort((a, b) => string.CompareOrdinal(a.String, b.String));

            for (var x = 0; x < len; x++)
            {
                arr.SetArrayIndex(x, values[x]);
            }

            var.ReturnVar = arr;
        }
    }
}
