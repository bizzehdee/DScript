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

        // ── superinstruction fusion ───────────────────────────────────────────

        [Test]
        public void SetVarPop_AssignmentStatementDiscardedCorrectly()
        {
            // SetVar + Pop fused → SetVarPop/SetVarPopN.
            // The assignment result is not left on the stack; subsequent reads are correct.
            const string src =
                "var x = 0; x = 42; var r = x;";
            Assert.That(RunInt(src), Is.EqualTo(42));
        }

        [Test]
        public void SetVarPop_MultipleSequentialAssignments()
        {
            // Successive fused assignments must each store their own values.
            const string src =
                "var a = 1; var b = 2; a = 10; b = 20; var r = a + b;";
            Assert.That(RunInt(src), Is.EqualTo(30));
        }

        [Test]
        public void SetPropPop_PropertySetStatementDiscardedCorrectly()
        {
            // SetProp + Pop fused → SetPropPop/SetPropPopN.
            const string src =
                "var o = {}; o.x = 99; var r = o.x;";
            Assert.That(RunInt(src), Is.EqualTo(99));
        }

        [Test]
        public void SetPropPop_MultiplePropertiesOnSameObject()
        {
            // Multiple property-set fusions on the same object must not clobber each other.
            const string src =
                "var o = {}; o.a = 3; o.b = 7; var r = o.a + o.b;";
            Assert.That(RunInt(src), Is.EqualTo(10));
        }

        [Test]
        public void GetVarGetProp_SimplePropertyRead()
        {
            // GetVar + GetProp fused → GetVarGetProp/GetVarGetPropN.
            const string src =
                "var o = { x: 55 }; var r = o.x;";
            Assert.That(RunInt(src), Is.EqualTo(55));
        }

        [Test]
        public void GetVarGetProp_ReadAfterWrite()
        {
            // Property cache must reflect the written value, not a stale fused read.
            const string src =
                "var o = { x: 1 }; o.x = 77; var r = o.x;";
            Assert.That(RunInt(src), Is.EqualTo(77));
        }

        [Test]
        public void SuperInstructions_DisassemblyContainsFusedOpcodes()
        {
            // Verify that the optimizer actually emits the fused narrow forms by
            // inspecting the disassembly of a program that contains all three patterns.
            const string src =
                "var x = 1; " +        // SetVarPopN (DeclareVar + assign)
                "var o = {}; " +       // SetVarPopN
                "o.p = 2; " +          // SetPropPopN
                "var r = o.p;";        // GetVarGetPropN

            var compiler = new DScriptCompiler { EnableOptimizer = true };
            var chunk = compiler.CompileProgram(src);
            var asm = Disassembler.Disassemble(chunk);

            Assert.That(asm, Does.Contain("SetVarPopN"),  "Expected SetVarPopN in disassembly");
            Assert.That(asm, Does.Contain("SetPropPopN"), "Expected SetPropPopN in disassembly");
            Assert.That(asm, Does.Contain("GetVarGetPropN"), "Expected GetVarGetPropN in disassembly");
        }

        [Test]
        public void SuperInstructions_OptimizerDisabled_StillCorrect()
        {
            // With the optimizer off, unfused forms must still produce correct results.
            const string src =
                "var x = 5; x = 10; " +
                "var o = { y: 3 }; o.y = 7; " +
                "var r = o.y + x;";

            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler { EnableOptimizer = false }.CompileProgram(src);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            Assert.That(engine.Root.GetParameter("r").Int, Is.EqualTo(17));
        }

        // ── GetPropMethod / GetPropCall0 ──────────────────────────────────────

        [Test]
        public void GetPropMethod_NamedMethodCallWithArgs_CorrectResult()
        {
            // obj.add(x) — compiler emits GetPropMethod + CallMethod 1
            const string src =
                "var o = { add: function(x) { return x + 10; } }; " +
                "var r = o.add(5);";
            Assert.That(RunInt(src), Is.EqualTo(15));
        }

        [Test]
        public void GetPropCall0_ZeroArgMethodCall_CorrectResult()
        {
            // obj.get() — compiler emits GetPropCall0N (no separate CallMethod)
            const string src =
                "var v = 42; " +
                "var o = { get: function() { return v; } }; " +
                "var r = o.get();";
            Assert.That(RunInt(src), Is.EqualTo(42));
        }

        [Test]
        public void GetPropCall0_ChainedZeroArgCalls_CorrectResult()
        {
            // o.first().second() — two consecutive 0-arg method calls
            const string src =
                "var o = { first: function() { return o; }, second: function() { return 99; } }; " +
                "var r = o.first().second();";
            Assert.That(RunInt(src), Is.EqualTo(99));
        }

        [Test]
        public void GetPropMethod_HotLoop_AccumulatesCorrectly()
        {
            // Exercises the path 1000 times to catch any caching or state-mutation issue.
            const string src =
                "var sum = 0; " +
                "var o = { inc: function(n) { return n + 1; } }; " +
                "for (var i = 0; i < 1000; i = i + 1) { sum = o.inc(sum); } " +
                "var r = sum;";
            Assert.That(RunInt(src), Is.EqualTo(1000));
        }

        // ── GetVarGetVarBinary ────────────────────────────────────────────────

        [Test]
        public void GetVarGetVarBinary_Addition_CorrectResult()
        {
            const string src = "var a = 3; var b = 4; var r = a + b;";
            Assert.That(RunInt(src), Is.EqualTo(7));
        }

        [Test]
        public void GetVarGetVarBinary_Subtraction_CorrectResult()
        {
            const string src = "var a = 10; var b = 3; var r = a - b;";
            Assert.That(RunInt(src), Is.EqualTo(7));
        }

        [Test]
        public void GetVarGetVarBinary_LessThan_CorrectResult()
        {
            const string src = "var a = 3; var b = 5; var r = (a < b) ? 1 : 0;";
            Assert.That(RunInt(src), Is.EqualTo(1));
        }

        [Test]
        public void GetVarGetVarBinary_HotLoop_Accumulates()
        {
            // sum += i triggers GetVarGetVarBinaryN in the hot path
            const string src =
                "var sum = 0; " +
                "for (var i = 0; i < 1000; i = i + 1) { sum = sum + i; } " +
                "var r = sum;";
            Assert.That(RunInt(src), Is.EqualTo(499500));
        }

        [Test]
        public void GetVarGetVarBinary_DisassemblyContainsFusedOpcode()
        {
            const string src = "var a = 1; var b = 2; var r = a + b;";
            var compiler = new DScriptCompiler { EnableOptimizer = true };
            var chunk = compiler.CompileProgram(src);
            var asm = Disassembler.Disassemble(chunk);
            Assert.That(asm, Does.Contain("GetVarGetVarBinaryN"), "Expected GetVarGetVarBinaryN in disassembly");
        }

        [Test]
        public void GetPropCall0_DisassemblyContainsFusedOpcodes()
        {
            // Verify that the narrow form GetPropCall0N and GetPropMethodN appear
            // in the disassembly for the expected call patterns.
            const string src =
                "var o = { get: function() { return 1; }, add: function(x) { return x; } }; " +
                "var a = o.get(); " +    // 0-arg → GetPropCall0N
                "var r = o.add(1);";     // 1-arg → GetPropMethodN + CallMethod

            var compiler = new DScriptCompiler { EnableOptimizer = true };
            var chunk = compiler.CompileProgram(src);
            var asm = Disassembler.Disassemble(chunk);

            Assert.That(asm, Does.Contain("GetPropCall0N"),  "Expected GetPropCall0N in disassembly");
            Assert.That(asm, Does.Contain("GetPropMethodN"), "Expected GetPropMethodN in disassembly");
        }
    }
}
