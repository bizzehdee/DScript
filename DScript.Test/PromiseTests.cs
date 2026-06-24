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
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    /// <summary>Tests for the built-in <c>Promise</c> type: resolve, reject, constructor, and <c>.then</c>/<c>.catch</c>.</summary>
    public class PromiseTests
    {
        private static int RunInt(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            ScriptEngine.DrainMicroTasks();
            return engine.Root.GetParameter("r").Int;
        }

        [Test]
        public void Promise_Resolve_InvokesThenCallback()
        {
            var src = @"
                var p = Promise.resolve(42);
                var r = 0;
                p.then(function(v) { r = v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(42));
        }

        [Test]
        public void Promise_Reject_InvokesCatchCallback()
        {
            var src = @"
                var p = Promise.reject(99);
                var r = 0;
                p.catch(function(v) { r = v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(99));
        }

        [Test]
        public void Promise_Constructor_ResolvesViaCallback()
        {
            var src = @"
                var r = 0;
                var p = new Promise(function(resolve, reject) { resolve(7); });
                p.then(function(v) { r = v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(7));
        }

        [Test]
        public void Promise_Constructor_RejectsViaCallback()
        {
            var src = @"
                var r = 0;
                var p = new Promise(function(resolve, reject) { reject(5); });
                p.catch(function(v) { r = v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(5));
        }

        [Test]
        public void Promise_Then_ReceivesMultipliedValue()
        {
            var src = @"
                var r = 0;
                var p = Promise.resolve(2);
                p.then(function(v) { r = v * 10; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(20));
        }

        // ── Promise.withResolvers ──────────────────────────────────────────────

        [Test]
        public void WithResolvers_ReturnedObjectHasAllThreeKeys()
        {
            var engine = new ScriptEngine();
            engine.Run(ScriptEngine.Compile("var wr = Promise.withResolvers();"));
            ScriptEngine.DrainMicroTasks();
            var wr = engine.Root.GetParameter("wr");
            Assert.That(wr.FindChild("promise"), Is.Not.Null);
            Assert.That(wr.FindChild("resolve"), Is.Not.Null);
            Assert.That(wr.FindChild("reject"), Is.Not.Null);
        }

        [Test]
        public void WithResolvers_CallingResolveFulfillsPromise()
        {
            var src = @"
                var r = 0;
                var wr = Promise.withResolvers();
                wr.promise.then(function(v) { r = v; });
                wr.resolve(42);
            ";
            Assert.That(RunInt(src), Is.EqualTo(42));
        }

        [Test]
        public void WithResolvers_CallingRejectRejectsPromise()
        {
            var src = @"
                var r = 0;
                var wr = Promise.withResolvers();
                wr.promise.catch(function(v) { r = v; });
                wr.reject(99);
            ";
            Assert.That(RunInt(src), Is.EqualTo(99));
        }

        [Test]
        public void WithResolvers_SecondResolveIsNoOp()
        {
            var src = @"
                var r = 0;
                var wr = Promise.withResolvers();
                wr.promise.then(function(v) { r = v; });
                wr.resolve(1);
                wr.resolve(2);
            ";
            Assert.That(RunInt(src), Is.EqualTo(1));
        }
    }
}
