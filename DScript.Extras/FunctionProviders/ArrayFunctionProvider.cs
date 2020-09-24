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

            bool contains = false;
            while (v != null)
            {
                if (v.Var.Equal(obj))
                {
                    contains = true;
                    break;
                }
                v = v.Next;
            }

            var.GetReturnVar().SetInt(contains ? 1 : 0);
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

            var separator = var.GetParameter("separator").GetString();
            var arr = var.GetParameter("this");

            var arrayLength = arr.GetArrayLength();
            for (int x = 0; x < arrayLength; x++)
            {
                if (x > 0)
                {
                    builder.Append(separator);
                }

                var str = arr.GetArrayIndex(x).GetString();
                builder.Append(str);

            }

            var.GetReturnVar().SetString(builder.ToString());
        }
    }
}
