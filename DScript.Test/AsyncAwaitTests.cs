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
    /// <summary>Tests for <c>async function</c> declarations/expressions and <c>await</c> expressions.</summary>
    public class AsyncAwaitTests
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
        public void AsyncFunction_Declaration_ReturnsPromiseThatResolvesToValue()
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
        public void Await_PlainValue_TreatedAsResolved()
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

        [Test]
        public void AsyncFunction_WithConditional_CorrectBranchSelected()
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

        [Test]
        public void AsyncFunction_MultipleCalls_IndependentPromises()
        {
            var src = @"
                async function inc(x) { return x + 1; }
                var r = 0;
                inc(10).then(function(v) { r = r + v; });
                inc(20).then(function(v) { r = r + v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(32)); // 11 + 21
        }

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
            Assert.That(RunInt(src), Is.EqualTo(10)); // 0+1+2+3+4
        }

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

        [Test]
        public void AsyncFunction_DirectReturn_ResolvesWithLiteral()
        {
            var src = @"
                async function wrapper() { return 77; }
                var r = 0;
                var p = wrapper();
                p.then(function(v) { r = v; });
            ";
            Assert.That(RunInt(src), Is.EqualTo(77));
        }

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
            Assert.That(RunInt(src), Is.EqualTo(11)); // inner(5)=10, outer=11
        }
    }
}
