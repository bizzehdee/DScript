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
    /// <summary>Tests for <c>function*</c> generator syntax, <c>yield</c>, the iterator protocol, and nested generators.</summary>
    public class GeneratorTests
    {
        private static int RunInt(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r").Int;
        }

        private static string RunStr(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r").String;
        }

        [Test]
        public void Generator_FunctionDeclaration_FirstYield()
        {
            var src = @"
                function* gen() { yield 1; }
                var it = gen();
                var r = it.next().value;
            ";
            Assert.That(RunInt(src), Is.EqualTo(1));
        }

        [Test]
        public void Generator_FunctionExpression_YieldsValue()
        {
            var src = @"
                var gen = function*() { yield 42; };
                var it = gen();
                var r = it.next().value;
            ";
            Assert.That(RunInt(src), Is.EqualTo(42));
        }

        [Test]
        public void Generator_MultipleYields_ProducesCorrectSequence()
        {
            var src = @"
                function* gen() { yield 10; yield 20; yield 30; }
                var it = gen();
                var a = it.next().value;
                var b = it.next().value;
                var c = it.next().value;
                var r = a + b + c;
            ";
            Assert.That(RunInt(src), Is.EqualTo(60));
        }

        [Test]
        public void Generator_DoneFlag_FalseWhileRunning_TrueAfterExhausted()
        {
            var src = @"
                function* gen() { yield 1; }
                var it = gen();
                var r1 = it.next();
                var r2 = it.next();
                var r = '';
                if (!r1.done) r += 'ok1';
                if (r2.done)  r += 'ok2';
            ";
            Assert.That(RunStr(src), Is.EqualTo("ok1ok2"));
        }

        [Test]
        public void Generator_YieldExpression_ComputedValue()
        {
            var src = @"
                function* counter(start) {
                    var n = start;
                    yield n;
                    yield n + 1;
                    yield n + 2;
                }
                var it = counter(5);
                var r = it.next().value + it.next().value + it.next().value;
            ";
            Assert.That(RunInt(src), Is.EqualTo(18)); // 5+6+7
        }

        [Test]
        public void Generator_ExplicitReturn_SetsDoneTrue()
        {
            var src = @"
                function* gen() { yield 1; return 99; }
                var it = gen();
                it.next();
                var res = it.next();
                var r = '';
                if (res.done) r += 'done';
            ";
            Assert.That(RunStr(src), Is.EqualTo("done"));
        }

        [Test]
        public void Generator_Nested_OuterConsumesInner()
        {
            var src = @"
                function* inner() { yield 1; yield 2; }
                function* outer() {
                    var it = inner();
                    var res;
                    res = it.next();
                    while (!res.done) {
                        yield res.value * 10;
                        res = it.next();
                    }
                }
                var r = 0;
                for (var v of outer()) { r += v; }
            ";
            Assert.That(RunInt(src), Is.EqualTo(30)); // 10+20
        }

        [Test]
        public void Generator_ExhaustedCallsNextAgain_ReturnsDone()
        {
            // Calling .next() after the generator is exhausted must keep returning {done: true}.
            var src = @"
                function* gen() { yield 1; }
                var it = gen();
                it.next();   // exhausts generator (yield 1)
                it.next();   // one past end -> done=true
                var res = it.next();
                var r = '';
                if (res.done) r += 'done';
            ";
            Assert.That(RunStr(src), Is.EqualTo("done"));
        }

        [Test]
        public void Generator_ThrowsInBody_PropagatesAsScriptException()
        {
            // An unhandled error inside the generator body should propagate to
            // the caller as a ScriptException (via GeneratorObject.Next error path).
            var src = @"
                function* bad() { throw 'boom'; yield 0; }
                var it = bad();
                try {
                    it.next();
                    var r = 'nothrown';
                } catch(e) {
                    var r = 'caught';
                }
            ";
            Assert.That(RunStr(src), Is.EqualTo("caught"));
        }
    }
}
