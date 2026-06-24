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

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("readline")]
    public static class ReadlineFunctionProvider
    {
        // Hidden property keys on the rl interface object
        private const string InputKey   = "__rlInput__";
        private const string OutputKey  = "__rlOutput__";
        private const string ClosedKey  = "__rlClosed__";
        private const string LineKey    = "__rlLineHandlers__";
        private const string CloseKey   = "__rlCloseHandlers__";

        [ScriptMethod("createInterface", "opts")]
        public static void CreateInterfaceImpl(ScriptVar var, object userData)
        {
            var engine = userData as ScriptEngine;
            var opts = var.GetParameter("opts");

            // Resolve input/output streams from opts.
            // opts.input / opts.output are expected to be script-side stream objects
            // with a hidden __reader__ / __writer__ data slot, or we fall back to
            // Console.In / Console.Out if they are not supplied.
            TextReader inputReader = Console.In;
            TextWriter outputWriter = Console.Out;

            if (!opts.IsUndefined)
            {
                var inputChild = opts.FindChild("input");
                if (inputChild != null && !inputChild.Var.IsUndefined)
                {
                    var reader = inputChild.Var.GetData() as TextReader;
                    if (reader != null) inputReader = reader;
                }

                var outputChild = opts.FindChild("output");
                if (outputChild != null && !outputChild.Var.IsUndefined)
                {
                    var writer = outputChild.Var.GetData() as TextWriter;
                    if (writer != null) outputWriter = writer;
                }
            }

            var rl = ScriptVar.CreateObject();

            // Store streams as native data on child vars so callbacks can retrieve them.
            var inputVar = ScriptVar.CreateObject();
            inputVar.SetData(inputReader);
            rl.AddChild(InputKey, inputVar);

            var outputVar = ScriptVar.CreateObject();
            outputVar.SetData(outputWriter);
            rl.AddChild(OutputKey, outputVar);

            rl.AddChild(ClosedKey, ScriptVar.FromInt(0)); // 0 = open

            var lineHandlers = ScriptVar.CreateUndefined(); lineHandlers.SetArray();
            rl.AddChild(LineKey, lineHandlers);

            var closeHandlers = ScriptVar.CreateUndefined(); closeHandlers.SetArray();
            rl.AddChild(CloseKey, closeHandlers);

            // .question(prompt, cb) — write prompt to output, read one line, call cb(answer)
            var capturedRl = rl;
            var capturedEngine = engine;

            var questionFn = ScriptVar.CreateNativeFunction();
            questionFn.AddChild("prompt", ScriptVar.CreateUndefined());
            questionFn.AddChild("cb", ScriptVar.CreateUndefined());
            questionFn.SetCallback((scope, _) =>
            {
                var prompt = scope.FindChild("prompt")?.Var?.String ?? "";
                var cb = scope.FindChild("cb")?.Var;
                if (capturedRl.FindChild(ClosedKey)?.Var?.Int != 0) return;

                var outVar = capturedRl.FindChild(OutputKey)?.Var;
                var writer = outVar?.GetData() as TextWriter ?? Console.Out;
                writer.Write(prompt);

                var inVar = capturedRl.FindChild(InputKey)?.Var;
                var reader = inVar?.GetData() as TextReader ?? Console.In;
                var line = reader.ReadLine() ?? string.Empty;

                if (cb != null && cb.IsFunction && capturedEngine != null)
                    capturedEngine.CallFunction(cb, null, ScriptVar.FromString(line));
            }, null);
            rl.AddChild("question", questionFn);

            // .close() — marks closed and fires 'close' handlers
            var closeFn = ScriptVar.CreateNativeFunction();
            closeFn.SetCallback((scope, _) =>
            {
                var closedLink = capturedRl.FindChild(ClosedKey);
                if (closedLink != null) closedLink.ReplaceWith(ScriptVar.FromInt(1));

                var closeArr = capturedRl.FindChild(CloseKey)?.Var;
                if (closeArr == null || capturedEngine == null) return;
                var len = closeArr.GetArrayLength();
                for (var i = 0; i < len; i++)
                {
                    var fn = closeArr.GetArrayIndex(i);
                    if (fn.IsFunction)
                        capturedEngine.CallFunction(fn, null);
                }
            }, null);
            rl.AddChild("close", closeFn);

            // .on(event, fn) — 'line' or 'close'
            var onFn = ScriptVar.CreateNativeFunction();
            onFn.AddChild("event", ScriptVar.CreateUndefined());
            onFn.AddChild("fn", ScriptVar.CreateUndefined());
            onFn.SetCallback((scope, _) =>
            {
                var evName = scope.FindChild("event")?.Var?.String ?? "";
                var fn = scope.FindChild("fn")?.Var;
                if (fn == null || !fn.IsFunction) return;

                var key = evName switch
                {
                    "line"  => LineKey,
                    "close" => CloseKey,
                    _ => null,
                };
                if (key == null) return;
                var arr = capturedRl.FindChild(key)?.Var;
                arr?.SetArrayIndex(arr.GetArrayLength(), fn);
            }, null);
            rl.AddChild("on", onFn);

            var.ReturnVar = rl;
        }
    }
}
