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

using System.Text;

using DScript.Extras.Registrars;
namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("__textcodec__")]
    public static class TextEncoderFunctionProvider
    {
        // --- TextEncoder constructor ---

        [ScriptMethod("TextEncoder", AppearAtRoot = true)]
        public static void TextEncoderConstructorImpl(ScriptVar var, object userData)
        {
            var obj = new ScriptVar(ScriptVar.Flags.Object);
            obj.AddChild("encoding", new ScriptVar("utf-8"));

            // .encode(str) → Buffer
            var encodeFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            encodeFn.AddChild("str", new ScriptVar(ScriptVar.Flags.Undefined));
            encodeFn.SetCallback((scope, _) =>
            {
                var s = scope.FindChild("str")?.Var?.String ?? "";
                var bytes = Encoding.UTF8.GetBytes(s);
                scope.ReturnVar = BufferRegistrar.MakeBuffer(bytes);
            }, null);
            obj.AddChild("encode", encodeFn);

            var.ReturnVar = obj;
        }

        // --- TextDecoder constructor ---

        [ScriptMethod("TextDecoder", "encoding", AppearAtRoot = true)]
        public static void TextDecoderConstructorImpl(ScriptVar var, object userData)
        {
            var encVar = var.GetParameter("encoding");
            var encName = encVar.IsUndefined ? "utf-8" : encVar.String.ToLowerInvariant();

            Encoding enc = encName switch
            {
                "ascii" => Encoding.ASCII,
                "latin1" or "iso-8859-1" or "binary" => Encoding.Latin1,
                _ => Encoding.UTF8,
            };

            var obj = new ScriptVar(ScriptVar.Flags.Object);
            obj.AddChild("encoding", new ScriptVar(encName));

            // .decode(buffer) → string
            var decodeFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            decodeFn.AddChild("buffer", new ScriptVar(ScriptVar.Flags.Undefined));
            var capturedEnc = enc;
            decodeFn.SetCallback((scope, _) =>
            {
                var bufVar = scope.FindChild("buffer")?.Var;
                byte[] bytes;
                if (bufVar?.GetData() is BufferObject bo)
                    bytes = bo.Data;
                else if (bufVar != null && !bufVar.IsUndefined && bufVar.IsArray)
                {
                    var len = bufVar.GetArrayLength();
                    bytes = new byte[len];
                    for (var i = 0; i < len; i++)
                        bytes[i] = (byte)bufVar.GetArrayIndex(i).Int;
                }
                else
                    bytes = System.Array.Empty<byte>();

                scope.ReturnVar = new ScriptVar(capturedEnc.GetString(bytes));
            }, null);
            obj.AddChild("decode", decodeFn);

            var.ReturnVar = obj;
        }
    }
}
