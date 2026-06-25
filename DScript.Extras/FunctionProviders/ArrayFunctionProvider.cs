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

            // Compute the length once (GetArrayLength can be O(n)); the new length is
            // simply len + 1.
            var len = arr.GetArrayLength();
            arr.SetArrayIndex(len, val);
            var.ReturnVar.Int = len + 1;
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
            var (start, end) = ProviderHelpers.NormalizeSliceRange(
                var.GetParameter("start"), var.GetParameter("end"), len);

            var.ReturnVar.SetArray();
            var idx = 0;
            for (var x = start; x < end; x++)
                var.ReturnVar.SetArrayIndex(idx++, arr.GetArrayIndex(x).DeepCopy());
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

        [ScriptMethod("sort", "compare")]
        public static void ArraySortImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var arr = var.GetParameter("this");
            var compare = var.GetParameter("compare");
            var len = arr.GetArrayLength();

            // Snapshot the element references (not deep copies): sorting reorders
            // references in place, exactly as JS does — copying would be slow and would
            // break reference identity (a[i] === the original object must still hold).
            var values = new List<ScriptVar>(len);
            for (var x = 0; x < len; x++)
            {
                values.Add(arr.GetArrayIndex(x));
            }

            if (compare.IsFunction)
            {
                //use the supplied comparator: negative => a before b
                values.Sort((a, b) => engine.CallFunction(compare, null, a, b).Int);
            }
            else
            {
                //default ordering is lexicographic, matching JavaScript's default sort
                values.Sort((a, b) => string.CompareOrdinal(a.String, b.String));
            }

            for (var x = 0; x < len; x++)
            {
                arr.SetArrayIndex(x, values[x]);
            }

            var.ReturnVar = arr;
        }

        [ScriptMethod("map", "callback")]
        public static void ArrayMapImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var arr = var.GetParameter("this");
            var callback = var.GetParameter("callback");
            var len = arr.GetArrayLength();

            var.ReturnVar.SetArray();
            for (var x = 0; x < len; x++)
            {
                var mapped = engine.CallFunction(callback, null, arr.GetArrayIndex(x), ScriptVar.FromInt(x), arr);
                var.ReturnVar.SetArrayIndex(x, mapped);
            }
        }

        [ScriptMethod("filter", "callback")]
        public static void ArrayFilterImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var arr = var.GetParameter("this");
            var callback = var.GetParameter("callback");
            var len = arr.GetArrayLength();

            var.ReturnVar.SetArray();
            var outIdx = 0;
            for (var x = 0; x < len; x++)
            {
                var element = arr.GetArrayIndex(x);
                var keep = engine.CallFunction(callback, null, element, ScriptVar.FromInt(x), arr);
                if (keep.Bool)
                {
                    var.ReturnVar.SetArrayIndex(outIdx++, element.DeepCopy());
                }
            }
        }

        [ScriptMethod("forEach", "callback")]
        public static void ArrayForEachImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var arr = var.GetParameter("this");
            var callback = var.GetParameter("callback");
            var len = arr.GetArrayLength();

            for (var x = 0; x < len; x++)
            {
                engine.CallFunction(callback, null, arr.GetArrayIndex(x), ScriptVar.FromInt(x), arr);
            }
        }

        [ScriptMethod("reduce", "callback", "initial")]
        public static void ArrayReduceImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var arr = var.GetParameter("this");
            var callback = var.GetParameter("callback");
            var len = arr.GetArrayLength();

            var accumulator = var.GetParameter("initial");
            var start = 0;

            //with no initial value, seed from the first element
            if (accumulator.IsUndefined && len > 0)
            {
                accumulator = arr.GetArrayIndex(0).DeepCopy();
                start = 1;
            }

            for (var x = start; x < len; x++)
            {
                accumulator = engine.CallFunction(callback, null, accumulator, arr.GetArrayIndex(x), ScriptVar.FromInt(x), arr);
            }

            var.ReturnVar = accumulator;
        }

        // ── ES2015+ instance methods ──────────────────────────────────────────

        [ScriptMethod("find", "callback")]
        public static void ArrayFindImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var arr = var.GetParameter("this");
            var callback = var.GetParameter("callback");
            var len = arr.GetArrayLength();
            for (var x = 0; x < len; x++)
            {
                var elem = arr.GetArrayIndex(x);
                if (engine.CallFunction(callback, null, elem, ScriptVar.FromInt(x), arr).Bool)
                {
                    var.ReturnVar = elem;
                    return;
                }
            }
            var.ReturnVar.SetUndefined();
        }

        [ScriptMethod("findIndex", "callback")]
        public static void ArrayFindIndexImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var arr = var.GetParameter("this");
            var callback = var.GetParameter("callback");
            var len = arr.GetArrayLength();
            for (var x = 0; x < len; x++)
            {
                if (engine.CallFunction(callback, null, arr.GetArrayIndex(x), ScriptVar.FromInt(x), arr).Bool)
                {
                    var.ReturnVar.Int = x;
                    return;
                }
            }
            var.ReturnVar.Int = -1;
        }

        [ScriptMethod("findLast", "callback")]
        public static void ArrayFindLastImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var arr = var.GetParameter("this");
            var callback = var.GetParameter("callback");
            var len = arr.GetArrayLength();
            for (var x = len - 1; x >= 0; x--)
            {
                var elem = arr.GetArrayIndex(x);
                if (engine.CallFunction(callback, null, elem, ScriptVar.FromInt(x), arr).Bool)
                {
                    var.ReturnVar = elem;
                    return;
                }
            }
            var.ReturnVar.SetUndefined();
        }

        [ScriptMethod("findLastIndex", "callback")]
        public static void ArrayFindLastIndexImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var arr = var.GetParameter("this");
            var callback = var.GetParameter("callback");
            var len = arr.GetArrayLength();
            for (var x = len - 1; x >= 0; x--)
            {
                if (engine.CallFunction(callback, null, arr.GetArrayIndex(x), ScriptVar.FromInt(x), arr).Bool)
                {
                    var.ReturnVar.Int = x;
                    return;
                }
            }
            var.ReturnVar.Int = -1;
        }

        [ScriptMethod("some", "callback")]
        public static void ArraySomeImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var arr = var.GetParameter("this");
            var callback = var.GetParameter("callback");
            var len = arr.GetArrayLength();
            for (var x = 0; x < len; x++)
            {
                if (engine.CallFunction(callback, null, arr.GetArrayIndex(x), ScriptVar.FromInt(x), arr).Bool)
                {
                    var.ReturnVar.Int = 1;
                    return;
                }
            }
            var.ReturnVar.Int = 0;
        }

        [ScriptMethod("every", "callback")]
        public static void ArrayEveryImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var arr = var.GetParameter("this");
            var callback = var.GetParameter("callback");
            var len = arr.GetArrayLength();
            for (var x = 0; x < len; x++)
            {
                if (!engine.CallFunction(callback, null, arr.GetArrayIndex(x), ScriptVar.FromInt(x), arr).Bool)
                {
                    var.ReturnVar.Int = 0;
                    return;
                }
            }
            var.ReturnVar.Int = 1;
        }

        [ScriptMethod("includes", "val")]
        public static void ArrayIncludesImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var val = var.GetParameter("val");
            var len = arr.GetArrayLength();
            for (var x = 0; x < len; x++)
            {
                if (arr.GetArrayIndex(x).Equal(val)) { var.ReturnVar.Int = 1; return; }
            }
            var.ReturnVar.Int = 0;
        }

        [ScriptMethod("flat", "depth")]
        public static void ArrayFlatImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var depthVar = var.GetParameter("depth");
            var depth = depthVar.IsUndefined ? 1 : depthVar.Int;
            var result = ScriptVar.CreateUndefined();
            result.SetArray();
            FlattenInto(result, arr, depth);
            var.ReturnVar = result;
        }

        private static void FlattenInto(ScriptVar target, ScriptVar src, int depth)
        {
            var len = src.GetArrayLength();
            var outIdx = target.GetArrayLength();
            for (var i = 0; i < len; i++)
            {
                var elem = src.GetArrayIndex(i);
                if (depth > 0 && elem.IsArray)
                    FlattenInto(target, elem, depth - 1);
                else
                    target.SetArrayIndex(outIdx++, elem.DeepCopy());
            }
        }

        [ScriptMethod("flatMap", "callback")]
        public static void ArrayFlatMapImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var arr = var.GetParameter("this");
            var callback = var.GetParameter("callback");
            var len = arr.GetArrayLength();
            var mapped = ScriptVar.CreateUndefined();
            mapped.SetArray();
            for (var x = 0; x < len; x++)
                mapped.SetArrayIndex(x, engine.CallFunction(callback, null, arr.GetArrayIndex(x), ScriptVar.FromInt(x), arr));
            var result = ScriptVar.CreateUndefined();
            result.SetArray();
            FlattenInto(result, mapped, 1);
            var.ReturnVar = result;
        }

        [ScriptMethod("fill", "val", "start", "end")]
        public static void ArrayFillImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var val = var.GetParameter("val");
            var len = arr.GetArrayLength();
            var (start, end) = ProviderHelpers.NormalizeSliceRange(
                var.GetParameter("start"), var.GetParameter("end"), len);
            for (var x = start; x < end; x++)
                arr.SetArrayIndex(x, val.DeepCopy());
            var.ReturnVar = arr;
        }

        [ScriptMethod("concat", "other")]
        public static void ArrayConcatImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var other = var.GetParameter("other");
            var result = ScriptVar.CreateUndefined();
            result.SetArray();
            var outIdx = 0;
            var lenA = arr.GetArrayLength();
            for (var x = 0; x < lenA; x++)
                result.SetArrayIndex(outIdx++, arr.GetArrayIndex(x).DeepCopy());
            var lenB = other.GetArrayLength();
            for (var x = 0; x < lenB; x++)
                result.SetArrayIndex(outIdx++, other.GetArrayIndex(x).DeepCopy());
            var.ReturnVar = result;
        }

        [ScriptMethod("splice", "start", "deleteCount", "item")]
        public static void ArraySpliceImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var len = arr.GetArrayLength();
            var startVar = var.GetParameter("start");
            var deleteCountVar = var.GetParameter("deleteCount");
            var item = var.GetParameter("item");

            var start = ProviderHelpers.NormalizeIndex(startVar.IsUndefined ? 0 : startVar.Int, len);

            var deleteCount = deleteCountVar.IsUndefined ? len - start : Math.Min(Math.Max(deleteCountVar.Int, 0), len - start);

            // Build removed array
            var removed = ScriptVar.CreateUndefined();
            removed.SetArray();
            for (var i = 0; i < deleteCount; i++)
                removed.SetArrayIndex(i, arr.GetArrayIndex(start + i).DeepCopy());

            // Collect remaining elements
            var items = new System.Collections.Generic.List<ScriptVar>();
            for (var i = 0; i < start; i++)
                items.Add(arr.GetArrayIndex(i).DeepCopy());
            if (!item.IsUndefined)
                items.Add(item.DeepCopy());
            for (var i = start + deleteCount; i < len; i++)
                items.Add(arr.GetArrayIndex(i).DeepCopy());

            // Rebuild array
            for (var i = 0; i < items.Count; i++)
                arr.SetArrayIndex(i, items[i]);
            // Remove trailing slots if array shrank
            for (var i = items.Count; i < len; i++)
            {
                var link = arr.FindChild(i.ToString());
                if (link != null) arr.RemoveLink(link);
            }

            var.ReturnVar = removed;
        }

        [ScriptMethod("at", "index")]
        public static void ArrayAtImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var index = var.GetParameter("index").Int;
            var len = arr.GetArrayLength();
            if (index < 0) index = len + index;
            if (index < 0 || index >= len) { var.ReturnVar.SetUndefined(); return; }
            var.ReturnVar = arr.GetArrayIndex(index);
        }

        [ScriptMethod("entries")]
        public static void ArrayEntriesImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var len = arr.GetArrayLength();
            var result = ScriptVar.CreateUndefined();
            result.SetArray();
            for (var i = 0; i < len; i++)
            {
                var pair = ScriptVar.CreateUndefined();
                pair.SetArray();
                pair.SetArrayIndex(0, ScriptVar.FromInt(i));
                pair.SetArrayIndex(1, arr.GetArrayIndex(i).DeepCopy());
                result.SetArrayIndex(i, pair);
            }
            var.ReturnVar = result;
        }

        [ScriptMethod("keys")]
        public static void ArrayKeysImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var len = arr.GetArrayLength();
            var result = ScriptVar.CreateUndefined();
            result.SetArray();
            for (var i = 0; i < len; i++)
                result.SetArrayIndex(i, ScriptVar.FromInt(i));
            var.ReturnVar = result;
        }

        [ScriptMethod("values")]
        public static void ArrayValuesImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var len = arr.GetArrayLength();
            var result = ScriptVar.CreateUndefined();
            result.SetArray();
            for (var i = 0; i < len; i++)
                result.SetArrayIndex(i, arr.GetArrayIndex(i).DeepCopy());
            var.ReturnVar = result;
        }

        // ── Static methods ────────────────────────────────────────────────────

        [ScriptMethod("isArray", "val")]
        public static void ArrayIsArrayImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = var.GetParameter("val").IsArray ? 1 : 0;
        }

        [ScriptMethod("from", "iterable", "mapFn")]
        public static void ArrayFromImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var iterable = var.GetParameter("iterable");
            var mapFn = var.GetParameter("mapFn");
            var result = ScriptVar.CreateUndefined();
            result.SetArray();

            // Source length and element access. Real arrays use their indices; strings
            // use their characters; any other object is treated as array-like via its
            // `length` property (missing indices read as undefined) — so
            // Array.from({ length: n }, (_, i) => ...) works like JS.
            int len;
            System.Func<int, ScriptVar> getElem;
            if (iterable.IsArray)
            {
                len = iterable.GetArrayLength();
                getElem = i => iterable.GetArrayIndex(i);
            }
            else if (iterable.IsString)
            {
                var str = iterable.String ?? "";
                len = str.Length;
                getElem = i => ScriptVar.FromString(str[i].ToString());
            }
            else
            {
                var lenLink = iterable.FindChild("length");
                len = lenLink != null ? lenLink.Var.Int : 0;
                if (len < 0) len = 0;
                getElem = i => iterable.FindChild(i.ToString())?.Var ?? ScriptVar.CreateUndefined();
            }

            for (var i = 0; i < len; i++)
            {
                var elem = getElem(i);
                var mapped = mapFn.IsFunction ? engine.CallFunction(mapFn, null, elem, ScriptVar.FromInt(i)) : elem.DeepCopy();
                result.SetArrayIndex(i, mapped);
            }
            var.ReturnVar = result;
        }

        [ScriptMethod("of", "val")]
        public static void ArrayOfImpl(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val");
            var result = ScriptVar.CreateUndefined();
            result.SetArray();
            if (!val.IsUndefined)
                result.SetArrayIndex(0, val.DeepCopy());
            var.ReturnVar = result;
        }

        // ── ES2023 copy-on-write methods ──────────────────────────────────────

        [ScriptMethod("reduceRight", "callback", "initial")]
        public static void ArrayReduceRightImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var arr = var.GetParameter("this");
            var callback = var.GetParameter("callback");
            var len = arr.GetArrayLength();

            var accumulator = var.GetParameter("initial");
            var start = len - 1;

            if (accumulator.IsUndefined && len > 0)
            {
                accumulator = arr.GetArrayIndex(len - 1).DeepCopy();
                start = len - 2;
            }

            for (var x = start; x >= 0; x--)
                accumulator = engine.CallFunction(callback, null, accumulator, arr.GetArrayIndex(x), ScriptVar.FromInt(x), arr);

            var.ReturnVar = accumulator;
        }

        [ScriptMethod("toSorted", "compare")]
        public static void ArrayToSortedImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var arr = var.GetParameter("this");
            var compare = var.GetParameter("compare");
            var len = arr.GetArrayLength();

            // toSorted returns a new array sharing the same element references (a
            // shallow copy), so snapshot references rather than deep-copying.
            var values = new List<ScriptVar>(len);
            for (var x = 0; x < len; x++)
                values.Add(arr.GetArrayIndex(x));

            if (compare.IsFunction)
                values.Sort((a, b) => engine.CallFunction(compare, null, a, b).Int);
            else
                values.Sort((a, b) => string.CompareOrdinal(a.String, b.String));

            var result = ScriptVar.CreateUndefined();
            result.SetArray();
            for (var x = 0; x < len; x++)
                result.SetArrayIndex(x, values[x]);
            var.ReturnVar = result;
        }

        [ScriptMethod("toReversed")]
        public static void ArrayToReversedImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var len = arr.GetArrayLength();
            var result = ScriptVar.CreateUndefined();
            result.SetArray();
            for (var x = 0; x < len; x++)
                result.SetArrayIndex(x, arr.GetArrayIndex(len - 1 - x).DeepCopy());
            var.ReturnVar = result;
        }

        [ScriptMethod("toSpliced", "start", "deleteCount", "item")]
        public static void ArrayToSplicedImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var len = arr.GetArrayLength();
            var startVar = var.GetParameter("start");
            var deleteCountVar = var.GetParameter("deleteCount");
            var item = var.GetParameter("item");

            var start = ProviderHelpers.NormalizeIndex(startVar.IsUndefined ? 0 : startVar.Int, len);

            var deleteCount = deleteCountVar.IsUndefined ? len - start : Math.Min(Math.Max(deleteCountVar.Int, 0), len - start);

            var items = new List<ScriptVar>();
            for (var i = 0; i < start; i++)
                items.Add(arr.GetArrayIndex(i).DeepCopy());
            if (!item.IsUndefined)
                items.Add(item.DeepCopy());
            for (var i = start + deleteCount; i < len; i++)
                items.Add(arr.GetArrayIndex(i).DeepCopy());

            var result = ScriptVar.CreateUndefined();
            result.SetArray();
            for (var i = 0; i < items.Count; i++)
                result.SetArrayIndex(i, items[i]);
            var.ReturnVar = result;
        }

        [ScriptMethod("with", "index", "val")]
        public static void ArrayWithImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("this");
            var len = arr.GetArrayLength();
            var index = var.GetParameter("index").Int;
            var val = var.GetParameter("val");

            if (index < 0) index = len + index;

            var result = ScriptVar.CreateUndefined();
            result.SetArray();
            for (var i = 0; i < len; i++)
                result.SetArrayIndex(i, i == index ? val.DeepCopy() : arr.GetArrayIndex(i).DeepCopy());
            var.ReturnVar = result;
        }
    }
}
