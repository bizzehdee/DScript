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
using System.Net;
using System.Text;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("http")]
    public static class HttpServerFunctionProvider
    {
        // Hidden property keys on the server object
        private const string ListenerKey   = "__httpListener__";
        private const string HandlerKey    = "__httpHandler__";
        private const string RunningKey    = "__httpRunning__";

        [ScriptMethod("createServer", "fn")]
        public static void CreateServerImpl(ScriptVar var, object userData)
        {
            var engine = userData as ScriptEngine;
            var handler = var.GetParameter("fn");

            var serverObj = ScriptVar.CreateObject();

            // Store request handler function
            serverObj.AddChild(HandlerKey, handler.IsFunction ? handler : ScriptVar.CreateUndefined());
            serverObj.AddChild(RunningKey, ScriptVar.FromInt(0));

            var capturedServer = serverObj;
            var capturedEngine = engine;

            // .listen(port, host?, cb?) — starts the HttpListener and blocks processing
            // requests synchronously one at a time until .close() is called.
            var listenFn = ScriptVar.CreateNativeFunction();
            listenFn.AddChild("port", ScriptVar.CreateUndefined());
            listenFn.AddChild("host", ScriptVar.CreateUndefined());
            listenFn.AddChild("cb", ScriptVar.CreateUndefined());
            listenFn.SetCallback((scope, _) =>
            {
                var portVar = scope.FindChild("port")?.Var;
                var hostVar = scope.FindChild("host")?.Var;
                var cbVar   = scope.FindChild("cb")?.Var;

                var port = portVar?.Int ?? 8080;
                // host may be the callback if omitted
                string hostStr = "localhost";
                if (hostVar != null && !hostVar.IsUndefined && !hostVar.IsFunction)
                    hostStr = hostVar.String;
                else if (hostVar != null && hostVar.IsFunction && (cbVar == null || cbVar.IsUndefined))
                    cbVar = hostVar;

                var listener = new HttpListener();
                listener.Prefixes.Add($"http://{hostStr}:{port}/");
                listener.Start();

                // Store listener so close() can stop it
                var listenerVar = ScriptVar.CreateObject();
                listenerVar.SetData(listener);
                capturedServer.AddChildNoDup(ListenerKey, listenerVar);
                capturedServer.FindChild(RunningKey).Var.Int = 1;

                // Fire the listen callback
                if (cbVar != null && cbVar.IsFunction && capturedEngine != null)
                    capturedEngine.CallFunction(cbVar, null);

                // Process requests synchronously until the server is stopped.
                // After each request, check if close() was called during handling;
                // if so, stop the listener here (not inside the handler) so that
                // the in-flight response is fully delivered before we tear down.
                while (true)
                {
                    HttpListenerContext ctx;
                    try { ctx = listener.GetContext(); }
                    catch (HttpListenerException) { break; }
                    catch (ObjectDisposedException) { break; }

                    HandleRequest(ctx, capturedServer, capturedEngine);

                    // Check after the request is fully handled.
                    if (capturedServer.FindChild(RunningKey)?.Var?.Int == 0)
                        break;
                }

                // Give the OS time to deliver the last response before tearing down.
                System.Threading.Thread.Sleep(20);
                try { listener.Stop(); listener.Close(); } catch { /* already closed */ }
            }, null);
            serverObj.AddChild("listen", listenFn);

            // .close() — signals the loop to stop after the current request completes.
            // Sets running=0 so the loop exits after HandleRequest returns.
            // For calls from outside a handler (e.g. another thread), also calls hl.Stop()
            // to interrupt a blocking GetContext(); the loop will catch the exception and exit.
            var closeFn = ScriptVar.CreateNativeFunction();
            closeFn.AddChild("cb", ScriptVar.CreateUndefined());
            closeFn.SetCallback((scope, _) =>
            {
                capturedServer.FindChild(RunningKey).Var.Int = 0;
                // Fire the callback synchronously (before the listener actually stops).
                var cb = scope.FindChild("cb")?.Var;
                if (cb != null && cb.IsFunction && capturedEngine != null)
                    capturedEngine.CallFunction(cb, null);
            }, null);
            serverObj.AddChild("close", closeFn);

            var.ReturnVar = serverObj;
        }

        private static void HandleRequest(HttpListenerContext ctx, ScriptVar serverObj, ScriptEngine engine)
        {
            if (engine == null) { ctx.Response.Close(); return; }

            var handlerLink = serverObj.FindChild(HandlerKey);
            if (handlerLink == null || !handlerLink.Var.IsFunction) { ctx.Response.Close(); return; }

            var req = ctx.Request;
            var resp = ctx.Response;

            // Build request object
            var reqVar = BuildRequestObject(req, engine);

            // Build response object
            var state = new ResponseState { Response = resp };
            var respVar = BuildResponseObject(state, engine);

            engine.CallFunction(handlerLink.Var, null, reqVar, respVar);

            // Ensure the response is closed even if the handler didn't call end()
            if (!state.Ended)
            {
                try { resp.Close(); } catch { /* ignore */ }
                state.Ended = true;
            }
        }

        private static ScriptVar BuildRequestObject(HttpListenerRequest req, ScriptEngine engine)
        {
            var reqVar = ScriptVar.CreateObject();
            reqVar.AddChild("method", ScriptVar.FromString(req.HttpMethod));
            reqVar.AddChild("url", ScriptVar.FromString(req.RawUrl ?? "/"));

            // headers object
            var headersVar = ScriptVar.CreateObject();
            foreach (string key in req.Headers.Keys)
                headersVar.AddChild(key.ToLowerInvariant(), ScriptVar.FromString(req.Headers[key] ?? ""));
            reqVar.AddChild("headers", headersVar);

            // Read body
            byte[] bodyBytes = Array.Empty<byte>();
            if (req.HasEntityBody)
            {
                using var ms = new System.IO.MemoryStream();
                req.InputStream.CopyTo(ms);
                bodyBytes = ms.ToArray();
            }
            var bodyStr = Encoding.UTF8.GetString(bodyBytes);

            // .on('data', fn) — fires immediately with the body (synchronous model)
            // .on('end', fn) — fires immediately after data
            var dataHandlers  = ScriptVar.CreateUndefined(); dataHandlers.SetArray();
            var endHandlers   = ScriptVar.CreateUndefined(); endHandlers.SetArray();
            const string dataKey = "__reqData__";
            const string endKey  = "__reqEnd__";
            reqVar.AddChild(dataKey, dataHandlers);
            reqVar.AddChild(endKey, endHandlers);

            var capturedReq = reqVar;
            var capturedBody = bodyStr;
            var capturedEngine = engine;

            var onFn = ScriptVar.CreateNativeFunction();
            onFn.AddChild("event", ScriptVar.CreateUndefined());
            onFn.AddChild("fn", ScriptVar.CreateUndefined());
            onFn.SetCallback((scope, _) =>
            {
                var evName = scope.FindChild("event")?.Var?.String ?? "";
                var fn = scope.FindChild("fn")?.Var;
                if (fn == null || !fn.IsFunction || capturedEngine == null) return;

                if (evName == "data" && !string.IsNullOrEmpty(capturedBody))
                    capturedEngine.CallFunction(fn, null, ScriptVar.FromString(capturedBody));
                else if (evName == "end")
                    capturedEngine.CallFunction(fn, null);
                // 'data' with empty body: just don't fire (no data)
            }, null);
            reqVar.AddChild("on", onFn);

            return reqVar;
        }

        private sealed class ResponseState
        {
            public HttpListenerResponse Response { get; set; }
            public int StatusCode { get; set; } = 200;
            public bool Ended { get; set; }
            public readonly List<(byte[], int, int)> Chunks = new();
        }

        private static ScriptVar BuildResponseObject(ResponseState state, ScriptEngine engine)
        {
            // Disable keep-alive so the TCP connection closes after this response.
            // This prevents listener.Close() from aborting an active keep-alive
            // connection before the client has read the response body.
            state.Response.KeepAlive = false;

            var respVar = ScriptVar.CreateObject();

            // .writeHead(status, headers?)
            var writeHeadFn = ScriptVar.CreateNativeFunction();
            writeHeadFn.AddChild("status", ScriptVar.CreateUndefined());
            writeHeadFn.AddChild("headers", ScriptVar.CreateUndefined());
            writeHeadFn.SetCallback((scope, _) =>
            {
                var statusVar = scope.FindChild("status")?.Var;
                if (statusVar != null && !statusVar.IsUndefined)
                    state.Response.StatusCode = statusVar.Int;

                var hdrs = scope.FindChild("headers")?.Var;
                if (hdrs != null && !hdrs.IsUndefined)
                {
                    var link = hdrs.FirstChild;
                    while (link != null)
                    {
                        try { state.Response.AddHeader(link.Name, link.Var.String); } catch { /* ignore invalid names */ }
                        link = link.Next;
                    }
                }
            }, null);
            respVar.AddChild("writeHead", writeHeadFn);

            // .write(data)
            var writeFn = ScriptVar.CreateNativeFunction();
            writeFn.AddChild("data", ScriptVar.CreateUndefined());
            writeFn.SetCallback((scope, _) =>
            {
                if (state.Ended) return;
                var dataVar = scope.FindChild("data")?.Var;
                var text = dataVar != null && !dataVar.IsUndefined ? dataVar.String : string.Empty;
                var bytes = Encoding.UTF8.GetBytes(text);
                state.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }, null);
            respVar.AddChild("write", writeFn);

            // .end(data?)
            var endFn = ScriptVar.CreateNativeFunction();
            endFn.AddChild("data", ScriptVar.CreateUndefined());
            endFn.SetCallback((scope, _) =>
            {
                if (state.Ended) return;
                var dataVar = scope.FindChild("data")?.Var;
                if (dataVar != null && !dataVar.IsUndefined)
                {
                    var bytes = Encoding.UTF8.GetBytes(dataVar.String);
                    state.Response.OutputStream.Write(bytes, 0, bytes.Length);
                }
                try
                {
                    state.Response.OutputStream.Flush();
                    state.Response.Close();
                }
                catch { /* already closed */ }
                state.Ended = true;
            }, null);
            respVar.AddChild("end", endFn);

            return respVar;
        }
    }
}
