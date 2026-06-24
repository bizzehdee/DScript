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
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class FetchTests
    {
        private HttpListener _listener;
        private string _baseUrl;
        private CancellationTokenSource _cts;

        [SetUp]
        public void SetUp()
        {
            // Find a free port
            var port = FindFreePort();
            _baseUrl = $"http://localhost:{port}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(_baseUrl);
            _listener.Start();
            _cts = new CancellationTokenSource();
            StartServer(_cts.Token);
        }

        [TearDown]
        public void TearDown()
        {
            _cts.Cancel();
            _listener.Stop();
            _listener.Close();
        }

        private static int FindFreePort()
        {
            using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            var port = ((IPEndPoint)socket.LocalEndpoint).Port;
            socket.Stop();
            return port;
        }

        private void StartServer(CancellationToken ct)
        {
            Task.Run(() =>
            {
                while (!ct.IsCancellationRequested && _listener.IsListening)
                {
                    try
                    {
                        var ctx = _listener.GetContext();
                        var req = ctx.Request;
                        var resp = ctx.Response;
                        if (req.Url.PathAndQuery == "/hello")
                        {
                            var buf = Encoding.UTF8.GetBytes("hello world");
                            resp.StatusCode = 200;
                            resp.ContentType = "text/plain";
                            resp.OutputStream.Write(buf, 0, buf.Length);
                        }
                        else if (req.Url.PathAndQuery == "/json")
                        {
                            var buf = Encoding.UTF8.GetBytes("{\"value\":42}");
                            resp.StatusCode = 200;
                            resp.ContentType = "application/json";
                            resp.OutputStream.Write(buf, 0, buf.Length);
                        }
                        else if (req.Url.PathAndQuery == "/notfound")
                        {
                            resp.StatusCode = 404;
                        }
                        else if (req.Url.PathAndQuery == "/post" && req.HttpMethod == "POST")
                        {
                            var body = Encoding.UTF8.GetBytes("posted");
                            resp.StatusCode = 200;
                            resp.OutputStream.Write(body, 0, body.Length);
                        }
                        else
                        {
                            resp.StatusCode = 200;
                        }
                        resp.OutputStream.Close();
                    }
                    catch { break; }
                }
            }, ct);
        }

        private ScriptEngine MakeEngine()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine, EnginePermissions.Network);
            return engine;
        }

        [Test]
        public void Fetch_Get_ReturnsOkTrue()
        {
            var engine = MakeEngine();
            engine.Execute($"var r = fetch('{_baseUrl}hello'); var result = r.ok;");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.True);
        }

        [Test]
        public void Fetch_Get_ReturnsStatus200()
        {
            var engine = MakeEngine();
            engine.Execute($"var r = fetch('{_baseUrl}hello'); var result = r.status;");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(200));
        }

        [Test]
        public void Fetch_Get_TextMethod_ReturnsBody()
        {
            var engine = MakeEngine();
            engine.Execute($"var r = fetch('{_baseUrl}hello'); var result = r.text();");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("hello world"));
        }

        [Test]
        public void Fetch_NotFound_OkFalse()
        {
            var engine = MakeEngine();
            engine.Execute($"var r = fetch('{_baseUrl}notfound'); var result = r.ok;");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.False);
        }

        [Test]
        public void Fetch_NotFound_Status404()
        {
            var engine = MakeEngine();
            engine.Execute($"var r = fetch('{_baseUrl}notfound'); var result = r.status;");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(404));
        }

        [Test]
        public void Fetch_ArrayBuffer_ReturnsBuffer()
        {
            var engine = MakeEngine();
            engine.Execute($"var r = fetch('{_baseUrl}hello'); var buf = r.arrayBuffer(); var result = buf.length;");
            Assert.That(engine.Root.GetParameter("result").Int, Is.GreaterThan(0));
        }

        [Test]
        public void Fetch_Post_SendsRequest()
        {
            var engine = MakeEngine();
            engine.Execute(
                $"var r = fetch('{_baseUrl}post', {{method: 'POST', body: 'data'}}); " +
                "var result = r.text();");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("posted"));
        }

        [Test]
        public void Fetch_InvalidUrl_ThrowsScriptException()
        {
            var engine = MakeEngine();
            Assert.Throws<ScriptException>(() =>
                engine.Run(ScriptEngine.Compile("fetch('http://invalid.invalid.invalid/');")));
        }
    }
}
