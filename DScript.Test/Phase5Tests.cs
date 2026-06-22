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
    /// Smoke tests for Phase 5: call-frame pool, tail-call elimination, and
    /// inline property cache.
    /// </summary>
    public class Phase5Tests
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

        // ── Tail-call correctness ─────────────────────────────────────────────

        [Test]
        public void TailCallDirectRecursionIsCorrect()
        {
            // Tail-recursive countdown: each call is a direct tail call.
            // Verifies that the TailCall opcode returns the correct value.
            const string src =
                "function count(n, acc) { if (n == 0) { return acc; } return count(n - 1, acc + 1); } " +
                "var r = count(100, 0);";
            Assert.That(RunInt(src), Is.EqualTo(100));
        }

        [Test]
        public void TailCallWithAccumulatorIsCorrect()
        {
            // Tail-recursive factorial via accumulator — classic TCO pattern.
            const string src =
                "function fact(n, acc) { if (n <= 1) { return acc; } return fact(n - 1, n * acc); } " +
                "var r = fact(10, 1);";
            Assert.That(RunInt(src), Is.EqualTo(3628800));
        }

        [Test]
        public void NonTailCallExpressionStillWorksCorrectly()
        {
            // return n * fact(n-1) is NOT a tail call (the multiplication follows).
            // Verifies the peephole does not misidentify it.
            const string src =
                "function fact(n) { if (n <= 1) { return 1; } return n * fact(n - 1); } " +
                "var r = fact(7);";
            Assert.That(RunInt(src), Is.EqualTo(5040));
        }

        [Test]
        public void TailMethodCallIsCorrect()
        {
            // return this.helper() is a tail method call — verifies TailCallMethod.
            const string src =
                "function Obj() { this.val = 42; } " +
                "Obj.getVal = function() { return this.val; }; " +
                "Obj.run = function() { return this.getVal(); }; " +
                "var o = new Obj(); var r = o.run();";
            Assert.That(RunInt(src), Is.EqualTo(42));
        }

        [Test]
        public void TailCallInsideTryBlockIsNotApplied()
        {
            // A return inside a try-with-finally must NOT become a TailCall,
            // because the finally block still needs to run after the return.
            const string src =
                "var flag = 0; " +
                "function f(n) { try { return n + 1; } finally { flag = 1; } } " +
                "var r = f(5);";
            var root = Run(src);
            Assert.That(root.GetParameter("r").Int, Is.EqualTo(6));
            Assert.That(root.GetParameter("flag").Int, Is.EqualTo(1));
        }

        // ── Property cache correctness ────────────────────────────────────────

        [Test]
        public void PropertyCacheReturnsCurrentValueAfterMutation()
        {
            // Reading the same property twice, with a write in between, must return
            // the updated value — not a stale cached one.
            const string src =
                "var o = { x: 1 }; " +
                "var first = o.x; " +
                "o.x = 99; " +
                "var r = o.x;";
            Assert.That(RunInt(src), Is.EqualTo(99));
        }

        [Test]
        public void PropertyCacheWorksAcrossMultipleObjects()
        {
            // Two different objects at the same call site must not share a cache
            // entry — each lookup must see the correct object's property value.
            const string src =
                "function make(v) { return { x: v }; } " +
                "var a = make(10); var b = make(20); " +
                "var r = a.x + b.x;";
            Assert.That(RunInt(src), Is.EqualTo(30));
        }

        [Test]
        public void PropertyCacheHandlesAddedProperties()
        {
            // After a new property is added (shape changes), the cache must miss
            // and re-resolve so the new property is found on the next read.
            const string src =
                "var o = { x: 1 }; " +
                "var before = o.x; " +
                "o.y = 2; " +          // structural change bumps ShapeVersion
                "var r = o.x + o.y;";
            Assert.That(RunInt(src), Is.EqualTo(3));
        }

        // ── Frame pool correctness ────────────────────────────────────────────

        [Test]
        public void PooledFramesDoNotLeakValuesAcrossCalls()
        {
            // Two successive calls to the same function must see their own local
            // bindings and not a stale value from a pooled (reused) frame.
            const string src =
                "function f(n) { var local = n * 2; return local; } " +
                "var a = f(3); " +
                "var r = f(7);";
            Assert.That(RunInt(src), Is.EqualTo(14));
        }

        [Test]
        public void DeepRecursionWithPoolReturnsCorrectResult()
        {
            // Moderate-depth recursion exercises the pool borrow/return cycle
            // and verifies correctness is preserved under frame reuse.
            const string src =
                "function sum(n) { if (n <= 0) { return 0; } return n + sum(n - 1); } " +
                "var r = sum(50);";
            Assert.That(RunInt(src), Is.EqualTo(1275));
        }
    }
}
