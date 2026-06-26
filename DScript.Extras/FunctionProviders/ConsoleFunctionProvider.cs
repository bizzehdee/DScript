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
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DScript.Extras.FunctionProviders
{

    [ScriptClass("console")]
    public static class ConsoleFunctionProvider
    {
        private static readonly Dictionary<string, Stopwatch> _timers = new Dictionary<string, Stopwatch>();
        private static readonly Dictionary<string, int> _counters = new Dictionary<string, int>();
        private static int _indentLevel = 0;

        private static Action<string> _stdout = Console.WriteLine;
        private static Action<string> _stderr = Console.Error.WriteLine;

        public static void SetOutput(Action<string> stdout, Action<string> stderr)
        {
            if (stdout != null) _stdout = stdout;
            if (stderr != null) _stderr = stderr;
        }

        public static void ResetOutput()
        {
            _stdout = Console.WriteLine;
            _stderr = Console.Error.WriteLine;
        }

        private static string Indent() => new string(' ', _indentLevel * 2);

        // Format a single console.log argument: strings print as their raw value
        // (no quotes), everything else uses GetParsableString().  This matches
        // Node.js behaviour where console.log("hi") outputs  hi  not  "hi".
        private static string FormatValue(ScriptVar v)
            => v.IsString ? v.String : v.GetParsableString();

        // Join all variadic arguments with a single space, matching console.log's
        // multi-argument behaviour. `args` is the rest-parameter array.
        private static string FormatArgs(ScriptVar args)
        {
            var len = args.GetArrayLength();
            if (len == 0) return string.Empty;
            if (len == 1) return FormatValue(args.GetArrayIndex(0));

            var sb = new StringBuilder();
            for (var i = 0; i < len; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(FormatValue(args.GetArrayIndex(i)));
            }
            return sb.ToString();
        }

        [ScriptMethod("log", "...args")]
        public static void ConsoleLogImpl(ScriptVar var, object userData)
        {
            _stdout(Indent() + FormatArgs(var.GetParameter("args")));
        }

        [ScriptMethod("error", "...args")]
        public static void ConsoleErrorImpl(ScriptVar var, object userData)
        {
            _stderr(Indent() + FormatArgs(var.GetParameter("args")));
        }

        [ScriptMethod("warn", "...args")]
        public static void ConsoleWarnImpl(ScriptVar var, object userData)
        {
            _stderr(Indent() + "[WARN] " + FormatArgs(var.GetParameter("args")));
        }

        [ScriptMethod("info", "...args")]
        public static void ConsoleInfoImpl(ScriptVar var, object userData)
        {
            _stdout(Indent() + "[INFO] " + FormatArgs(var.GetParameter("args")));
        }

        [ScriptMethod("debug", "...args")]
        public static void ConsoleDebugImpl(ScriptVar var, object userData)
        {
            _stdout(Indent() + "[DEBUG] " + FormatArgs(var.GetParameter("args")));
        }

        [ScriptMethod("assert", "cond", "msg")]
        public static void ConsoleAssertImpl(ScriptVar var, object userData)
        {
            var cond = var.GetParameter("cond");
            if (!cond.Bool)
            {
                var msgVar = var.GetParameter("msg");
                var msg = msgVar.IsUndefined ? "Assertion failed" : msgVar.GetParsableString();
                _stderr(Indent() + "[ASSERT] " + msg);
            }
        }

        [ScriptMethod("time", "label")]
        public static void ConsoleTimeImpl(ScriptVar var, object userData)
        {
            var labelVar = var.GetParameter("label");
            var label = labelVar.IsUndefined ? "default" : labelVar.String;
            _timers[label] = Stopwatch.StartNew();
        }

        [ScriptMethod("timeEnd", "label")]
        public static void ConsoleTimeEndImpl(ScriptVar var, object userData)
        {
            var labelVar = var.GetParameter("label");
            var label = labelVar.IsUndefined ? "default" : labelVar.String;
            if (_timers.TryGetValue(label, out var sw))
            {
                sw.Stop();
                _timers.Remove(label);
                _stdout(Indent() + $"{label}: {sw.Elapsed.TotalMilliseconds:0.###}ms");
            }
        }

        [ScriptMethod("timeLog", "label")]
        public static void ConsoleTimeLogImpl(ScriptVar var, object userData)
        {
            var labelVar = var.GetParameter("label");
            var label = labelVar.IsUndefined ? "default" : labelVar.String;
            if (_timers.TryGetValue(label, out var sw))
                _stdout(Indent() + $"{label}: {sw.Elapsed.TotalMilliseconds:0.###}ms");
        }

        [ScriptMethod("count", "label")]
        public static void ConsoleCountImpl(ScriptVar var, object userData)
        {
            var labelVar = var.GetParameter("label");
            var label = labelVar.IsUndefined ? "default" : labelVar.String;
            _counters.TryGetValue(label, out var n);
            _counters[label] = ++n;
            _stdout(Indent() + $"{label}: {n}");
        }

        [ScriptMethod("countReset", "label")]
        public static void ConsoleCountResetImpl(ScriptVar var, object userData)
        {
            var labelVar = var.GetParameter("label");
            var label = labelVar.IsUndefined ? "default" : labelVar.String;
            _counters[label] = 0;
        }

        [ScriptMethod("group", "label")]
        public static void ConsoleGroupImpl(ScriptVar var, object userData)
        {
            var labelVar = var.GetParameter("label");
            if (!labelVar.IsUndefined)
                _stdout(Indent() + labelVar.String);
            _indentLevel++;
        }

        [ScriptMethod("groupEnd")]
        public static void ConsoleGroupEndImpl(ScriptVar var, object userData)
        {
            if (_indentLevel > 0) _indentLevel--;
        }

        [ScriptMethod("dir", "obj")]
        public static void ConsoleDirImpl(ScriptVar var, object userData)
        {
            var.GetParameter("obj").Trace(0, null);
        }

        [ScriptMethod("table", "arr")]
        public static void ConsoleTableImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("arr");
            var len = arr.GetArrayLength();
            if (len == 0) return;

            var cols = new List<string>();
            var first = arr.GetArrayIndex(0);
            var link = first.FirstChild;
            while (link != null)
            {
                if (link.Name != ScriptVar.PrototypeClassName)
                    cols.Add(link.Name);
                link = link.Next;
            }

            var widths = new int[cols.Count];
            for (var c = 0; c < cols.Count; c++)
                widths[c] = cols[c].Length;
            for (var r = 0; r < len; r++)
            {
                var row = arr.GetArrayIndex(r);
                for (var c = 0; c < cols.Count; c++)
                {
                    var cell = row.FindChild(cols[c])?.Var.String ?? "";
                    if (cell.Length > widths[c]) widths[c] = cell.Length;
                }
            }

            var sb = new StringBuilder(Indent());
            for (var c = 0; c < cols.Count; c++)
                sb.Append("| ").Append(cols[c].PadRight(widths[c])).Append(' ');
            sb.Append('|');
            _stdout(sb.ToString());

            sb.Clear().Append(Indent());
            for (var c = 0; c < cols.Count; c++)
                sb.Append("+-").Append(new string('-', widths[c])).Append('-');
            sb.Append('+');
            _stdout(sb.ToString());

            for (var r = 0; r < len; r++)
            {
                var row = arr.GetArrayIndex(r);
                sb.Clear().Append(Indent());
                for (var c = 0; c < cols.Count; c++)
                {
                    var cell = row.FindChild(cols[c])?.Var.String ?? "";
                    sb.Append("| ").Append(cell.PadRight(widths[c])).Append(' ');
                }
                sb.Append('|');
                _stdout(sb.ToString());
            }
        }

        [ExcludeFromCodeCoverage]
        [ScriptMethod("clear")]
        public static void ConsoleClearImpl(ScriptVar var, object userData)
        {
            try { Console.Clear(); } catch { /* non-TTY environments throw */ }
        }
    }
}