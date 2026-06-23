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

using DScript;
using DScript.Extras;
using DScript.Extras.FunctionProviders;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class ProcessHooksTests
    {
        private static ScriptEngine MakeEngine()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            return engine;
        }

        [Test]
        public void Process_On_RegistersHandler()
        {
            var engine = MakeEngine();
            engine.Execute("process.on('exit', function(code) {}); ");
            // Verify the handler is stored
            var handlerArr = engine.Root.FindChild("__process_on_exit__");
            Assert.That(handlerArr, Is.Not.Null);
            Assert.That(handlerArr.Var.GetArrayLength(), Is.EqualTo(1));
        }

        [Test]
        public void Process_On_MultipleHandlers_AllRegistered()
        {
            var engine = MakeEngine();
            engine.Execute(
                "process.on('exit', function(c) {}); " +
                "process.on('exit', function(c) {});");
            var handlerArr = engine.Root.FindChild("__process_on_exit__");
            Assert.That(handlerArr, Is.Not.Null);
            Assert.That(handlerArr.Var.GetArrayLength(), Is.EqualTo(2));
        }

        [Test]
        public void Process_Emit_CallsRegisteredHandler()
        {
            var engine = MakeEngine();
            engine.Execute(
                "var called = 0; " +
                "process.on('exit', function(code) { called = called + 1; }); " +
                "process.emit('exit', 0);");
            Assert.That(engine.Root.GetParameter("called").Int, Is.EqualTo(1));
        }

        [Test]
        public void Process_Emit_PassesArgumentToHandler()
        {
            var engine = MakeEngine();
            engine.Execute(
                "var received = -1; " +
                "process.on('exit', function(code) { received = code; }); " +
                "process.emit('exit', 42);");
            Assert.That(engine.Root.GetParameter("received").Int, Is.EqualTo(42));
        }

        [Test]
        public void Process_Off_RemovesHandlers()
        {
            var engine = MakeEngine();
            engine.Execute(
                "var called = 0; " +
                "process.on('exit', function(c) { called = called + 1; }); " +
                "process.off('exit'); " +
                "process.emit('exit', 0);");
            Assert.That(engine.Root.GetParameter("called").Int, Is.EqualTo(0));
        }

        [Test]
        public void Process_On_UncaughtException_RegistersHandler()
        {
            var engine = MakeEngine();
            engine.Execute("process.on('uncaughtException', function(err) {});");
            var handlerArr = engine.Root.FindChild("__process_on_uncaughtException__");
            Assert.That(handlerArr, Is.Not.Null);
        }

        [Test]
        public void Process_On_UnhandledRejection_RegistersHandler()
        {
            var engine = MakeEngine();
            engine.Execute("process.on('unhandledRejection', function(reason, p) {});");
            var handlerArr = engine.Root.FindChild("__process_on_unhandledRejection__");
            Assert.That(handlerArr, Is.Not.Null);
        }

        [Test]
        public void Process_DispatchEvent_CSharpApi_CallsHandlers()
        {
            var engine = MakeEngine();
            engine.Execute("var result = ''; process.on('custom', function(x) { result = x; });");
            ProcessFunctionProvider.DispatchEvent(engine, "custom", new ScriptVar("hello"));
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("hello"));
        }

        [Test]
        public void Process_Emit_NoHandlers_DoesNotThrow()
        {
            var engine = MakeEngine();
            Assert.DoesNotThrow(() => engine.Execute("process.emit('nohandler', 0);"));
        }
    }
}
