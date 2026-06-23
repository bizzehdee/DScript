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
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("net")]
    public static class NetFunctionProvider
    {
        // --- net.createServer(connectionListener?) ---

        [ScriptMethod("createServer", "connectionListener")]
        public static void CreateServerImpl(ScriptVar var, object userData)
        {
            var engine = userData as ScriptEngine;
            var handler = var.GetParameter("connectionListener");

            var serverObj = new ScriptVar(ScriptVar.Flags.Object);
            serverObj.AddChild("__netRunning__", new ScriptVar(0));
            serverObj.AddChild("__netListener__", new ScriptVar(ScriptVar.Flags.Undefined));

            if (handler.IsFunction)
                serverObj.AddChild("__netHandler__", handler);

            var capturedServer = serverObj;
            var capturedEngine = engine;

            // .listen(port, host?, cb?)
            var listenFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            listenFn.AddChild("port", new ScriptVar(ScriptVar.Flags.Undefined));
            listenFn.AddChild("host", new ScriptVar(ScriptVar.Flags.Undefined));
            listenFn.AddChild("cb", new ScriptVar(ScriptVar.Flags.Undefined));
            listenFn.SetCallback((scope, _) =>
            {
                var portVar = scope.FindChild("port")?.Var;
                var hostVar = scope.FindChild("host")?.Var;
                var cbVar   = scope.FindChild("cb")?.Var;

                var port = portVar?.Int ?? 0;
                string hostStr = "127.0.0.1";
                if (hostVar != null && !hostVar.IsUndefined && !hostVar.IsFunction)
                    hostStr = hostVar.String;
                else if (hostVar != null && hostVar.IsFunction && (cbVar == null || cbVar.IsUndefined))
                    cbVar = hostVar;

                var tcpListener = new TcpListener(IPAddress.Parse(hostStr), port);
                tcpListener.Start();

                // Store the listener object
                var listenerVar = new ScriptVar(ScriptVar.Flags.Object);
                listenerVar.SetData(tcpListener);
                capturedServer.AddChildNoDup("__netListener__", listenerVar);
                capturedServer.FindChild("__netRunning__").Var.Int = 1;

                // Fire listen callback
                if (cbVar != null && cbVar.IsFunction && capturedEngine != null)
                    capturedEngine.CallFunction(cbVar, null);

                // Synchronously accept connections until .close() is called
                while (capturedServer.FindChild("__netRunning__")?.Var?.Int == 1)
                {
                    tcpListener.Server.ReceiveTimeout = 100;
                    Socket socket;
                    try
                    {
                        if (!tcpListener.Pending())
                        {
                            System.Threading.Thread.Sleep(10);
                            continue;
                        }
                        socket = tcpListener.AcceptSocket();
                    }
                    catch (SocketException) { break; }
                    catch (ObjectDisposedException) { break; }

                    var socketVar = BuildSocketObject(socket, capturedEngine);

                    // Fire connection handlers on the server
                    var connHandlers = capturedServer.FindChild("__netConnHandlers__")?.Var;
                    if (connHandlers != null && capturedEngine != null)
                    {
                        var len = connHandlers.GetArrayLength();
                        for (var i = 0; i < len; i++)
                        {
                            var fn = connHandlers.GetArrayIndex(i);
                            if (fn.IsFunction) capturedEngine.CallFunction(fn, null, socketVar);
                        }
                    }

                    // Fire the constructor-provided handler
                    var handlerLink = capturedServer.FindChild("__netHandler__")?.Var;
                    if (handlerLink != null && handlerLink.IsFunction && capturedEngine != null)
                        capturedEngine.CallFunction(handlerLink, null, socketVar);
                }

                try { tcpListener.Stop(); } catch { /* ignore */ }
            }, null);
            serverObj.AddChild("listen", listenFn);

            // .on('connection', fn)
            var connHandlers = new ScriptVar(); connHandlers.SetArray();
            serverObj.AddChild("__netConnHandlers__", connHandlers);

            var onFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            onFn.AddChild("event", new ScriptVar(ScriptVar.Flags.Undefined));
            onFn.AddChild("fn", new ScriptVar(ScriptVar.Flags.Undefined));
            onFn.SetCallback((scope, _) =>
            {
                var evName = scope.FindChild("event")?.Var?.String ?? "";
                var fn = scope.FindChild("fn")?.Var;
                if (fn == null || !fn.IsFunction) return;
                if (evName == "connection")
                {
                    var arr = capturedServer.FindChild("__netConnHandlers__")?.Var;
                    arr?.SetArrayIndex(arr.GetArrayLength(), fn);
                }
            }, null);
            serverObj.AddChild("on", onFn);

            // .close(cb?)
            var closeFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            closeFn.AddChild("cb", new ScriptVar(ScriptVar.Flags.Undefined));
            closeFn.SetCallback((scope, _) =>
            {
                capturedServer.FindChild("__netRunning__").Var.Int = 0;
                var listenerLink = capturedServer.FindChild("__netListener__")?.Var;
                if (listenerLink?.GetData() is TcpListener tl)
                {
                    try { tl.Stop(); } catch { /* ignore */ }
                }
                var cb = scope.FindChild("cb")?.Var;
                if (cb != null && cb.IsFunction && capturedEngine != null)
                    capturedEngine.CallFunction(cb, null);
            }, null);
            serverObj.AddChild("close", closeFn);

            var.ReturnVar = serverObj;
        }

        // --- net.createConnection(port, host?, connectListener?) ---

        [ScriptMethod("createConnection", "port", "host", "connectListener")]
        public static void CreateConnectionImpl(ScriptVar var, object userData)
        {
            var engine = userData as ScriptEngine;
            var portVar = var.GetParameter("port");
            var hostVar = var.GetParameter("host");
            var cbVar   = var.GetParameter("connectListener");

            var port = portVar.Int;
            string host = "127.0.0.1";
            if (!hostVar.IsUndefined && !hostVar.IsFunction)
                host = hostVar.String;
            else if (hostVar.IsFunction && cbVar.IsUndefined)
                cbVar = hostVar;

            var client = new TcpClient();
            try { client.Connect(host, port); }
            catch (SocketException ex) { throw new ScriptException($"net.createConnection failed: {ex.Message}"); }

            var socketVar = BuildSocketObject(client.Client, engine);

            // Fire connect callback immediately (synchronous connect)
            if (cbVar.IsFunction && engine != null)
                engine.CallFunction(cbVar, null, socketVar);

            // Fire 'connect' event handlers that were registered before the callback
            var connectHandlers = socketVar.FindChild("__netConnectHandlers__")?.Var;
            if (connectHandlers != null && engine != null)
            {
                var len = connectHandlers.GetArrayLength();
                for (var i = 0; i < len; i++)
                {
                    var fn = connectHandlers.GetArrayIndex(i);
                    if (fn.IsFunction) engine.CallFunction(fn, null);
                }
            }

            var.ReturnVar = socketVar;
        }

        // --- socket object helpers ---

        internal static ScriptVar BuildSocketObject(Socket socket, ScriptEngine engine)
        {
            var s = new ScriptVar(ScriptVar.Flags.Object);
            var socketData = new ScriptVar(ScriptVar.Flags.Object);
            socketData.SetData(socket);
            s.AddChild("__netSocket__", socketData);

            var dataHandlers    = new ScriptVar(); dataHandlers.SetArray();
            var closeHandlers   = new ScriptVar(); closeHandlers.SetArray();
            var connectHandlers = new ScriptVar(); connectHandlers.SetArray();
            s.AddChild("__netDataHandlers__",    dataHandlers);
            s.AddChild("__netCloseHandlers__",   closeHandlers);
            s.AddChild("__netConnectHandlers__", connectHandlers);

            var capturedS      = s;
            var capturedEngine = engine;
            var capturedSocket = socket;

            // .write(data)
            var writeFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            writeFn.AddChild("data", new ScriptVar(ScriptVar.Flags.Undefined));
            writeFn.SetCallback((scope, _) =>
            {
                var dataVar = scope.FindChild("data")?.Var;
                if (dataVar == null || dataVar.IsUndefined) return;
                var bytes = Encoding.UTF8.GetBytes(dataVar.String);
                try { capturedSocket.Send(bytes); } catch { /* ignore disconnected */ }
            }, null);
            s.AddChild("write", writeFn);

            // .end(data?) — send optional data then shutdown
            var endFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            endFn.AddChild("data", new ScriptVar(ScriptVar.Flags.Undefined));
            endFn.SetCallback((scope, _) =>
            {
                var dataVar = scope.FindChild("data")?.Var;
                if (dataVar != null && !dataVar.IsUndefined)
                {
                    var bytes = Encoding.UTF8.GetBytes(dataVar.String);
                    try { capturedSocket.Send(bytes); } catch { /* ignore */ }
                }
                try { capturedSocket.Shutdown(SocketShutdown.Both); capturedSocket.Close(); } catch { /* ignore */ }

                // Fire close handlers
                var arr = capturedS.FindChild("__netCloseHandlers__")?.Var;
                if (arr != null && capturedEngine != null)
                {
                    var len = arr.GetArrayLength();
                    for (var i = 0; i < len; i++)
                    {
                        var fn = arr.GetArrayIndex(i);
                        if (fn.IsFunction) capturedEngine.CallFunction(fn, null);
                    }
                }
            }, null);
            s.AddChild("end", endFn);

            // .destroy() — forcibly close
            var destroyFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            destroyFn.SetCallback((scope, _) =>
            {
                try { capturedSocket.Close(); } catch { /* ignore */ }
            }, null);
            s.AddChild("destroy", destroyFn);

            // .read() — read available data (non-blocking if none available; returns empty string)
            var readFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            readFn.SetCallback((scope, _) =>
            {
                if (!capturedSocket.Connected) { scope.ReturnVar = new ScriptVar(""); return; }
                var buf = new byte[4096];
                string result;
                try
                {
                    capturedSocket.ReceiveTimeout = 0; // non-blocking peek
                    if (capturedSocket.Available == 0) { scope.ReturnVar = new ScriptVar(""); return; }
                    var n = capturedSocket.Receive(buf);
                    result = Encoding.UTF8.GetString(buf, 0, n);
                }
                catch { result = ""; }
                scope.ReturnVar = new ScriptVar(result);
            }, null);
            s.AddChild("read", readFn);

            // .on(event, fn) — 'data', 'close', 'connect'
            var onFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            onFn.AddChild("event", new ScriptVar(ScriptVar.Flags.Undefined));
            onFn.AddChild("fn", new ScriptVar(ScriptVar.Flags.Undefined));
            onFn.SetCallback((scope, _) =>
            {
                var evName = scope.FindChild("event")?.Var?.String ?? "";
                var fn = scope.FindChild("fn")?.Var;
                if (fn == null || !fn.IsFunction) return;

                string arrKey = evName switch
                {
                    "data"    => "__netDataHandlers__",
                    "close"   => "__netCloseHandlers__",
                    "connect" => "__netConnectHandlers__",
                    _         => null
                };
                if (arrKey == null) return;
                var arr = capturedS.FindChild(arrKey)?.Var;
                arr?.SetArrayIndex(arr.GetArrayLength(), fn);
            }, null);
            s.AddChild("on", onFn);

            // remoteAddress / remotePort
            try
            {
                if (socket.RemoteEndPoint is IPEndPoint ep)
                {
                    s.AddChild("remoteAddress", new ScriptVar(ep.Address.ToString()));
                    s.AddChild("remotePort", new ScriptVar(ep.Port));
                }
            }
            catch { /* not connected yet */ }

            return s;
        }
    }
}
