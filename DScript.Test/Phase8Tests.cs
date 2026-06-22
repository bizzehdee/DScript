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
    /// <summary>Tests for Phase 8: async/await and Promise.</summary>
    public class Phase8Tests
    {
        private static ScriptEngine RunEngine(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            var vm = new VirtualMachine(engine);
            vm.Run(chunk, new Vm.Environment(engine.Root, null));
            engine.DrainMicroTasks();
            return engine;
        }

        private static int RunInt(string source)
        {
            var engine = RunEngine(source);
            return engine.Root.GetParameter("r").Int;
        }

        private static string RunStr(string source)
        {
            var engine = RunEngine(source);
            return engine.Root.GetParameter("r").String;
        }

        private static bool RunBool(string source)
        {
            var engine = RunEngine(source);
            return engine.Root.GetParameter("r").Bool;
        }

        // ---- 1. Promise.resolve --------------------------------------------

        [Test]
        public void Promise_Resolve_CreatesFulfilledPromise()
        {
            var src = @"
                var p = Promise.resolve(42);
                var r = 0;
                p.then(function(v) { r = v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(42));
        }

        [Test]
        public void Promise_Reject_CreatesRejectedPromise()
        {
            var src = @"
                var p = Promise.reject(99);
                var r = 0;
                p.catch(function(v) { r = v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(99));
        }

        // ---- 2. Promise constructor ----------------------------------------

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

        // ---- 3. async function returning a value --------------------------

        [Test]
        public void AsyncFunction_Declaration_ReturnsPromise()
        {
            var src = @"
                async function double(x) { return x * 2; }
                var p = double(21);
                var r = 0;
                p.then(function(v) { r = v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(42));
        }

        [Test]
        public void AsyncFunction_Expression_ReturnsPromise()
        {
            var src = @"
                var triple = async function(x) { return x * 3; };
                var p = triple(7);
                var r = 0;
                p.then(function(v) { r = v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(21));
        }

        // ---- 4. await expression ------------------------------------------

        [Test]
        public void Await_ResolvedPromise_ReturnsValue()
        {
            var src = @"
                async function test() {
                    var v = await Promise.resolve(10);
                    return v + 5;
                }
                var r = 0;
                test().then(function(v) { r = v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(15));
        }

        [Test]
        public void Await_PlainValue_ReturnsValue()
        {
            var src = @"
                async function test() {
                    var v = await 100;
                    return v;
                }
                var r = 0;
                test().then(function(v) { r = v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(100));
        }

        // ---- 5. chained awaits --------------------------------------------

        [Test]
        public void Await_ChainedAwaits_AccumulatesCorrectly()
        {
            var src = @"
                async function chain() {
                    var a = await Promise.resolve(1);
                    var b = await Promise.resolve(2);
                    var c = await Promise.resolve(3);
                    return a + b + c;
                }
                var r = 0;
                chain().then(function(v) { r = v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(6));
        }

        // ---- 6. async with conditional ------------------------------------

        [Test]
        public void AsyncFunction_WithCondition_CorrectBranch()
        {
            var src = @"
                async function pick(x) {
                    if (x > 0) {
                        return await Promise.resolve(1);
                    } else {
                        return await Promise.resolve(-1);
                    }
                }
                var r = 0;
                pick(5).then(function(v) { r = v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(1));
        }

        // ---- 7. Promise .then chaining ------------------------------------

        [Test]
        public void Promise_Then_ChainsValue()
        {
            var src = @"
                var r = 0;
                var p = Promise.resolve(2);
                p.then(function(v) { r = v * 10; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(20));
        }

        // ---- 8. async function returning promise directly -----------------

        [Test]
        public void AsyncFunction_ReturnsPromiseDirectly_Resolves()
        {
            var src = @"
                async function wrapper() {
                    return 77;
                }
                var r = 0;
                var p = wrapper();
                p.then(function(v) { r = v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(77));
        }

        // ---- 9. multiple async calls --------------------------------------

        [Test]
        public void AsyncFunction_MultipleCalls_IndependentPromises()
        {
            var src = @"
                async function inc(x) { return x + 1; }
                var r = 0;
                inc(10).then(function(v) { r = r + v; });
                inc(20).then(function(v) { r = r + v; });
            ";
            // After drain: r = 11 + 21 = 32
            Assert.That(RunInt(src), Is.EqualTo(32));
        }

        // ---- 10. await in loop body ---------------------------------------

        [Test]
        public void AsyncFunction_AwaitInLoop_AccumulatesSum()
        {
            var src = @"
                async function sumUp(n) {
                    var total = 0;
                    var i = 0;
                    while (i < n) {
                        var v = await Promise.resolve(i);
                        total = total + v;
                        i = i + 1;
                    }
                    return total;
                }
                var r = 0;
                sumUp(5).then(function(v) { r = v; });
            ";
            // 0+1+2+3+4 = 10
            Assert.That(RunInt(src), Is.EqualTo(10));
        }

        // ---- 11. async with no return ------------------------------------

        [Test]
        public void AsyncFunction_NoReturn_ResolvesUndefined()
        {
            var src = @"
                async function noop() { }
                var r = 99;
                noop().then(function(v) {
                    if (v === undefined) { r = 1; } else { r = 0; }
                });
            ";
            Assert.That(RunInt(src), Is.EqualTo(1));
        }

        // ---- 12. Promise.resolve then chained with await ------------------

        [Test]
        public void AsyncFunction_AwaitNestedAsync_CorrectResult()
        {
            var src = @"
                async function inner(x) { return x * 2; }
                async function outer(x) {
                    var v = await inner(x);
                    return v + 1;
                }
                var r = 0;
                outer(5).then(function(v) { r = v; });
            ";
            // inner(5) = 10, outer = 11
            Assert.That(RunInt(src), Is.EqualTo(11));
        }
    }
}
