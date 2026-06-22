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
    /// Correctness tests for VM internals: the inline property cache (<c>GetProp</c>)
    /// and the call-frame allocation pool.
    /// </summary>
    public class VmInternalsTests
    {
        private static int RunInt(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r").Int;
        }

        // ── inline property cache ─────────────────────────────────────────────

        [Test]
        public void PropertyCache_ReturnsCurrentValueAfterMutation()
        {
            // Reading the same property twice with a write in between must return
            // the updated value, not a stale cached one.
            const string src =
                "var o = { x: 1 }; " +
                "var first = o.x; " +
                "o.x = 99; " +
                "var r = o.x;";
            Assert.That(RunInt(src), Is.EqualTo(99));
        }

        [Test]
        public void PropertyCache_TwoObjectsAtSameCallSite_IndependentValues()
        {
            // Two different objects at the same call site must not share a cache entry.
            const string src =
                "function make(v) { return { x: v }; } " +
                "var a = make(10); var b = make(20); " +
                "var r = a.x + b.x;";
            Assert.That(RunInt(src), Is.EqualTo(30));
        }

        [Test]
        public void PropertyCache_ShapeChangeTriggersMiss()
        {
            // Adding a new property bumps ShapeVersion; the cache must miss and
            // re-resolve so both the old and new properties are found correctly.
            const string src =
                "var o = { x: 1 }; " +
                "var before = o.x; " +
                "o.y = 2; " +
                "var r = o.x + o.y;";
            Assert.That(RunInt(src), Is.EqualTo(3));
        }

        // ── call-frame pool ───────────────────────────────────────────────────

        [Test]
        public void FramePool_PooledFramesDoNotLeakValues()
        {
            // Two successive calls to the same function must see their own locals,
            // not a stale value from a reused (pooled) frame.
            const string src =
                "function f(n) { var local = n * 2; return local; } " +
                "var a = f(3); " +
                "var r = f(7);";
            Assert.That(RunInt(src), Is.EqualTo(14));
        }

        [Test]
        public void FramePool_DeepRecursion_CorrectResult()
        {
            // Moderate-depth recursion exercises the pool borrow/return cycle.
            const string src =
                "function sum(n) { if (n <= 0) { return 0; } return n + sum(n - 1); } " +
                "var r = sum(50);";
            Assert.That(RunInt(src), Is.EqualTo(1275));
        }
    }
}
