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

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("Function")]
    public static class FunctionFunctionProvider
    {
        // Collects named args, trimming trailing undefineds so the callee
        // does not receive phantom undefined args beyond what the caller passed.
        private static ScriptVar[] CollectArgs(ScriptVar scope, string[] names)
        {
            var list = new List<ScriptVar>();
            foreach (var n in names)
                list.Add(scope.GetParameter(n));
            while (list.Count > 0 && list[list.Count - 1].IsUndefined)
                list.RemoveAt(list.Count - 1);
            return list.ToArray();
        }

        private static int GetFunctionLength(ScriptVar fn)
        {
            if (fn.IsNative)
            {
                var cnt = 0;
                var p = fn.FirstChild;
                while (p != null) { cnt++; p = p.Next; }
                return cnt;
            }
            var vmfn = (Vm.VmFunction)fn.GetData();
            return vmfn?.Body?.Parameters?.Count ?? 0;
        }

        private static string GetFunctionName(ScriptVar fn)
        {
            var link = fn.FindChild("name");
            if (link != null) return link.Var.String;
            if (!fn.IsNative)
            {
                var vmfn = (Vm.VmFunction)fn.GetData();
                return vmfn?.Body?.Name ?? "";
            }
            return "";
        }

        [ScriptMethod("call", "thisArg", "a0", "a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8", "a9")]
        public static void FunctionCallImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var fn = var.GetParameter("this");
            var newThis = var.GetParameter("thisArg");
            var args = CollectArgs(var, ["a0", "a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8", "a9"]);
            var.ReturnVar = engine.CallFunction(fn, newThis, args);
        }

        [ScriptMethod("apply", "thisArg", "argsArray")]
        public static void FunctionApplyImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var fn = var.GetParameter("this");
            var newThis = var.GetParameter("thisArg");
            var argsArray = var.GetParameter("argsArray");
            var len = (argsArray.IsUndefined || argsArray.IsNull) ? 0 : argsArray.GetArrayLength();
            var args = new ScriptVar[len];
            for (var i = 0; i < len; i++)
                args[i] = argsArray.GetArrayIndex(i);
            var.ReturnVar = engine.CallFunction(fn, newThis, args);
        }

        [ScriptMethod("bind", "thisArg", "a0", "a1", "a2", "a3", "a4")]
        public static void FunctionBindImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var fn = var.GetParameter("this");
            var newThis = var.GetParameter("thisArg");
            var partials = CollectArgs(var, ["a0", "a1", "a2", "a3", "a4"]);

            var boundName = "bound " + GetFunctionName(fn);
            var boundLength = System.Math.Max(0, GetFunctionLength(fn) - partials.Length);

            var capturedFn = fn;
            var capturedThis = newThis;
            var capturedPartials = (ScriptVar[])partials.Clone();

            // Bound function accepts up to 10 additional call-site args (b0..b9)
            var boundFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            for (var i = 0; i < 10; i++)
                boundFn.AddChild($"b{i}", new ScriptVar(ScriptVar.Flags.Undefined));

            boundFn.SetCallback((scope, _) =>
            {
                var callArgs = new List<ScriptVar>(capturedPartials);
                for (var i = 0; i < 10; i++)
                    callArgs.Add(scope.GetParameter($"b{i}"));
                while (callArgs.Count > capturedPartials.Length && callArgs[callArgs.Count - 1].IsUndefined)
                    callArgs.RemoveAt(callArgs.Count - 1);
                scope.ReturnVar = engine.CallFunction(capturedFn, capturedThis, callArgs.ToArray());
            }, null);

            boundFn.AddChild("name", new ScriptVar(boundName));
            boundFn.AddChild("length", new ScriptVar(boundLength));

            var.ReturnVar = boundFn;
        }
    }
}
