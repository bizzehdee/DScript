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
using System.Text;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("Buffer")]
    public static class BufferFunctionProvider
    {
        // Set by BufferRegistrar.Register() so MakeBuffer can link instances to the ctor.
        internal static ScriptVar CtorVar { get; set; }

        private static BufferObject GetBuffer(ScriptVar thisVar)
            => thisVar.GetData() as BufferObject ?? new BufferObject(0);

        [ScriptMethod("toString", "enc")]
        public static void BufferToStringImpl(ScriptVar var, object userData)
        {
            var buf = GetBuffer(var.GetParameter("this"));
            var encVar = var.GetParameter("enc");
            var encName = encVar.IsUndefined ? "utf8" : encVar.String;
            if (encName == "hex")
            {
                var.ReturnVar.String = Convert.ToHexString(buf.Data).ToLowerInvariant();
                return;
            }
            if (encName == "base64")
            {
                var.ReturnVar.String = Convert.ToBase64String(buf.Data);
                return;
            }
            var enc = encName switch
            {
                "ascii" => Encoding.ASCII,
                "latin1" or "binary" => Encoding.Latin1,
                _ => Encoding.UTF8
            };
            var.ReturnVar.String = enc.GetString(buf.Data);
        }

        [ScriptMethod("slice", "start", "end")]
        public static void BufferSliceImpl(ScriptVar var, object userData)
        {
            var buf = GetBuffer(var.GetParameter("this"));
            var start = var.GetParameter("start");
            var end = var.GetParameter("end");
            var s = start.IsUndefined ? 0 : start.Int;
            var e = end.IsUndefined ? buf.Data.Length : end.Int;
            if (s < 0) s = Math.Max(0, buf.Data.Length + s);
            if (e < 0) e = Math.Max(0, buf.Data.Length + e);
            s = Math.Clamp(s, 0, buf.Data.Length);
            e = Math.Clamp(e, s, buf.Data.Length);
            var slice = new byte[e - s];
            Array.Copy(buf.Data, s, slice, 0, e - s);
            var.ReturnVar = BufferRegistrar.MakeBuffer(slice);
        }

        [ScriptMethod("copy", "target", "targetStart")]
        public static void BufferCopyImpl(ScriptVar var, object userData)
        {
            var src = GetBuffer(var.GetParameter("this"));
            var target = GetBuffer(var.GetParameter("target"));
            var tsVar = var.GetParameter("targetStart");
            var targetStart = tsVar.IsUndefined ? 0 : tsVar.Int;
            var toCopy = Math.Min(src.Data.Length, target.Data.Length - targetStart);
            if (toCopy > 0)
                Array.Copy(src.Data, 0, target.Data, targetStart, toCopy);
            var.ReturnVar.Int = toCopy;
        }

        [ScriptMethod("equals", "other")]
        public static void BufferEqualsImpl(ScriptVar var, object userData)
        {
            var a = GetBuffer(var.GetParameter("this"));
            var b = GetBuffer(var.GetParameter("other"));
            if (a.Data.Length != b.Data.Length) { var.ReturnVar.Bool = false; return; }
            for (var i = 0; i < a.Data.Length; i++)
                if (a.Data[i] != b.Data[i]) { var.ReturnVar.Bool = false; return; }
            var.ReturnVar.Bool = true;
        }

        [ScriptMethod("readUInt8", "offset")]
        public static void BufferReadUInt8Impl(ScriptVar var, object userData)
        {
            var buf = GetBuffer(var.GetParameter("this"));
            var off = var.GetParameter("offset").Int;
            var.ReturnVar.Int = buf.Data[off];
        }

        [ScriptMethod("writeUInt8", "val", "offset")]
        public static void BufferWriteUInt8Impl(ScriptVar var, object userData)
        {
            var buf = GetBuffer(var.GetParameter("this"));
            var val = (byte)var.GetParameter("val").Int;
            var off = var.GetParameter("offset").Int;
            buf.Data[off] = val;
            var.ReturnVar.Int = off + 1;
        }

        [ScriptMethod("readUInt16LE", "offset")]
        public static void BufferReadUInt16LEImpl(ScriptVar var, object userData)
        {
            var buf = GetBuffer(var.GetParameter("this"));
            var off = var.GetParameter("offset").Int;
            var.ReturnVar.Int = buf.Data[off] | (buf.Data[off + 1] << 8);
        }

        [ScriptMethod("readUInt32LE", "offset")]
        public static void BufferReadUInt32LEImpl(ScriptVar var, object userData)
        {
            var buf = GetBuffer(var.GetParameter("this"));
            var off = var.GetParameter("offset").Int;
            var.ReturnVar.Int = (int)((uint)(buf.Data[off] | (buf.Data[off + 1] << 8) | (buf.Data[off + 2] << 16) | (buf.Data[off + 3] << 24)));
        }

        [ScriptMethod("readInt32LE", "offset")]
        public static void BufferReadInt32LEImpl(ScriptVar var, object userData)
        {
            var buf = GetBuffer(var.GetParameter("this"));
            var off = var.GetParameter("offset").Int;
            var.ReturnVar.Int = buf.Data[off] | (buf.Data[off + 1] << 8) | (buf.Data[off + 2] << 16) | (buf.Data[off + 3] << 24);
        }
    }
}
