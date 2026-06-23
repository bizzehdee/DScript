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

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("__url__")]
    public static class UrlFunctionProvider
    {
        // --- URL constructor ---

        [ScriptMethod("URL", "href", "base", AppearAtRoot = true)]
        public static void UrlConstructorImpl(ScriptVar var, object userData)
        {
            var hrefVar = var.GetParameter("href");
            var baseVar = var.GetParameter("base");

            Uri uri;
            try
            {
                if (!baseVar.IsUndefined && !string.IsNullOrEmpty(baseVar.String))
                    uri = new Uri(new Uri(baseVar.String), hrefVar.String);
                else
                    uri = new Uri(hrefVar.String);
            }
            catch (UriFormatException ex)
            {
                throw new ScriptException($"Invalid URL: {ex.Message}");
            }

            var obj = new ScriptVar(ScriptVar.Flags.Object);
            obj.AddChild("href",     new ScriptVar(uri.AbsoluteUri));
            obj.AddChild("protocol", new ScriptVar(uri.Scheme + ":"));
            obj.AddChild("host",     new ScriptVar(uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}"));
            obj.AddChild("hostname", new ScriptVar(uri.Host));
            obj.AddChild("port",     new ScriptVar(uri.IsDefaultPort ? "" : uri.Port.ToString()));
            obj.AddChild("pathname", new ScriptVar(uri.AbsolutePath));
            obj.AddChild("search",   new ScriptVar(string.IsNullOrEmpty(uri.Query) ? "" : uri.Query));
            obj.AddChild("hash",     new ScriptVar(string.IsNullOrEmpty(uri.Fragment) ? "" : uri.Fragment));
            obj.AddChild("origin",   new ScriptVar($"{uri.Scheme}://{uri.Host}:{uri.Port}"));
            obj.AddChild("username", new ScriptVar(uri.UserInfo.Split(':')[0]));
            obj.AddChild("password", new ScriptVar(uri.UserInfo.Contains(':') ? uri.UserInfo.Split(':')[1] : ""));

            // searchParams property
            var searchParams = BuildSearchParams(uri.Query);
            obj.AddChild("searchParams", searchParams);

            // .toString() method
            var capturedUri = uri;
            var toStringFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            toStringFn.SetCallback((s, _) => s.ReturnVar = new ScriptVar(capturedUri.AbsoluteUri), null);
            obj.AddChild("toString", toStringFn);

            var.ReturnVar = obj;
        }

        // --- URLSearchParams constructor ---

        [ScriptMethod("URLSearchParams", "init", AppearAtRoot = true)]
        public static void UrlSearchParamsConstructorImpl(ScriptVar var, object userData)
        {
            var init = var.GetParameter("init");
            var query = init.IsUndefined ? "" : init.String;
            var.ReturnVar = BuildSearchParams(query);
        }

        private static ScriptVar BuildSearchParams(string query)
        {
            var sp = new ScriptVar(ScriptVar.Flags.Object);
            var pairs = new List<(string, string)>();

            if (!string.IsNullOrEmpty(query))
            {
                var q = query.TrimStart('?');
                foreach (var part in q.Split('&'))
                {
                    if (string.IsNullOrEmpty(part)) continue;
                    var idx = part.IndexOf('=');
                    if (idx < 0)
                        pairs.Add((Uri.UnescapeDataString(part), ""));
                    else
                        pairs.Add((Uri.UnescapeDataString(part[..idx]), Uri.UnescapeDataString(part[(idx + 1)..])));
                }
            }

            var capturedPairs = pairs;

            // .get(name) — returns first value for name, or null
            var getFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            getFn.AddChild("name", new ScriptVar(ScriptVar.Flags.Undefined));
            getFn.SetCallback((scope, _) =>
            {
                var name = scope.FindChild("name")?.Var?.String ?? "";
                foreach (var (k, v) in capturedPairs)
                    if (k == name) { scope.ReturnVar = new ScriptVar(v); return; }
                scope.ReturnVar = new ScriptVar(ScriptVar.Flags.Null);
            }, null);
            sp.AddChild("get", getFn);

            // .getAll(name) — returns array of all values for name
            var getAllFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            getAllFn.AddChild("name", new ScriptVar(ScriptVar.Flags.Undefined));
            getAllFn.SetCallback((scope, _) =>
            {
                var name = scope.FindChild("name")?.Var?.String ?? "";
                var arr = new ScriptVar(); arr.SetArray();
                var i = 0;
                foreach (var (k, v) in capturedPairs)
                    if (k == name) arr.SetArrayIndex(i++, new ScriptVar(v));
                scope.ReturnVar = arr;
            }, null);
            sp.AddChild("getAll", getAllFn);

            // .has(name) — returns bool
            var hasFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            hasFn.AddChild("name", new ScriptVar(ScriptVar.Flags.Undefined));
            hasFn.SetCallback((scope, _) =>
            {
                var name = scope.FindChild("name")?.Var?.String ?? "";
                foreach (var (k, _) in capturedPairs)
                    if (k == name) { scope.ReturnVar = new ScriptVar(1); return; }
                scope.ReturnVar = new ScriptVar(0);
            }, null);
            sp.AddChild("has", hasFn);

            // .set(name, value) — replaces or adds
            var setFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            setFn.AddChild("name",  new ScriptVar(ScriptVar.Flags.Undefined));
            setFn.AddChild("value", new ScriptVar(ScriptVar.Flags.Undefined));
            setFn.SetCallback((scope, _) =>
            {
                var name  = scope.FindChild("name")?.Var?.String ?? "";
                var value = scope.FindChild("value")?.Var?.String ?? "";
                capturedPairs.RemoveAll(p => p.Item1 == name);
                capturedPairs.Add((name, value));
            }, null);
            sp.AddChild("set", setFn);

            // .append(name, value)
            var appendFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            appendFn.AddChild("name",  new ScriptVar(ScriptVar.Flags.Undefined));
            appendFn.AddChild("value", new ScriptVar(ScriptVar.Flags.Undefined));
            appendFn.SetCallback((scope, _) =>
            {
                var name  = scope.FindChild("name")?.Var?.String ?? "";
                var value = scope.FindChild("value")?.Var?.String ?? "";
                capturedPairs.Add((name, value));
            }, null);
            sp.AddChild("append", appendFn);

            // .delete(name)
            var deleteFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            deleteFn.AddChild("name", new ScriptVar(ScriptVar.Flags.Undefined));
            deleteFn.SetCallback((scope, _) =>
            {
                var name = scope.FindChild("name")?.Var?.String ?? "";
                capturedPairs.RemoveAll(p => p.Item1 == name);
            }, null);
            sp.AddChild("delete", deleteFn);

            // .toString()
            var toStringFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            toStringFn.SetCallback((scope, _) =>
            {
                var parts = new System.Collections.Generic.List<string>();
                foreach (var (k, v) in capturedPairs)
                    parts.Add($"{Uri.EscapeDataString(k)}={Uri.EscapeDataString(v)}");
                scope.ReturnVar = new ScriptVar(string.Join("&", parts));
            }, null);
            sp.AddChild("toString", toStringFn);

            return sp;
        }
    }
}
