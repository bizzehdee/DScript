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
using DScript.Extras.FunctionProviders;

namespace DScript.Extras.Registrars
{
    internal static class BufferRegistrar
    {
        internal static void Register(ScriptEngine engine)
        {
            var bufferCtorVar = ScriptVar.CreateNativeFunction();
            bufferCtorVar.SetCallback((scope, _) =>
            {
                // new Buffer() — create empty 0-byte buffer
                var thisVar = scope.FindChild("this")?.Var;
                if (thisVar != null)
                {
                    var buf = new BufferObject(0);
                    thisVar.SetData(buf);
                    thisVar.AddChild("length", ScriptVar.FromInt(0));
                }
            }, null);

            // Buffer.from(src, enc?)
            var fromFn = ScriptVar.CreateNativeFunction();
            fromFn.AddChild("src", ScriptVar.CreateUndefined());
            fromFn.AddChild("enc", ScriptVar.CreateUndefined());
            fromFn.SetCallback((scope, _) =>
            {
                var src = scope.FindChild("src")?.Var ?? ScriptVar.CreateUndefined();
                var encVar = scope.FindChild("enc")?.Var;
                byte[] bytes;
                if (src.IsArray)
                {
                    var len = src.GetArrayLength();
                    bytes = new byte[len];
                    for (var i = 0; i < len; i++)
                        bytes[i] = (byte)src.GetArrayIndex(i).Int;
                }
                else
                {
                    var enc = GetEncoding(encVar == null || encVar.IsUndefined ? "utf8" : encVar.String);
                    bytes = enc.GetBytes(src.String);
                }
                scope.ReturnVar = MakeBuffer(bytes);
            }, null);

            // Buffer.alloc(size, fill?)
            var allocFn = ScriptVar.CreateNativeFunction();
            allocFn.AddChild("size", ScriptVar.CreateUndefined());
            allocFn.AddChild("fill", ScriptVar.CreateUndefined());
            allocFn.SetCallback((scope, _) =>
            {
                var size = scope.FindChild("size")?.Var?.Int ?? 0;
                var fillVar = scope.FindChild("fill")?.Var;
                var bytes = new byte[size];
                if (fillVar != null && !fillVar.IsUndefined)
                {
                    var fillByte = (byte)fillVar.Int;
                    for (var i = 0; i < size; i++) bytes[i] = fillByte;
                }
                scope.ReturnVar = MakeBuffer(bytes);
            }, null);

            // Buffer.allocUnsafe(size)
            var allocUnsafeFn = ScriptVar.CreateNativeFunction();
            allocUnsafeFn.AddChild("size", ScriptVar.CreateUndefined());
            allocUnsafeFn.SetCallback((scope, _) =>
            {
                var size = scope.FindChild("size")?.Var?.Int ?? 0;
                scope.ReturnVar = MakeBuffer(new byte[size]);
            }, null);

            // Buffer.isBuffer(val)
            var isBufferFn = ScriptVar.CreateNativeFunction();
            isBufferFn.AddChild("val", ScriptVar.CreateUndefined());
            isBufferFn.SetCallback((scope, _) =>
            {
                var val = scope.FindChild("val")?.Var;
                scope.ReturnVar = ScriptVar.FromBool(val?.GetData() is BufferObject);
            }, null);

            // Buffer.concat(list)
            var concatFn = ScriptVar.CreateNativeFunction();
            concatFn.AddChild("list", ScriptVar.CreateUndefined());
            concatFn.SetCallback((scope, _) =>
            {
                var list = scope.FindChild("list")?.Var ?? ScriptVar.CreateUndefined();
                var total = 0;
                var count = list.GetArrayLength();
                var arrays = new byte[count][];
                for (var i = 0; i < count; i++)
                {
                    var item = list.GetArrayIndex(i);
                    var buf = item.GetData() as BufferObject;
                    arrays[i] = buf?.Data ?? Array.Empty<byte>();
                    total += arrays[i].Length;
                }
                var result = new byte[total];
                var offset = 0;
                foreach (var arr in arrays)
                {
                    Buffer.BlockCopy(arr, 0, result, offset, arr.Length);
                    offset += arr.Length;
                }
                scope.ReturnVar = MakeBuffer(result);
            }, null);

            bufferCtorVar.AddChild("from", fromFn);
            bufferCtorVar.AddChild("alloc", allocFn);
            bufferCtorVar.AddChild("allocUnsafe", allocUnsafeFn);
            bufferCtorVar.AddChild("isBuffer", isBufferFn);
            bufferCtorVar.AddChild("concat", concatFn);

            engine.Root.AddChild("Buffer", bufferCtorVar);

            // Store the ctor so MakeBuffer can link instances to it for method dispatch.
            FunctionProviders.BufferFunctionProvider.CtorVar = bufferCtorVar;
        }

        internal static ScriptVar MakeBuffer(byte[] data)
        {
            var sv = ScriptVar.CreateObject();
            sv.SetData(new BufferObject(data));
            sv.AddChild("length", ScriptVar.FromInt(data.Length));
            var ctor = FunctionProviders.BufferFunctionProvider.CtorVar;
            if (ctor != null)
                sv.AddChild(ScriptVar.PrototypeClassName, ctor);
            return sv;
        }

        private static Encoding GetEncoding(string name) =>
            (name ?? "utf8").ToLowerInvariant() switch
            {
                "utf8" or "utf-8" => Encoding.UTF8,
                "ascii" => Encoding.ASCII,
                "latin1" or "binary" => Encoding.Latin1,
                "base64" => Encoding.UTF8,
                "hex" => Encoding.UTF8,
                _ => Encoding.UTF8
            };
    }
}
