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
    /// <summary>
    /// Tests for <c>async function*</c> (T10) and <c>for await...of</c> (T09).
    /// </summary>
    public class AsyncGeneratorTests
    {
        private static int RunInt(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            ScriptEngine.DrainMicroTasks();
            return engine.Root.GetParameter("r").Int;
        }

        private static string RunStr(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            ScriptEngine.DrainMicroTasks();
            return engine.Root.GetParameter("r").String;
        }

        // ── async function* (T10) ───────────────────────────────────────────

        [Test]
        public void AsyncGenerator_Declaration_NextReturnsPromise()
        {
            var src = @"
                async function* gen() { yield 42; }
                var ag = gen();
                var r = 0;
                ag.next().then(function(v) { r = v.value; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(42));
        }

        [Test]
        public void AsyncGenerator_Done_IsTrueWhenExhausted()
        {
            // Verify exhaustion by counting how many times the loop body runs
            // inside a for-await-of: if done never becomes true the loop never exits.
            var src = @"
                async function* gen() { yield 10; yield 20; }
                async function run() {
                    var count = 0;
                    for await (var v of gen()) { count++; }
                    return count;
                }
                var r = 0;
                run().then(function(v) { r = v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(2));
        }

        [Test]
        public void AsyncGenerator_Expression_Works()
        {
            var src = @"
                var gen = async function*() { yield 7; };
                var ag = gen();
                var r = 0;
                ag.next().then(function(v) { r = v.value; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(7));
        }

        [Test]
        public void AsyncGenerator_EmptyBody_DoneImmediately()
        {
            var src = @"
                async function* empty() {}
                var ag = empty();
                var r = '';
                ag.next().then(function(v) { r = v.done ? 'done' : 'more'; });
            ";
            Assert.That(RunStr(src), Is.EqualTo("done"));
        }

        // ── for await...of (T09) ───────────────────────────────────────────

        [Test]
        public void ForAwaitOf_AsyncGenerator_SumsYieldedValues()
        {
            var src = @"
                async function* nums() { yield 1; yield 2; yield 3; }
                async function run() {
                    var total = 0;
                    for await (var v of nums()) { total += v; }
                    return total;
                }
                var r = 0;
                run().then(function(v) { r = v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(6));
        }

        [Test]
        public void ForAwaitOf_Array_SumsAllElements()
        {
            var src = @"
                async function run() {
                    var total = 0;
                    for await (var v of [10, 20, 30]) { total += v; }
                    return total;
                }
                var r = 0;
                run().then(function(v) { r = v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(60));
        }

        [Test]
        public void ForAwaitOf_Break_StopsIteration()
        {
            var src = @"
                async function* nums() { yield 1; yield 2; yield 3; yield 4; yield 5; }
                async function run() {
                    var total = 0;
                    for await (var v of nums()) {
                        if (v === 3) break;
                        total += v;
                    }
                    return total;
                }
                var r = 0;
                run().then(function(v) { r = v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(3)); // 1 + 2
        }

        [Test]
        public void ForAwaitOf_InsideNonAsyncFunction_ThrowsException()
        {
            // for await...of inside a regular (non-async) function has no async drive
            // loop, so the VM throws when ForAwaitOfStep finds genObj == null.
            var src = @"function notAsync() { for await (var v of [1, 2, 3]) {} } notAsync();";
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(src);
            Assert.Throws<ScriptException>(() =>
                new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null)));
        }
    }
}
