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

using System.Globalization;
using System.Text;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("util")]
    public static class UtilFunctionProvider
    {
        [ScriptMethod("format", "fmt", "a0", "a1", "a2", "a3")]
        public static void UtilFormatImpl(ScriptVar var, object userData)
        {
            var fmt = var.GetParameter("fmt").String;
            var args = new[] { "a0", "a1", "a2", "a3" };
            var argIdx = 0;

            var sb = new StringBuilder();
            var i = 0;
            while (i < fmt.Length)
            {
                if (fmt[i] == '%' && i + 1 < fmt.Length && argIdx < args.Length)
                {
                    var spec = fmt[i + 1];
                    var argVar = var.GetParameter(args[argIdx]);
                    switch (spec)
                    {
                        case 's': sb.Append(argVar.String); argIdx++; i += 2; continue;
                        case 'd':
                        case 'i': sb.Append(argVar.Int.ToString(CultureInfo.InvariantCulture)); argIdx++; i += 2; continue;
                        case 'f': sb.Append(argVar.Float.ToString(CultureInfo.InvariantCulture)); argIdx++; i += 2; continue;
                        case 'o':
                        case 'j': sb.Append(Inspect(argVar, 0)); argIdx++; i += 2; continue;
                        case '%': sb.Append('%'); i += 2; continue;
                    }
                }
                sb.Append(fmt[i]);
                i++;
            }
            // Append any remaining args
            while (argIdx < args.Length)
            {
                var argVar = var.GetParameter(args[argIdx]);
                if (argVar.IsUndefined) break;
                sb.Append(' ').Append(argVar.String);
                argIdx++;
            }

            var.ReturnVar.String = sb.ToString();
        }

        [ScriptMethod("inspect", "val")]
        public static void UtilInspectImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.String = Inspect(var.GetParameter("val"), 0);
        }

        [ScriptMethod("promisify", "fn")]
        public static void UtilPromisifyImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var fn = var.GetParameter("fn");

            // Returns a wrapper that calls fn(arg, callback) and captures the result.
            // The wrapper itself is synchronous (full async Promise support needs the timer queue).
            var wrapper = ScriptVar.CreateNativeFunction();
            wrapper.AddChild("arg", ScriptVar.CreateUndefined());
            wrapper.SetCallback((scope, _) =>
            {
                var arg = scope.FindChild("arg")?.Var ?? ScriptVar.CreateUndefined();

                ScriptVar capturedResult = ScriptVar.CreateUndefined();
                ScriptVar capturedError = ScriptVar.CreateUndefined();

                var cb = ScriptVar.CreateNativeFunction();
                cb.AddChild("err", ScriptVar.CreateUndefined());
                cb.AddChild("result", ScriptVar.CreateUndefined());
                cb.SetCallback((cbScope, __) =>
                {
                    var err = cbScope.FindChild("err")?.Var;
                    var res = cbScope.FindChild("result")?.Var;
                    if (err != null && !err.IsNull && !err.IsUndefined)
                        capturedError.CopyValue(err);
                    else if (res != null)
                        capturedResult.CopyValue(res);
                }, null);

                engine.CallFunction(fn, null, arg, cb);

                // Return a simple {then, catch} object backed by the captured result.
                var promiseLike = MakePromiseLike(engine, capturedResult, capturedError);
                scope.ReturnVar = promiseLike;
            }, null);

            var.ReturnVar = wrapper;
        }

        private static ScriptVar MakePromiseLike(ScriptEngine engine, ScriptVar result, ScriptVar error)
        {
            var obj = ScriptVar.CreateObject();

            var thenFn = ScriptVar.CreateNativeFunction();
            thenFn.AddChild("onFulfilled", ScriptVar.CreateUndefined());
            thenFn.SetCallback((scope, _) =>
            {
                var onFulfilled = scope.FindChild("onFulfilled")?.Var;
                if (onFulfilled != null && onFulfilled.IsFunction && error.IsUndefined)
                    engine.CallFunction(onFulfilled, null, result);
            }, null);

            var catchFn = ScriptVar.CreateNativeFunction();
            catchFn.AddChild("onRejected", ScriptVar.CreateUndefined());
            catchFn.SetCallback((scope, _) =>
            {
                var onRejected = scope.FindChild("onRejected")?.Var;
                if (onRejected != null && onRejected.IsFunction && !error.IsUndefined)
                    engine.CallFunction(onRejected, null, error);
            }, null);

            obj.AddChild("then", thenFn);
            obj.AddChild("catch", catchFn);
            return obj;
        }

        [ScriptMethod("deprecate", "fn", "msg")]
        public static void UtilDeprecateImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var fn = var.GetParameter("fn");
            var msg = var.GetParameter("msg").String;

            var warned = false;
            var wrapper = ScriptVar.CreateNativeFunction();
            wrapper.AddChild("arg", ScriptVar.CreateUndefined());
            wrapper.SetCallback((scope, _) =>
            {
                if (!warned)
                {
                    System.Console.Error.WriteLine("DeprecationWarning: " + msg);
                    warned = true;
                }
                var arg = scope.FindChild("arg")?.Var ?? ScriptVar.CreateUndefined();
                var result = engine.CallFunction(fn, null, arg);
                scope.ReturnVar = result;
            }, null);

            var.ReturnVar = wrapper;
        }

        private static string Inspect(ScriptVar val, int depth)
        {
            if (depth > 4) return "[...]";
            if (val.IsNull) return "null";
            if (val.IsUndefined) return "undefined";
            if (val.IsString) return "'" + val.String.Replace("'", "\\'") + "'";
            if (val.IsInt || val.IsDouble) return val.Float.ToString(CultureInfo.InvariantCulture);
            if (val.IsFunction) return "[Function]";
            if (val.IsArray)
            {
                var len = val.GetArrayLength();
                var sb = new StringBuilder("[");
                for (var i = 0; i < len; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(Inspect(val.GetArrayIndex(i), depth + 1));
                }
                sb.Append(']');
                return sb.ToString();
            }
            if (val.IsObject)
            {
                var sb = new StringBuilder("{");
                var first = true;
                var link = val.FirstChild;
                while (link != null)
                {
                    if (link.Name != ScriptVar.PrototypeClassName)
                    {
                        if (!first) sb.Append(", ");
                        first = false;
                        sb.Append(link.Name).Append(": ").Append(Inspect(link.Var, depth + 1));
                    }
                    link = link.Next;
                }
                sb.Append('}');
                return sb.ToString();
            }
            return val.String;
        }
    }
}
