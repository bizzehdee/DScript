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
using System.IO;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("path")]
    public static class PathFunctionProvider
    {
        [ScriptMethod("join", "a", "b", "c", "d")]
        public static void PathJoinImpl(ScriptVar var, object userData)
        {
            var parts = new List<string>();
            foreach (var name in new[] { "a", "b", "c", "d" })
            {
                var p = var.GetParameter(name);
                if (!p.IsUndefined) parts.Add(p.String);
            }
            var joined = parts.Count == 0 ? "." : Path.Combine(parts.ToArray());
            var.ReturnVar.String = NormalizeSeparators(joined);
        }

        [ScriptMethod("resolve", "a", "b", "c", "d")]
        public static void PathResolveImpl(ScriptVar var, object userData)
        {
            var parts = new List<string>();
            foreach (var name in new[] { "a", "b", "c", "d" })
            {
                var p = var.GetParameter(name);
                if (!p.IsUndefined) parts.Add(p.String);
            }
            var combined = parts.Count == 0 ? "." : Path.Combine(parts.ToArray());
            var.ReturnVar.String = NormalizeSeparators(Path.GetFullPath(combined));
        }

        [ScriptMethod("dirname", "p")]
        public static void PathDirnameImpl(ScriptVar var, object userData)
        {
            var p = var.GetParameter("p").String;
            var dir = Path.GetDirectoryName(p);
            var.ReturnVar.String = NormalizeSeparators(dir ?? ".");
        }

        [ScriptMethod("basename", "p", "ext")]
        public static void PathBasenameImpl(ScriptVar var, object userData)
        {
            var p = var.GetParameter("p").String;
            var extParam = var.GetParameter("ext");
            if (!extParam.IsUndefined)
            {
                var actualExt = Path.GetExtension(p);
                if (string.Equals(actualExt, extParam.String, StringComparison.OrdinalIgnoreCase))
                    var.ReturnVar.String = Path.GetFileNameWithoutExtension(p);
                else
                    var.ReturnVar.String = Path.GetFileName(p);
            }
            else
            {
                var.ReturnVar.String = Path.GetFileName(p);
            }
        }

        [ScriptMethod("extname", "p")]
        public static void PathExtnameImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.String = Path.GetExtension(var.GetParameter("p").String);
        }

        [ScriptMethod("isAbsolute", "p")]
        public static void PathIsAbsoluteImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = Path.IsPathRooted(var.GetParameter("p").String) ? 1 : 0;
        }

        [ScriptMethod("normalize", "p")]
        public static void PathNormalizeImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.String = NormalizePath(var.GetParameter("p").String);
        }

        [ScriptProperty("sep")]
        public static void PathSepImpl(ScriptVar var, object userData)
        {
            var.String = "/";
        }

        private static string NormalizeSeparators(string path)
            => path.Replace('\\', '/');

        private static string NormalizePath(string p)
        {
            // Normalize separators, collapse . and .., keep leading slash if present.
            p = p.Replace('\\', '/');
            var leadingSlash = p.StartsWith("/", StringComparison.Ordinal);
            var parts = p.Split('/');
            var stack = new List<string>();
            foreach (var part in parts)
            {
                if (part == ".." && stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
                else if (part != "." && part != "")
                    stack.Add(part);
            }
            var result = string.Join("/", stack);
            if (result == "") result = ".";
            return leadingSlash ? "/" + result : result;
        }
    }
}
