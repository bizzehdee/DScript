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

namespace DScript.Extras.FunctionProviders
{
    /// <summary>
    /// Shared methods for all TypedArray subtypes (Int8Array, Float64Array, …).
    /// Registered on a synthetic "TypedArray" class; DScript's FindInParentClasses
    /// resolves these for any ScriptVar whose native data is a TypedArrayObject.
    /// </summary>
    [ScriptClass("TypedArray")]
    public static class TypedArrayFunctionProvider
    {
        private static TypedArrayObject GetTA(ScriptVar thisVar)
            => thisVar.GetData() as TypedArrayObject
               ?? throw new JITException(MakeTypeError("not a TypedArray"));

        // ── element iteration helpers ─────────────────────────────────────────

        [ScriptMethod("forEach", "callback")]
        public static void TAForEachImpl(ScriptVar var, object userData)
        {
            var engine   = (ScriptEngine)userData;
            var ta       = GetTA(var.GetParameter("this"));
            var callback = var.GetParameter("callback");
            for (var i = 0; i < ta.Length; i++)
                engine.CallFunction(callback, null, ta.GetElement(i), ScriptVar.FromInt(i));
        }

        [ScriptMethod("map", "callback")]
        public static void TAMapImpl(ScriptVar var, object userData)
        {
            var engine   = (ScriptEngine)userData;
            var thisVar  = var.GetParameter("this");
            var ta       = GetTA(thisVar);
            var callback = var.GetParameter("callback");
            var result   = MakeTypedArray(engine, ta.Kind, ta.Length);
            var resTa    = result.GetData() as TypedArrayObject;
            for (var i = 0; i < ta.Length; i++)
            {
                var mapped = engine.CallFunction(callback, null, ta.GetElement(i), ScriptVar.FromInt(i));
                resTa?.SetElement(i, mapped ?? ScriptVar.CreateUndefined());
            }
            var.ReturnVar = result;
        }

        [ScriptMethod("filter", "callback")]
        public static void TAFilterImpl(ScriptVar var, object userData)
        {
            var engine   = (ScriptEngine)userData;
            var ta       = GetTA(var.GetParameter("this"));
            var callback = var.GetParameter("callback");
            var kept     = new System.Collections.Generic.List<ScriptVar>(ta.Length);
            for (var i = 0; i < ta.Length; i++)
            {
                var elem = ta.GetElement(i);
                var res  = engine.CallFunction(callback, null, elem, ScriptVar.FromInt(i));
                if (res?.Bool == true) kept.Add(elem);
            }
            var result = MakeTypedArray(engine, ta.Kind, kept.Count);
            var resTa  = result.GetData() as TypedArrayObject;
            for (var i = 0; i < kept.Count; i++)
                resTa?.SetElement(i, kept[i]);
            var.ReturnVar = result;
        }

        [ScriptMethod("find", "callback")]
        public static void TAFindImpl(ScriptVar var, object userData)
        {
            var engine   = (ScriptEngine)userData;
            var ta       = GetTA(var.GetParameter("this"));
            var callback = var.GetParameter("callback");
            for (var i = 0; i < ta.Length; i++)
            {
                var elem = ta.GetElement(i);
                var res  = engine.CallFunction(callback, null, elem, ScriptVar.FromInt(i));
                if (res?.Bool == true) { var.ReturnVar = elem; return; }
            }
            var.ReturnVar.SetUndefined();
        }

        [ScriptMethod("findIndex", "callback")]
        public static void TAFindIndexImpl(ScriptVar var, object userData)
        {
            var engine   = (ScriptEngine)userData;
            var ta       = GetTA(var.GetParameter("this"));
            var callback = var.GetParameter("callback");
            for (var i = 0; i < ta.Length; i++)
            {
                var elem = ta.GetElement(i);
                var res  = engine.CallFunction(callback, null, elem, ScriptVar.FromInt(i));
                if (res?.Bool == true) { var.ReturnVar.Int = i; return; }
            }
            var.ReturnVar.Int = -1;
        }

        [ScriptMethod("indexOf", "searchElement", "fromIndex")]
        public static void TAIndexOfImpl(ScriptVar var, object userData)
        {
            var ta     = GetTA(var.GetParameter("this"));
            var search = var.GetParameter("searchElement");
            var from   = var.GetParameter("fromIndex");
            var start  = from.IsUndefined ? 0 : from.Int;
            if (start < 0) start = Math.Max(0, ta.Length + start);
            for (var i = start; i < ta.Length; i++)
            {
                if (ta.GetElement(i).Equal(search)) { var.ReturnVar.Int = i; return; }
            }
            var.ReturnVar.Int = -1;
        }

        [ScriptMethod("includes", "searchElement", "fromIndex")]
        public static void TAIncludesImpl(ScriptVar var, object userData)
        {
            var ta     = GetTA(var.GetParameter("this"));
            var search = var.GetParameter("searchElement");
            var from   = var.GetParameter("fromIndex");
            var start  = from.IsUndefined ? 0 : from.Int;
            if (start < 0) start = Math.Max(0, ta.Length + start);
            for (var i = start; i < ta.Length; i++)
            {
                if (ta.GetElement(i).Equal(search)) { var.ReturnVar.Int = 1; return; }
            }
            var.ReturnVar.Int = 0;
        }

        [ScriptMethod("every", "callback")]
        public static void TAEveryImpl(ScriptVar var, object userData)
        {
            var engine   = (ScriptEngine)userData;
            var ta       = GetTA(var.GetParameter("this"));
            var callback = var.GetParameter("callback");
            for (var i = 0; i < ta.Length; i++)
            {
                var res = engine.CallFunction(callback, null, ta.GetElement(i), ScriptVar.FromInt(i));
                if (res?.Bool != true) { var.ReturnVar.Int = 0; return; }
            }
            var.ReturnVar.Int = 1;
        }

        [ScriptMethod("some", "callback")]
        public static void TASomeImpl(ScriptVar var, object userData)
        {
            var engine   = (ScriptEngine)userData;
            var ta       = GetTA(var.GetParameter("this"));
            var callback = var.GetParameter("callback");
            for (var i = 0; i < ta.Length; i++)
            {
                var res = engine.CallFunction(callback, null, ta.GetElement(i), ScriptVar.FromInt(i));
                if (res?.Bool == true) { var.ReturnVar.Int = 1; return; }
            }
            var.ReturnVar.Int = 0;
        }

        [ScriptMethod("reduce", "callback", "initialValue")]
        public static void TAReduceImpl(ScriptVar var, object userData)
        {
            var engine   = (ScriptEngine)userData;
            var ta       = GetTA(var.GetParameter("this"));
            var callback = var.GetParameter("callback");
            var initVar  = var.GetParameter("initialValue");
            if (ta.Length == 0 && initVar.IsUndefined)
                throw new JITException(MakeTypeError("reduce of empty TypedArray with no initial value"));
            ScriptVar acc;
            var start = 0;
            if (initVar.IsUndefined) { acc = ta.GetElement(0); start = 1; }
            else acc = initVar;
            for (var i = start; i < ta.Length; i++)
                acc = engine.CallFunction(callback, null, acc, ta.GetElement(i), ScriptVar.FromInt(i)) ?? ScriptVar.CreateUndefined();
            var.ReturnVar = acc;
        }

        [ScriptMethod("fill", "value", "start", "end")]
        public static void TAFillImpl(ScriptVar var, object userData)
        {
            var thisVar  = var.GetParameter("this");
            var ta       = GetTA(thisVar);
            var fillVal  = var.GetParameter("value");
            var startVar = var.GetParameter("start");
            var endVar   = var.GetParameter("end");
            var start    = startVar.IsUndefined ? 0 : startVar.Int;
            var end      = endVar.IsUndefined   ? ta.Length : endVar.Int;
            if (start < 0) start = Math.Max(0, ta.Length + start);
            if (end   < 0) end   = Math.Max(0, ta.Length + end);
            start = Math.Min(Math.Max(start, 0), ta.Length);
            end   = Math.Min(Math.Max(end,   0), ta.Length);
            for (var i = start; i < end; i++)
                ta.SetElement(i, fillVal);
            var.ReturnVar = thisVar;
        }

        [ScriptMethod("set", "array", "offset")]
        public static void TASetImpl(ScriptVar var, object userData)
        {
            var ta       = GetTA(var.GetParameter("this"));
            var src      = var.GetParameter("array");
            var offsetV  = var.GetParameter("offset");
            var offset   = offsetV.IsUndefined ? 0 : offsetV.Int;

            if (src.GetData() is TypedArrayObject srcTa)
            {
                for (var i = 0; i < srcTa.Length; i++)
                    if (offset + i < ta.Length)
                        ta.SetElement(offset + i, srcTa.GetElement(i));
            }
            else if (src.IsArray)
            {
                var len = src.GetArrayLength();
                for (var i = 0; i < len; i++)
                    if (offset + i < ta.Length)
                        ta.SetElement(offset + i, src.GetArrayIndex(i));
            }
        }

        [ScriptMethod("subarray", "begin", "end")]
        public static void TASubarrayImpl(ScriptVar var, object userData)
        {
            var engine  = (ScriptEngine)userData;
            var thisVar = var.GetParameter("this");
            var ta      = GetTA(thisVar);
            var beginV  = var.GetParameter("begin");
            var endV    = var.GetParameter("end");
            var begin   = beginV.IsUndefined ? 0 : beginV.Int;
            var end     = endV.IsUndefined   ? ta.Length : endV.Int;
            if (begin < 0) begin = Math.Max(0, ta.Length + begin);
            if (end   < 0) end   = Math.Max(0, ta.Length + end);
            begin = Math.Min(Math.Max(begin, 0), ta.Length);
            end   = Math.Min(Math.Max(end,   0), ta.Length);
            var newLen    = Math.Max(0, end - begin);
            var byteStart = ta.ByteOffset + begin * ta.BytesPerElement;

            var sub = MakeTypedArrayView(engine, ta.Kind, ta.Buffer, byteStart, newLen);
            var.ReturnVar = sub;
        }

        [ScriptMethod("slice", "begin", "end")]
        public static void TASliceImpl(ScriptVar var, object userData)
        {
            var engine  = (ScriptEngine)userData;
            var ta      = GetTA(var.GetParameter("this"));
            var beginV  = var.GetParameter("begin");
            var endV    = var.GetParameter("end");
            var begin   = beginV.IsUndefined ? 0 : beginV.Int;
            var end     = endV.IsUndefined   ? ta.Length : endV.Int;
            if (begin < 0) begin = Math.Max(0, ta.Length + begin);
            if (end   < 0) end   = Math.Max(0, ta.Length + end);
            begin = Math.Min(Math.Max(begin, 0), ta.Length);
            end   = Math.Min(Math.Max(end,   0), ta.Length);
            var newLen = Math.Max(0, end - begin);
            var result = MakeTypedArray(engine, ta.Kind, newLen);
            var resTa  = result.GetData() as TypedArrayObject;
            for (var i = 0; i < newLen; i++)
                resTa?.SetElement(i, ta.GetElement(begin + i));
            var.ReturnVar = result;
        }

        [ScriptMethod("join", "separator")]
        public static void TAJoinImpl(ScriptVar var, object userData)
        {
            var ta  = GetTA(var.GetParameter("this"));
            var sep = var.GetParameter("separator");
            var sepStr = sep.IsUndefined ? "," : sep.String;
            var sb  = new System.Text.StringBuilder();
            for (var i = 0; i < ta.Length; i++)
            {
                if (i > 0) sb.Append(sepStr);
                sb.Append(ta.GetElement(i).String);
            }
            var.ReturnVar = ScriptVar.FromString(sb.ToString());
        }

        [ScriptMethod("reverse")]
        public static void TAReverseImpl(ScriptVar var, object userData)
        {
            var thisVar = var.GetParameter("this");
            var ta      = GetTA(thisVar);
            var hi = ta.Length - 1;
            for (var lo = 0; lo < hi; lo++, hi--)
            {
                var tmp = ta.GetElement(lo);
                ta.SetElement(lo, ta.GetElement(hi));
                ta.SetElement(hi, tmp);
            }
            var.ReturnVar = thisVar;
        }

        [ScriptMethod("copyWithin", "target", "start", "end")]
        public static void TACopyWithinImpl(ScriptVar var, object userData)
        {
            var thisVar = var.GetParameter("this");
            var ta      = GetTA(thisVar);
            var targetI = var.GetParameter("target").Int;
            var startI  = var.GetParameter("start").Int;
            var endV    = var.GetParameter("end");
            var endI    = endV.IsUndefined ? ta.Length : endV.Int;
            if (targetI < 0) targetI = Math.Max(0, ta.Length + targetI);
            if (startI  < 0) startI  = Math.Max(0, ta.Length + startI);
            if (endI    < 0) endI    = Math.Max(0, ta.Length + endI);
            var count = Math.Min(endI - startI, ta.Length - targetI);
            for (var i = 0; i < count; i++)
                ta.SetElement(targetI + i, ta.GetElement(startI + i));
            var.ReturnVar = thisVar;
        }

        [ScriptMethod("keys")]
        public static void TAKeysImpl(ScriptVar var, object userData)
        {
            var ta = GetTA(var.GetParameter("this"));
            var arr = ScriptVar.CreateUndefined();
            arr.SetArray();
            for (var i = 0; i < ta.Length; i++)
                arr.SetArrayIndex(i, ScriptVar.FromInt(i));
            var.ReturnVar = arr;
        }

        [ScriptMethod("values")]
        public static void TAValuesImpl(ScriptVar var, object userData)
        {
            var ta = GetTA(var.GetParameter("this"));
            var arr = ScriptVar.CreateUndefined();
            arr.SetArray();
            for (var i = 0; i < ta.Length; i++)
                arr.SetArrayIndex(i, ta.GetElement(i));
            var.ReturnVar = arr;
        }

        [ScriptMethod("entries")]
        public static void TAEntriesImpl(ScriptVar var, object userData)
        {
            var ta = GetTA(var.GetParameter("this"));
            var arr = ScriptVar.CreateUndefined();
            arr.SetArray();
            for (var i = 0; i < ta.Length; i++)
            {
                var pair = ScriptVar.CreateUndefined();
                pair.SetArray();
                pair.SetArrayIndex(0, ScriptVar.FromInt(i));
                pair.SetArrayIndex(1, ta.GetElement(i));
                arr.SetArrayIndex(i, pair);
            }
            var.ReturnVar = arr;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        /// <summary>Create a new zero-filled TypedArray ScriptVar of the given kind and length.</summary>
        internal static ScriptVar MakeTypedArray(ScriptEngine engine, TypedArrayKind kind, int length)
        {
            var bpe = TypedArrayObject.GetBytesPerElement(kind);
            var abObj = new ArrayBufferObject(length * bpe);
            return MakeTypedArrayView(engine, kind, abObj, 0, length);
        }

        /// <summary>Create a TypedArray view into an existing buffer (used by subarray).</summary>
        internal static ScriptVar MakeTypedArrayView(ScriptEngine engine, TypedArrayKind kind,
            ArrayBufferObject buffer, int byteOffset, int length)
        {
            var bpe = TypedArrayObject.GetBytesPerElement(kind);
            var taObj = new TypedArrayObject(kind, buffer, byteOffset, length);
            var sv = ScriptVar.CreateObject();
            sv.SetData(taObj);
            sv.AddChild("length",           ScriptVar.FromInt(length));
            sv.AddChild("byteLength",        ScriptVar.FromInt(length * bpe));
            sv.AddChild("byteOffset",        ScriptVar.FromInt(byteOffset));
            sv.AddChild("BYTES_PER_ELEMENT", ScriptVar.FromInt(bpe));
            var bufVar = ScriptVar.CreateObject();
            bufVar.SetData(buffer);
            bufVar.AddChild("byteLength", ScriptVar.FromInt(buffer.ByteLength));
            sv.AddChild("buffer", bufVar);
            // Set __prototype__ to the specific typed array constructor so methods resolve.
            var ctorVar = engine.Root.FindChild(KindToCtorName(kind))?.Var;
            if (ctorVar != null) sv.AddChild(ScriptVar.PrototypeClassName, ctorVar);
            return sv;
        }

        private static string KindToCtorName(TypedArrayKind kind) => kind switch
        {
            TypedArrayKind.Int8         => "Int8Array",
            TypedArrayKind.Uint8        => "Uint8Array",
            TypedArrayKind.Uint8Clamped => "Uint8ClampedArray",
            TypedArrayKind.Int16        => "Int16Array",
            TypedArrayKind.Uint16       => "Uint16Array",
            TypedArrayKind.Int32        => "Int32Array",
            TypedArrayKind.Uint32       => "Uint32Array",
            TypedArrayKind.Float32      => "Float32Array",
            TypedArrayKind.Float64      => "Float64Array",
            TypedArrayKind.BigInt64     => "BigInt64Array",
            TypedArrayKind.BigUint64    => "BigUint64Array",
            _                           => "Uint8Array"
        };

        private static ScriptVar MakeTypeError(string message)
        {
            var err = ScriptVar.CreateObject();
            err.AddChild("name", ScriptVar.FromString("TypeError"));
            err.AddChild("message", ScriptVar.FromString(message));
            return err;
        }
    }
}
