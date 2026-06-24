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

using System.Collections.Generic;
using System.Text;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("stream")]
    public static class StreamFunctionProvider
    {
        private const string DataKey   = "__streamData__";
        private const string EndKey    = "__streamEnd__";
        private const string ClosedKey = "__streamClosed__";

        // --- Readable ---

        [ScriptMethod("Readable")]
        public static void ReadableImpl(ScriptVar var, object userData)
        {
            var.ReturnVar = MakeReadable(userData as ScriptEngine);
        }

        internal static ScriptVar MakeReadable(ScriptEngine engine)
        {
            var r = ScriptVar.CreateObject();
            var chunks = new List<string>();
            var capturedEngine = engine;

            var dataHandlers = ScriptVar.CreateUndefined(); dataHandlers.SetArray();
            var endHandlers  = ScriptVar.CreateUndefined(); endHandlers.SetArray();
            r.AddChild(DataKey, dataHandlers);
            r.AddChild(EndKey, endHandlers);
            r.AddChild(ClosedKey, ScriptVar.FromInt(0));

            var capturedR = r;

            // .on(event, fn) — 'data' or 'end'
            var onFn = ScriptVar.CreateNativeFunction();
            onFn.AddChild("event", ScriptVar.CreateUndefined());
            onFn.AddChild("fn", ScriptVar.CreateUndefined());
            onFn.SetCallback((scope, _) =>
            {
                var evName = scope.FindChild("event")?.Var?.String ?? "";
                var fn = scope.FindChild("fn")?.Var;
                if (fn == null || !fn.IsFunction) return;

                var key = evName switch { "data" => DataKey, "end" => EndKey, _ => null };
                if (key == null) return;
                var arr = capturedR.FindChild(key)?.Var;
                arr?.SetArrayIndex(arr.GetArrayLength(), fn);
            }, null);
            r.AddChild("on", onFn);

            // .push(chunk) — called by producer code to feed data
            var capturedChunks = chunks;
            var pushFn = ScriptVar.CreateNativeFunction();
            pushFn.AddChild("chunk", ScriptVar.CreateUndefined());
            pushFn.SetCallback((scope, _) =>
            {
                var chunkVar = scope.FindChild("chunk")?.Var;
                if (chunkVar == null || chunkVar.IsNull || chunkVar.IsUndefined)
                {
                    // null signals end of stream — fire 'end' handlers
                    if (capturedEngine != null)
                    {
                        var arr = capturedR.FindChild(EndKey)?.Var;
                        if (arr != null)
                        {
                            var len = arr.GetArrayLength();
                            for (var i = 0; i < len; i++)
                            {
                                var fn = arr.GetArrayIndex(i);
                                if (fn.IsFunction) capturedEngine.CallFunction(fn, null);
                            }
                        }
                    }
                    return;
                }

                var text = chunkVar.String;
                capturedChunks.Add(text);
                if (capturedEngine != null)
                {
                    var arr = capturedR.FindChild(DataKey)?.Var;
                    if (arr != null)
                    {
                        var len = arr.GetArrayLength();
                        for (var i = 0; i < len; i++)
                        {
                            var fn = arr.GetArrayIndex(i);
                            if (fn.IsFunction) capturedEngine.CallFunction(fn, null, ScriptVar.FromString(text));
                        }
                    }
                }
            }, null);
            r.AddChild("push", pushFn);

            // .read() — returns buffered chunks as a single string
            var readFn = ScriptVar.CreateNativeFunction();
            readFn.SetCallback((scope, _) =>
            {
                var sb = new StringBuilder();
                foreach (var c in capturedChunks) sb.Append(c);
                capturedChunks.Clear();
                scope.ReturnVar = ScriptVar.FromString(sb.ToString());
            }, null);
            r.AddChild("read", readFn);

            // .pipe(writable) — pushes all buffered data into writable
            var pipeFn = ScriptVar.CreateNativeFunction();
            pipeFn.AddChild("dest", ScriptVar.CreateUndefined());
            pipeFn.SetCallback((scope, _) =>
            {
                var dest = scope.FindChild("dest")?.Var;
                if (dest == null || dest.IsUndefined || capturedEngine == null) return;
                foreach (var c in capturedChunks)
                {
                    var writeFn = dest.FindChild("write")?.Var;
                    if (writeFn != null && writeFn.IsFunction)
                        capturedEngine.CallFunction(writeFn, null, ScriptVar.FromString(c));
                }
                capturedChunks.Clear();
                // Signal end
                var destEndFn = dest.FindChild("end")?.Var;
                if (destEndFn != null && destEndFn.IsFunction)
                    capturedEngine.CallFunction(destEndFn, null);
                scope.ReturnVar = dest;
            }, null);
            r.AddChild("pipe", pipeFn);

            return r;
        }

        // --- Writable ---

        [ScriptMethod("Writable")]
        public static void WritableImpl(ScriptVar var, object userData)
        {
            var.ReturnVar = MakeWritable(userData as ScriptEngine);
        }

        internal static ScriptVar MakeWritable(ScriptEngine engine)
        {
            var w = ScriptVar.CreateObject();
            var buf = new StringBuilder();
            var capturedEngine = engine;

            var finishHandlers = ScriptVar.CreateUndefined(); finishHandlers.SetArray();
            w.AddChild("__finishHandlers__", finishHandlers);

            var capturedW = w;
            var capturedBuf = buf;

            // .write(chunk)
            var writeFn = ScriptVar.CreateNativeFunction();
            writeFn.AddChild("chunk", ScriptVar.CreateUndefined());
            writeFn.SetCallback((scope, _) =>
            {
                var chunk = scope.FindChild("chunk")?.Var;
                if (chunk != null && !chunk.IsUndefined) capturedBuf.Append(chunk.String);
            }, null);
            w.AddChild("write", writeFn);

            // .end(chunk?)
            var endFn = ScriptVar.CreateNativeFunction();
            endFn.AddChild("chunk", ScriptVar.CreateUndefined());
            endFn.SetCallback((scope, _) =>
            {
                var chunk = scope.FindChild("chunk")?.Var;
                if (chunk != null && !chunk.IsUndefined) capturedBuf.Append(chunk.String);

                if (capturedEngine != null)
                {
                    var arr = capturedW.FindChild("__finishHandlers__")?.Var;
                    if (arr != null)
                    {
                        var len = arr.GetArrayLength();
                        for (var i = 0; i < len; i++)
                        {
                            var fn = arr.GetArrayIndex(i);
                            if (fn.IsFunction) capturedEngine.CallFunction(fn, null);
                        }
                    }
                }
            }, null);
            w.AddChild("end", endFn);

            // .on('finish', fn)
            var onFn = ScriptVar.CreateNativeFunction();
            onFn.AddChild("event", ScriptVar.CreateUndefined());
            onFn.AddChild("fn", ScriptVar.CreateUndefined());
            onFn.SetCallback((scope, _) =>
            {
                var evName = scope.FindChild("event")?.Var?.String ?? "";
                var fn = scope.FindChild("fn")?.Var;
                if (fn == null || !fn.IsFunction) return;
                if (evName == "finish")
                {
                    var arr = capturedW.FindChild("__finishHandlers__")?.Var;
                    arr?.SetArrayIndex(arr.GetArrayLength(), fn);
                }
            }, null);
            w.AddChild("on", onFn);

            // .getBuffer() — returns accumulated string (not Node.js API; useful for tests)
            var getBufFn = ScriptVar.CreateNativeFunction();
            getBufFn.SetCallback((scope, _) => scope.ReturnVar = ScriptVar.FromString(capturedBuf.ToString()), null);
            w.AddChild("getBuffer", getBufFn);

            return w;
        }

        // --- Transform ---

        [ScriptMethod("Transform")]
        public static void TransformImpl(ScriptVar var, object userData)
        {
            // Transform extends both Readable and Writable.
            // Simple implementation: writable + readable piped together.
            var engine = userData as ScriptEngine;
            var r = MakeReadable(engine);
            var w = ScriptVar.CreateObject();

            // Copy Readable methods onto the transform
            var link = r.FirstChild;
            while (link != null) { w.AddChild(link.Name, link.Var); link = link.Next; }

            // .write(chunk) — pushes into the readable side
            var writeFn = ScriptVar.CreateNativeFunction();
            writeFn.AddChild("chunk", ScriptVar.CreateUndefined());
            var capturedR = r;
            var capturedEngine = engine;
            writeFn.SetCallback((scope, _) =>
            {
                var chunk = scope.FindChild("chunk")?.Var;
                if (chunk == null || chunk.IsUndefined) return;
                var pushFn2 = capturedR.FindChild("push")?.Var;
                if (pushFn2 != null && pushFn2.IsFunction && capturedEngine != null)
                    capturedEngine.CallFunction(pushFn2, null, chunk);
            }, null);
            w.AddChild("write", writeFn);

            // .end(chunk?)
            var endFn = ScriptVar.CreateNativeFunction();
            endFn.AddChild("chunk", ScriptVar.CreateUndefined());
            endFn.SetCallback((scope, _) =>
            {
                var chunk = scope.FindChild("chunk")?.Var;
                if (chunk != null && !chunk.IsUndefined)
                {
                    var pushFn2 = capturedR.FindChild("push")?.Var;
                    if (pushFn2 != null && pushFn2.IsFunction && capturedEngine != null)
                        capturedEngine.CallFunction(pushFn2, null, chunk);
                }
                // Push null to signal end
                var pushNullFn = capturedR.FindChild("push")?.Var;
                if (pushNullFn != null && pushNullFn.IsFunction && capturedEngine != null)
                    capturedEngine.CallFunction(pushNullFn, null, ScriptVar.CreateNull());
            }, null);
            w.AddChild("end", endFn);

            var.ReturnVar = w;
        }
    }
}
