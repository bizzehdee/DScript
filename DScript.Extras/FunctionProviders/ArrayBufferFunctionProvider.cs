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

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("ArrayBuffer")]
    public static class ArrayBufferFunctionProvider
    {
        private static ArrayBufferObject GetAB(ScriptVar thisVar)
            => thisVar.GetData() as ArrayBufferObject ?? new ArrayBufferObject(0);

        /// <summary>ArrayBuffer.isView(arg) — returns true if arg is a TypedArray or DataView.</summary>
        [ScriptMethod("isView", "arg", AppearAtRoot = false)]
        public static void ArrayBufferIsViewImpl(ScriptVar var, object userData)
        {
            var arg = var.GetParameter("arg");
            var.ReturnVar.Int = (arg.GetData() is TypedArrayObject || arg.GetData() is DataViewObject) ? 1 : 0;
        }

        [ScriptMethod("slice", "start", "end")]
        public static void ArrayBufferSliceImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var ab = GetAB(var.GetParameter("this"));
            var startVar = var.GetParameter("start");
            var endVar   = var.GetParameter("end");
            var start = startVar.IsUndefined ? 0 : startVar.Int;
            var end   = endVar.IsUndefined   ? ab.ByteLength : endVar.Int;
            var sliced = ab.Slice(start, end);

            // Construct a new ArrayBuffer ScriptVar wrapping the sliced bytes.
            var abCtor = engine.Root.FindChild("ArrayBuffer")?.Var;
            if (abCtor != null)
            {
                var newAb = ScriptVar.CreateObject();
                newAb.SetData(sliced);
                newAb.AddChild("byteLength", ScriptVar.FromInt(sliced.ByteLength));
                var.ReturnVar = newAb;
            }
            else
            {
                var.ReturnVar.SetUndefined();
            }
        }
    }
}
