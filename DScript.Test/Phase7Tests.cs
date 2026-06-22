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
    /// <summary>Tests for Phase 7: generators and iterators.</summary>
    public class Phase7Tests
    {
        private static ScriptVar Run(string source, ScriptEngine engine = null)
        {
            engine ??= new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            var vm = new VirtualMachine(engine);
            vm.Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root;
        }

        private static int RunInt(string source)
        {
            var engine = new ScriptEngine();
            Run(source, engine);
            return engine.Root.GetParameter("r").Int;
        }

        private static string RunStr(string source)
        {
            var engine = new ScriptEngine();
            Run(source, engine);
            return engine.Root.GetParameter("r").String;
        }

        // ---- 1. Generator function declaration --------------------------------

        [Test]
        public void Generator_FunctionDeclaration_ReturnsIteratorObject()
        {
            var src = @"
                function* gen() { yield 1; }
                var it = gen();
                var r = it.next().value;
            ";
            Assert.That(RunInt(src), Is.EqualTo(1));
        }

        [Test]
        public void Generator_FunctionExpression_ReturnsIteratorObject()
        {
            var src = @"
                var gen = function*() { yield 42; };
                var it = gen();
                var r = it.next().value;
            ";
            Assert.That(RunInt(src), Is.EqualTo(42));
        }

        // ---- 2. Multiple yields -----------------------------------------------

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

        // ---- 3. done flag -----------------------------------------------------

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

        // ---- 4. Yield with expression -----------------------------------------

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

        // ---- 5. for...of over array ------------------------------------------

        [Test]
        public void ForOf_Array_IteratesAllElements()
        {
            var src = @"
                var r = 0;
                for (var x of [1, 2, 3, 4, 5]) { r += x; }
            ";
            Assert.That(RunInt(src), Is.EqualTo(15));
        }

        [Test]
        public void ForOf_Array_CollectsIntoString()
        {
            var src = @"
                var r = '';
                for (var s of ['a', 'b', 'c']) { r += s; }
            ";
            Assert.That(RunStr(src), Is.EqualTo("abc"));
        }

        // ---- 6. for...of over generator --------------------------------------

        [Test]
        public void ForOf_Generator_IteratesYieldedValues()
        {
            var src = @"
                function* range(n) {
                    var i = 0;
                    while (i < n) {
                        yield i;
                        i++;
                    }
                }
                var r = 0;
                for (var v of range(5)) { r += v; }
            ";
            Assert.That(RunInt(src), Is.EqualTo(10)); // 0+1+2+3+4
        }

        // ---- 7. Generator with return value ----------------------------------

        [Test]
        public void Generator_ExplicitReturn_SetsDoneAfterReturn()
        {
            var src = @"
                function* gen() { yield 1; return 99; }
                var it = gen();
                it.next();          // consume yield 1
                var res = it.next(); // hits return 99
                var r = '';
                if (res.done) r += 'done';
            ";
            Assert.That(RunStr(src), Is.EqualTo("done"));
        }

        // ---- 8. for...of break -----------------------------------------------

        [Test]
        public void ForOf_Break_StopsIteration()
        {
            var src = @"
                var r = 0;
                for (var x of [10, 20, 30, 40, 50]) {
                    r++;
                    if (x === 30) break;
                }
            ";
            Assert.That(RunInt(src), Is.EqualTo(3));
        }

        // ---- 9. for...of continue --------------------------------------------

        [Test]
        public void ForOf_Continue_SkipsRestOfBody()
        {
            var src = @"
                var r = 0;
                for (var x of [1, 2, 3, 4, 5]) {
                    if (x === 3) continue;
                    r += x;
                }
            ";
            Assert.That(RunInt(src), Is.EqualTo(12)); // 1+2+4+5 = 12
        }

        // ---- 10. Nested generators -------------------------------------------

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
    }
}
