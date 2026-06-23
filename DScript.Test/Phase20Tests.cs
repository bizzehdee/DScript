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
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    // =========================================================
    // zlib tests
    // =========================================================

    [TestFixture]
    public class ZlibTests
    {
        private static ScriptEngine MakeEngine()
        {
            var e = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(e);
            return e;
        }

        private static void Exec(string code) => MakeEngine().Run(ScriptEngine.Compile(code));

        private static ScriptEngine ExecAndReturn(string code)
        {
            var e = MakeEngine();
            e.Run(ScriptEngine.Compile(code));
            return e;
        }

        [Test]
        public void GzipSync_RoundTrip()
        {
            var e = ExecAndReturn(@"
                var original = 'hello world';
                var compressed = zlib.gzipSync(original);
                var decompressed = zlib.gunzipSync(compressed);
                var result = decompressed.toString('utf8');
            ");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("hello world"));
        }

        [Test]
        public void DeflateSync_RoundTrip()
        {
            var e = ExecAndReturn(@"
                var original = 'compress me';
                var compressed = zlib.deflateSync(original);
                var decompressed = zlib.inflateSync(compressed);
                var result = decompressed.toString('utf8');
            ");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("compress me"));
        }

        [Test]
        public void GzipSync_ReturnsBuffer()
        {
            var e = ExecAndReturn(@"var buf = zlib.gzipSync('x'); var result = Buffer.isBuffer(buf) ? 'yes' : 'no';");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("yes"));
        }

        [Test]
        public void GzipSync_EmptyString_RoundTrip()
        {
            var e = ExecAndReturn(@"
                var compressed = zlib.gzipSync('');
                var decompressed = zlib.gunzipSync(compressed);
                var result = decompressed.toString('utf8');
            ");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo(""));
        }

        [Test]
        public void InflateSync_WrongData_Throws()
        {
            Assert.Throws<ScriptException>(() => Exec("zlib.inflateSync('not-deflated');"));
        }
    }

    // =========================================================
    // URL / URLSearchParams tests
    // =========================================================

    [TestFixture]
    public class UrlTests
    {
        private static ScriptEngine MakeEngine()
        {
            var e = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(e);
            return e;
        }

        private static ScriptEngine Exec(string code)
        {
            var e = MakeEngine();
            e.Run(ScriptEngine.Compile(code));
            return e;
        }

        [Test]
        public void URL_ParsesProtocolAndHost()
        {
            var e = Exec("var u = new URL('https://example.com/path'); var result = u.protocol + '|' + u.hostname;");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("https:|example.com"));
        }

        [Test]
        public void URL_ParsesPathname()
        {
            var e = Exec("var u = new URL('https://example.com/foo/bar'); var result = u.pathname;");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("/foo/bar"));
        }

        [Test]
        public void URL_ParsesQueryString()
        {
            var e = Exec("var u = new URL('https://example.com/?a=1'); var result = u.search;");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("?a=1"));
        }

        [Test]
        public void URL_SearchParamsGet()
        {
            var e = Exec("var u = new URL('https://example.com/?foo=bar'); var result = u.searchParams.get('foo');");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("bar"));
        }

        [Test]
        public void URL_RelativeToBase()
        {
            var e = Exec("var u = new URL('/page', 'https://example.com'); var result = u.href;");
            var href = e.Root.FindChild("result")?.Var?.String;
            Assert.That(href, Does.Contain("example.com"));
            Assert.That(href, Does.Contain("/page"));
        }

        [Test]
        public void URL_ToString()
        {
            var e = Exec("var u = new URL('http://a.com/b'); var result = u.toString();");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Does.Contain("a.com"));
        }

        [Test]
        public void URL_InvalidHref_Throws()
        {
            Assert.Throws<ScriptException>(() => Exec("new URL('not a url');"));
        }

        [Test]
        public void URLSearchParams_SetAndGet()
        {
            var e = Exec(@"
                var sp = new URLSearchParams('a=1&b=2');
                sp.set('a', '99');
                var result = sp.get('a');
            ");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("99"));
        }

        [Test]
        public void URLSearchParams_Has()
        {
            var e = Exec("var sp = new URLSearchParams('x=1'); var result = sp.has('x') ? 'yes' : 'no';");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("yes"));
        }

        [Test]
        public void URLSearchParams_Delete()
        {
            var e = Exec("var sp = new URLSearchParams('x=1'); sp.delete('x'); var result = sp.has('x') ? 'yes' : 'no';");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("no"));
        }

        [Test]
        public void URLSearchParams_Append_GetAll()
        {
            var e = Exec(@"
                var sp = new URLSearchParams('');
                sp.append('tag', 'a');
                sp.append('tag', 'b');
                var all = sp.getAll('tag');
                var result = all[0] + ',' + all[1];
            ");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("a,b"));
        }

        [Test]
        public void URLSearchParams_ToString()
        {
            var e = Exec("var sp = new URLSearchParams(''); sp.set('k', 'v'); var result = sp.toString();");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("k=v"));
        }

        [Test]
        public void URLSearchParams_Empty_Init()
        {
            var e = Exec("var sp = new URLSearchParams(); var result = sp.has('x') ? 'yes' : 'no';");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("no"));
        }
    }

    // =========================================================
    // TextEncoder / TextDecoder tests
    // =========================================================

    [TestFixture]
    public class TextEncoderTests
    {
        private static ScriptEngine MakeEngine()
        {
            var e = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(e);
            return e;
        }

        [Test]
        public void TextEncoder_EncodingIsUtf8()
        {
            var e = MakeEngine();
            e.Execute("var enc = new TextEncoder(); var result = enc.encoding;");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("utf-8"));
        }

        [Test]
        public void TextEncoder_Encode_ReturnsBuffer()
        {
            var e = MakeEngine();
            e.Execute("var enc = new TextEncoder(); var buf = enc.encode('hi'); var result = Buffer.isBuffer(buf) ? 'yes' : 'no';");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("yes"));
        }

        [Test]
        public void TextEncoder_Decode_RoundTrip()
        {
            var e = MakeEngine();
            e.Execute(@"
                var enc = new TextEncoder();
                var dec = new TextDecoder();
                var buf = enc.encode('hello');
                var result = dec.decode(buf);
            ");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("hello"));
        }

        [Test]
        public void TextDecoder_DefaultEncoding()
        {
            var e = MakeEngine();
            e.Execute("var dec = new TextDecoder(); var result = dec.encoding;");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("utf-8"));
        }

        [Test]
        public void TextDecoder_ExplicitEncoding()
        {
            var e = MakeEngine();
            e.Execute("var dec = new TextDecoder('ascii'); var result = dec.encoding;");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("ascii"));
        }

        [Test]
        public void TextDecoder_EmptyBuffer()
        {
            var e = MakeEngine();
            e.Execute(@"
                var dec = new TextDecoder();
                var result = dec.decode(undefined);
            ");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo(""));
        }

        [Test]
        public void TextEncoder_Encode_MultibyteChar()
        {
            var e = MakeEngine();
            e.Execute(@"
                var enc = new TextEncoder();
                var dec = new TextDecoder();
                var buf = enc.encode('é');
                var result = dec.decode(buf);
            ");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("é"));
        }
    }

    // =========================================================
    // stream tests
    // =========================================================

    [TestFixture]
    public class StreamTests
    {
        private static ScriptEngine MakeEngine()
        {
            var e = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(e);
            return e;
        }

        [Test]
        public void Readable_Push_Read()
        {
            var e = MakeEngine();
            e.Execute(@"
                var r = new stream.Readable();
                r.push('hello');
                r.push(' world');
                var result = r.read();
            ");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("hello world"));
        }

        [Test]
        public void Readable_OnData_FiredOnPush()
        {
            var e = MakeEngine();
            e.Execute(@"
                var r = new stream.Readable();
                var result = '';
                r.on('data', function(chunk) { result += chunk; });
                r.push('foo');
                r.push('bar');
            ");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("foobar"));
        }

        [Test]
        public void Readable_OnEnd_FiredWhenNullPushed()
        {
            var e = MakeEngine();
            e.Execute(@"
                var r = new stream.Readable();
                var ended = 0;
                r.on('end', function() { ended = 1; });
                r.push(null);
                var result = ended;
            ");
            Assert.That(e.Root.FindChild("result")?.Var?.Int, Is.EqualTo(1));
        }

        [Test]
        public void Writable_Write_GetBuffer()
        {
            var e = MakeEngine();
            e.Execute(@"
                var w = new stream.Writable();
                w.write('abc');
                w.write('def');
                var result = w.getBuffer();
            ");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("abcdef"));
        }

        [Test]
        public void Writable_End_FiresFinish()
        {
            var e = MakeEngine();
            e.Execute(@"
                var w = new stream.Writable();
                var finished = 0;
                w.on('finish', function() { finished = 1; });
                w.end('done');
                var result = finished;
            ");
            Assert.That(e.Root.FindChild("result")?.Var?.Int, Is.EqualTo(1));
        }

        [Test]
        public void Readable_Pipe_Writable()
        {
            var e = MakeEngine();
            e.Execute(@"
                var r = new stream.Readable();
                var w = new stream.Writable();
                r.push('hello');
                r.push(' piped');
                r.pipe(w);
                var result = w.getBuffer();
            ");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("hello piped"));
        }

        [Test]
        public void Transform_WriteAndRead()
        {
            var e = MakeEngine();
            e.Execute(@"
                var t = new stream.Transform();
                var collected = '';
                t.on('data', function(chunk) { collected += chunk; });
                t.write('x');
                t.write('y');
                var result = collected;
            ");
            Assert.That(e.Root.FindChild("result")?.Var?.String, Is.EqualTo("xy"));
        }
    }

    // =========================================================
    // net tests
    // =========================================================

    [TestFixture]
    public class NetTests
    {
        private static ScriptEngine MakeEngine()
        {
            var e = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(e);
            return e;
        }

        private static int FindFreePort()
        {
            using var t = new TcpListener(IPAddress.Loopback, 0);
            t.Start();
            var port = ((IPEndPoint)t.LocalEndpoint).Port;
            t.Stop();
            return port;
        }

        [Test]
        public void NetServer_AcceptsConnection_AndReceivesData()
        {
            var port = FindFreePort();
            string received = null;
            Exception serverEx = null;

            var serverTask = Task.Run(() =>
            {
                try
                {
                    var e = MakeEngine();
                    var script = $@"
                        var server = net.createServer(function(socket) {{
                            var data = socket.read();
                            socket.write('echo:' + data);
                            socket.end();
                            server.close();
                        }});
                        server.listen({port});
                    ";
                    e.Execute(script);
                }
                catch (Exception ex) { serverEx = ex; }
            });

            Thread.Sleep(200);

            // Connect a raw TCP client and send data
            using var client = new TcpClient("127.0.0.1", port);
            var stream = client.GetStream();
            var sendBytes = Encoding.UTF8.GetBytes("hello");
            stream.Write(sendBytes, 0, sendBytes.Length);

            Thread.Sleep(100);

            var buf = new byte[256];
            stream.ReadTimeout = 2000;
            var n = stream.Read(buf, 0, buf.Length);
            received = Encoding.UTF8.GetString(buf, 0, n);
            client.Close();

            serverTask.Wait(3000);

            Assert.That(serverEx, Is.Null, $"Server threw: {serverEx}");
            Assert.That(received, Is.EqualTo("echo:hello"));
        }

        [Test]
        public void NetServer_Close_StopsLoop()
        {
            var port = FindFreePort();
            var closedCleanly = false;
            Exception serverEx = null;

            var serverTask = Task.Run(() =>
            {
                try
                {
                    var e = MakeEngine();
                    var script = $@"
                        var server = net.createServer(function(socket) {{
                            server.close();
                        }});
                        server.listen({port});
                    ";
                    e.Execute(script);
                    closedCleanly = true;
                }
                catch (Exception ex) { serverEx = ex; }
            });

            Thread.Sleep(200);

            // Send a connection to trigger the handler
            try { using var c = new TcpClient("127.0.0.1", port); c.Close(); } catch { /* server may have closed */ }

            serverTask.Wait(3000);

            Assert.That(serverEx, Is.Null, $"Server threw: {serverEx}");
            Assert.That(closedCleanly, Is.True);
        }

        [Test]
        public void CreateConnection_ConnectsAndExchangesData()
        {
            var port = FindFreePort();
            Exception serverEx = null;

            // Start a plain TCP echo server using .NET (not DScript)
            var tcpServer = new TcpListener(IPAddress.Loopback, port);
            tcpServer.Start();
            var serverTask = Task.Run(() =>
            {
                try
                {
                    var s = tcpServer.AcceptTcpClient();
                    var ns = s.GetStream();
                    var b = new byte[256];
                    var n = ns.Read(b, 0, b.Length);
                    // Echo back
                    ns.Write(b, 0, n);
                    s.Close();
                    tcpServer.Stop();
                }
                catch (Exception ex) { serverEx = ex; }
            });

            Thread.Sleep(100);

            var e = MakeEngine();
            var script = $@"
                var sock = net.createConnection({port}, '127.0.0.1');
                sock.write('ping');
                // small delay to let echo arrive
                var i = 0;
                while (i < 1000000) {{ i++; }}
                var result = sock.read();
                sock.end();
            ";
            e.Execute(script);

            serverTask.Wait(3000);

            var result = e.Root.FindChild("result")?.Var?.String;
            Assert.That(serverEx, Is.Null, $"Echo server threw: {serverEx}");
            Assert.That(result, Is.EqualTo("ping"));
        }

        [Test]
        public void NetSocket_RemoteAddress_IsSet()
        {
            var port = FindFreePort();
            Exception serverEx = null;
            string remoteAddr = null;

            var serverTask = Task.Run(() =>
            {
                try
                {
                    var e = MakeEngine();
                    var script = $@"
                        var server = net.createServer(function(socket) {{
                            _remoteAddr = socket.remoteAddress;
                            server.close();
                        }});
                        server.listen({port});
                    ";
                    e.Execute(script);
                    remoteAddr = e.Root.FindChild("_remoteAddr")?.Var?.String;
                }
                catch (Exception ex) { serverEx = ex; }
            });

            Thread.Sleep(200);
            try { using var c = new TcpClient("127.0.0.1", port); c.Close(); } catch { /* ok */ }

            serverTask.Wait(3000);
            Assert.That(serverEx, Is.Null, $"Server threw: {serverEx}");
            Assert.That(remoteAddr, Is.Not.Null.And.Not.Empty);
        }
    }
}
