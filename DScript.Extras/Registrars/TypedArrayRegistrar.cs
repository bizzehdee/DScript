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

using DScript.Extras.FunctionProviders;

namespace DScript.Extras.Registrars
{
    internal static class TypedArrayRegistrar
    {
        internal static void Register(ScriptEngine engine)
        {
            RegisterArrayBuffer(engine);
            RegisterTypedArray(engine, "Int8Array",         TypedArrayKind.Int8);
            RegisterTypedArray(engine, "Uint8Array",        TypedArrayKind.Uint8);
            RegisterTypedArray(engine, "Uint8ClampedArray", TypedArrayKind.Uint8Clamped);
            RegisterTypedArray(engine, "Int16Array",        TypedArrayKind.Int16);
            RegisterTypedArray(engine, "Uint16Array",       TypedArrayKind.Uint16);
            RegisterTypedArray(engine, "Int32Array",        TypedArrayKind.Int32);
            RegisterTypedArray(engine, "Uint32Array",       TypedArrayKind.Uint32);
            RegisterTypedArray(engine, "Float32Array",      TypedArrayKind.Float32);
            RegisterTypedArray(engine, "Float64Array",      TypedArrayKind.Float64);
            RegisterTypedArray(engine, "BigInt64Array",     TypedArrayKind.BigInt64);
            RegisterTypedArray(engine, "BigUint64Array",    TypedArrayKind.BigUint64);
            RegisterDataView(engine);
        }

        // ── ArrayBuffer ───────────────────────────────────────────────────────

        private static void RegisterArrayBuffer(ScriptEngine engine)
        {
            var ctor = ScriptVar.CreateNativeFunction();
            ctor.AddChild("byteLength", ScriptVar.CreateUndefined());
            ctor.SetCallback((scope, _) =>
            {
                var lenVar = scope.FindChild("byteLength")?.Var;
                var len = lenVar != null && !lenVar.IsUndefined ? System.Math.Max(0, lenVar.Int) : 0;
                var abObj = new ArrayBufferObject(len);
                var thisVar = scope.FindChild("this")?.Var;
                if (thisVar == null) return;
                thisVar.SetData(abObj);
                thisVar.AddChild("byteLength", ScriptVar.FromInt(len));
            }, null);
            engine.Root.AddChild("ArrayBuffer", ctor);
        }

        // ── Typed arrays ──────────────────────────────────────────────────────

        private static void RegisterTypedArray(ScriptEngine engine, string name, TypedArrayKind kind)
        {
            var bpe = TypedArrayObject.GetBytesPerElement(kind);

            var ctor = ScriptVar.CreateNativeFunction();
            // Three optional params cover all constructor forms.
            ctor.AddChild("src", ScriptVar.CreateUndefined());
            ctor.AddChild("byteOffset", ScriptVar.CreateUndefined());
            ctor.AddChild("length", ScriptVar.CreateUndefined());
            ctor.SetCallback((scope, _) =>
            {
                var srcVar    = scope.FindChild("src")?.Var        ?? ScriptVar.CreateUndefined();
                var offsetVar = scope.FindChild("byteOffset")?.Var ?? ScriptVar.CreateUndefined();
                var lenVar    = scope.FindChild("length")?.Var     ?? ScriptVar.CreateUndefined();
                var thisVar   = scope.FindChild("this")?.Var;
                if (thisVar == null) return;

                ArrayBufferObject abObj;
                int byteOffset;
                int elementCount;

                if (srcVar.IsUndefined || (srcVar.IsInt && !srcVar.IsObject))
                {
                    // new TypedArray(length)
                    elementCount = srcVar.IsUndefined ? 0 : System.Math.Max(0, srcVar.Int);
                    abObj = new ArrayBufferObject(elementCount * bpe);
                    byteOffset = 0;
                }
                else if (srcVar.GetData() is ArrayBufferObject srcAbObj)
                {
                    // new TypedArray(arrayBuffer [, byteOffset [, length]])
                    abObj = srcAbObj;
                    byteOffset = offsetVar.IsUndefined ? 0 : System.Math.Max(0, offsetVar.Int);
                    if (!lenVar.IsUndefined)
                        elementCount = System.Math.Max(0, lenVar.Int);
                    else
                        elementCount = (abObj.ByteLength - byteOffset) / bpe;
                }
                else if (srcVar.GetData() is TypedArrayObject srcTa)
                {
                    // new TypedArray(otherTypedArray) — copy elements
                    elementCount = srcTa.Length;
                    abObj = new ArrayBufferObject(elementCount * bpe);
                    byteOffset = 0;
                    var dst = new TypedArrayObject(kind, abObj, 0, elementCount);
                    for (var i = 0; i < elementCount; i++)
                        dst.SetElement(i, srcTa.GetElement(i));
                }
                else if (srcVar.IsArray)
                {
                    // new TypedArray([1, 2, 3]) — array-like
                    elementCount = srcVar.GetArrayLength();
                    abObj = new ArrayBufferObject(elementCount * bpe);
                    byteOffset = 0;
                    var tmp = new TypedArrayObject(kind, abObj, 0, elementCount);
                    for (var i = 0; i < elementCount; i++)
                        tmp.SetElement(i, srcVar.GetArrayIndex(i));
                }
                else
                {
                    // Fallback: treat as length 0
                    elementCount = 0;
                    abObj = new ArrayBufferObject(0);
                    byteOffset = 0;
                }

                var taObj = new TypedArrayObject(kind, abObj, byteOffset, elementCount);
                thisVar.SetData(taObj);
                thisVar.AddChild("length",          ScriptVar.FromInt(elementCount));
                thisVar.AddChild("byteLength",       ScriptVar.FromInt(elementCount * bpe));
                thisVar.AddChild("byteOffset",       ScriptVar.FromInt(byteOffset));
                thisVar.AddChild("BYTES_PER_ELEMENT", ScriptVar.FromInt(bpe));

                // Expose the backing ArrayBuffer as .buffer so DataView / other views can share it.
                var bufVar = ScriptVar.CreateObject();
                bufVar.SetData(abObj);
                bufVar.AddChild("byteLength", ScriptVar.FromInt(abObj.ByteLength));
                thisVar.AddChild("buffer", bufVar);
            }, null);

            // Static BYTES_PER_ELEMENT on the constructor itself (TypedArray.BYTES_PER_ELEMENT).
            ctor.AddChild("BYTES_PER_ELEMENT", ScriptVar.FromInt(bpe));

            engine.Root.AddChild(name, ctor);
        }

        // ── DataView ──────────────────────────────────────────────────────────

        private static void RegisterDataView(ScriptEngine engine)
        {
            var ctor = ScriptVar.CreateNativeFunction();
            ctor.AddChild("buffer", ScriptVar.CreateUndefined());
            ctor.AddChild("byteOffset", ScriptVar.CreateUndefined());
            ctor.AddChild("byteLength", ScriptVar.CreateUndefined());
            ctor.SetCallback((scope, _) =>
            {
                var bufVar    = scope.FindChild("buffer")?.Var     ?? ScriptVar.CreateUndefined();
                var offsetVar = scope.FindChild("byteOffset")?.Var ?? ScriptVar.CreateUndefined();
                var lenVar    = scope.FindChild("byteLength")?.Var ?? ScriptVar.CreateUndefined();
                var thisVar   = scope.FindChild("this")?.Var;
                if (thisVar == null) return;

                ArrayBufferObject abObj;
                if (bufVar.GetData() is ArrayBufferObject ab)
                    abObj = ab;
                else
                {
                    // Wrap a bare buffer ScriptVar as a zero-byte buffer fallback.
                    abObj = new ArrayBufferObject(0);
                }

                var byteOffset = offsetVar.IsUndefined ? 0 : System.Math.Max(0, offsetVar.Int);
                var byteLength = lenVar.IsUndefined
                    ? System.Math.Max(0, abObj.ByteLength - byteOffset)
                    : System.Math.Max(0, lenVar.Int);

                var dvObj = new DataViewObject(abObj, byteOffset, byteLength);
                thisVar.SetData(dvObj);
                thisVar.AddChild("byteOffset", ScriptVar.FromInt(byteOffset));
                thisVar.AddChild("byteLength", ScriptVar.FromInt(byteLength));
                // Expose the same buffer ScriptVar so dv.buffer === ta.buffer can be detected.
                thisVar.AddChild("buffer", bufVar);
            }, null);
            engine.Root.AddChild("DataView", ctor);
        }
    }
}
