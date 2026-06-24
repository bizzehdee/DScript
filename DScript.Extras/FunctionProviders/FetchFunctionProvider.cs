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
using System.Net.Http;
using System.Text;

using DScript.Extras.Registrars;
namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("__fetch__")]
    public static class FetchFunctionProvider
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        [ScriptMethod("fetch", "url", "opts", AppearAtRoot = true)]
        public static void FetchImpl(ScriptVar var, object userData)
        {
            if (userData is ScriptEngine eng)
                EnginePermissionStore.Require(eng, EnginePermissions.Network);
            var url = var.GetParameter("url").String;
            var opts = var.GetParameter("opts");

            var method = "GET";
            string body = null;
            string contentType = null;

            if (!opts.IsUndefined)
            {
                var methodChild = opts.FindChild("method");
                if (methodChild != null) method = methodChild.Var.String.ToUpperInvariant();

                var bodyChild = opts.FindChild("body");
                if (bodyChild != null && !bodyChild.Var.IsUndefined)
                    body = bodyChild.Var.String;

                var headersChild = opts.FindChild("headers");
                if (headersChild != null && !headersChild.Var.IsUndefined)
                {
                    var ctChild = headersChild.Var.FindChild("Content-Type")
                               ?? headersChild.Var.FindChild("content-type");
                    if (ctChild != null)
                        contentType = ctChild.Var.String;
                }
            }

            HttpResponseMessage response;
            try
            {
                var request = new HttpRequestMessage(new HttpMethod(method), url);
                if (body != null)
                    request.Content = new StringContent(body, Encoding.UTF8, contentType ?? "application/json");
                response = _httpClient.Send(request);
            }
            catch (Exception ex)
            {
                throw new ScriptException($"fetch failed: {ex.Message}");
            }

            var statusCode = (int)response.StatusCode;
            var statusText = response.ReasonPhrase ?? "";
            var responseBodyBytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            var responseBodyString = Encoding.UTF8.GetString(responseBodyBytes);

            var resp = new ScriptVar(ScriptVar.Flags.Object);
            resp.AddChild("ok", new ScriptVar(statusCode >= 200 && statusCode < 300));
            resp.AddChild("status", new ScriptVar(statusCode));
            resp.AddChild("statusText", new ScriptVar(statusText));

            // headers object with .get(name) method
            var headers = BuildHeadersObject(response.Headers, response.Content.Headers);
            resp.AddChild("headers", headers);

            // .text() → string
            var textBody = responseBodyString;
            var textFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            textFn.SetCallback((scope, _) =>
            {
                scope.ReturnVar = new ScriptVar(textBody);
            }, null);
            resp.AddChild("text", textFn);

            // .json() → parsed object (as JSON string first, then parse via engine)
            var jsonFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            var engine = userData as ScriptEngine;
            jsonFn.SetCallback((scope, _) =>
            {
                if (engine != null)
                {
                    // Evaluate JSON using the engine's JSON.parse
                    var jsonParseLink = engine.Root.FindChild("JSON");
                    var jsonVar = jsonParseLink?.Var;
                    var parseLink = jsonVar?.FindChild("parse");
                    if (parseLink != null && parseLink.Var.IsFunction)
                    {
                        var argVar = new ScriptVar(textBody);
                        var result = engine.CallFunction(parseLink.Var, null, argVar);
                        if (result != null) scope.ReturnVar = result;
                        return;
                    }
                }
                scope.ReturnVar = new ScriptVar(textBody);
            }, null);
            resp.AddChild("json", jsonFn);

            // .arrayBuffer() → Buffer
            var bodyBytes = responseBodyBytes;
            var bufFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            bufFn.SetCallback((scope, _) =>
            {
                scope.ReturnVar = BufferRegistrar.MakeBuffer(bodyBytes);
            }, null);
            resp.AddChild("arrayBuffer", bufFn);

            var.ReturnVar = resp;
        }

        private static ScriptVar BuildHeadersObject(
            System.Net.Http.Headers.HttpResponseHeaders respHeaders,
            System.Net.Http.Headers.HttpContentHeaders contentHeaders)
        {
            var headers = new ScriptVar(ScriptVar.Flags.Object);

            // Store all headers as children
            foreach (var h in respHeaders)
                headers.AddChild(h.Key.ToLowerInvariant(), new ScriptVar(string.Join(", ", h.Value)));
            foreach (var h in contentHeaders)
                headers.AddChild(h.Key.ToLowerInvariant(), new ScriptVar(string.Join(", ", h.Value)));

            // .get(name) method
            var getFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            getFn.AddChild("name", new ScriptVar(ScriptVar.Flags.Undefined));
            var captured = headers;
            getFn.SetCallback((scope, _) =>
            {
                var name = scope.FindChild("name")?.Var?.String?.ToLowerInvariant() ?? "";
                var link = captured.FindChild(name);
                if (link != null)
                    scope.ReturnVar = link.Var;
                else
                    scope.ReturnVar = new ScriptVar(ScriptVar.Flags.Undefined);
            }, null);
            headers.AddChild("get", getFn);

            return headers;
        }
    }
}
