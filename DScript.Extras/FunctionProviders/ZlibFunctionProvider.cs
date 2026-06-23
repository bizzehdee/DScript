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
using System.IO;
using System.IO.Compression;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("zlib")]
    public static class ZlibFunctionProvider
    {
        private static byte[] GetBytes(ScriptVar sv)
        {
            if (sv.GetData() is BufferObject buf)
                return buf.Data;
            // Fallback: treat as UTF-8 string
            return System.Text.Encoding.UTF8.GetBytes(sv.String);
        }

        [ScriptMethod("gzipSync", "input")]
        public static void GzipSyncImpl(ScriptVar var, object userData)
        {
            var input = var.GetParameter("input");
            var bytes = GetBytes(input);
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
                gz.Write(bytes, 0, bytes.Length);
            var.ReturnVar = BufferRegistrar.MakeBuffer(ms.ToArray());
        }

        [ScriptMethod("gunzipSync", "input")]
        public static void GunzipSyncImpl(ScriptVar var, object userData)
        {
            var input = var.GetParameter("input");
            var bytes = GetBytes(input);
            try
            {
                using var ms = new MemoryStream(bytes);
                using var gz = new GZipStream(ms, CompressionMode.Decompress);
                using var out_ = new MemoryStream();
                gz.CopyTo(out_);
                var.ReturnVar = BufferRegistrar.MakeBuffer(out_.ToArray());
            }
            catch (Exception ex) when (ex is not ScriptException) { throw new ScriptException(ex.Message); }
        }

        [ScriptMethod("deflateSync", "input")]
        public static void DeflateSyncImpl(ScriptVar var, object userData)
        {
            var input = var.GetParameter("input");
            var bytes = GetBytes(input);
            using var ms = new MemoryStream();
            using (var zs = new ZLibStream(ms, CompressionMode.Compress, leaveOpen: true))
                zs.Write(bytes, 0, bytes.Length);
            var.ReturnVar = BufferRegistrar.MakeBuffer(ms.ToArray());
        }

        [ScriptMethod("inflateSync", "input")]
        public static void InflateSyncImpl(ScriptVar var, object userData)
        {
            var input = var.GetParameter("input");
            var bytes = GetBytes(input);
            try
            {
                using var ms = new MemoryStream(bytes);
                using var zs = new ZLibStream(ms, CompressionMode.Decompress);
                using var out_ = new MemoryStream();
                zs.CopyTo(out_);
                var.ReturnVar = BufferRegistrar.MakeBuffer(out_.ToArray());
            }
            catch (Exception ex) when (ex is not ScriptException) { throw new ScriptException(ex.Message); }
        }
    }
}
