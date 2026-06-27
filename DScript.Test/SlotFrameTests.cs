using NUnit.Framework;
using DScript;
using DScript.Compiler;
using DScript.Jit;
using DScript.Vm;

namespace DScript.Test
{
    /// <summary>
    /// Lever A positional local slots (AOT/closure-only, gated by
    /// <see cref="ScriptEngine.EnableLocalSlots"/>). When enabled, the compiler rewrites
    /// fully-slottable <c>let</c>/<c>var</c> locals to GetLocal/SetLocal; the
    /// interpreter and the closure-threaded JIT read them, and the Reflection.Emit
    /// back-end declines slotted chunks. Every slotted run must match the name-based
    /// interpreter baseline.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class SlotFrameTests
    {
        [TearDown]
        public void Reset()
        {
            JitRegistry.Clear();
            ScriptEngine.EnableLocalSlots = false;
        }

        private static string Run(string src, bool slots, IJitCompiler jit)
        {
            ScriptEngine.EnableLocalSlots = slots;
            if (jit != null) JitRegistry.Register(jit); else JitRegistry.Clear();
            var chunk = ScriptEngine.Compile(src);
            var engine = new ScriptEngine();
            engine.Run(chunk);
            return engine.Root.GetParameter("__result__").String;
        }

        // The slotted interpreter and slotted closure JIT must both match the
        // name-based interpreter baseline.
        private static void Matches(string src)
        {
            var baseline = Run(src, slots: false, jit: null);
            Assert.That(Run(src, slots: true, jit: null), Is.EqualTo(baseline), "slotted interpreter");
            Assert.That(Run(src, slots: true, jit: new ClosureThreadedJitCompiler()), Is.EqualTo(baseline), "slotted closure JIT");
            Assert.That(Run(src, slots: true, jit: new ReflectionEmitJitCompiler()), Is.EqualTo(baseline), "reflection JIT declines slots");
        }

        // Wrap a JIT-compiled hot function `f` driven past the tier threshold.
        private static string Wrap(string fBody, string arg) =>
            "function f(n){ " + fBody + " }\n" +
            "var r=0; var i=0; while(i<1500){ r = f(" + arg + "); i = i + 1; }\n__result__ = r;";

        [Test]
        public void SlottedVarLoop()
            => Matches(Wrap("var s = 0; var k = 0; while (k < n) { s = s + k; k = k + 1; } return s;", "i % 40"));

        [Test]
        public void SlottedLetLoop()
            => Matches(Wrap("let s = 0; for (let k = 0; k < n; k = k + 1) { s = s + k; } return s;", "i % 40"));

        [Test]
        public void SlottedAccumulatorWithParam()
            // `n` is a parameter (stays name-based); `s` is a slottable local.
            => Matches(Wrap("var s = n; var k = 0; while (k < 5) { s = s + n; k = k + 1; } return s;", "i % 30"));

        [Test]
        public void EligibleFunctionActuallySlots()
        {
            ScriptEngine.EnableLocalSlots = true;
            var chunk = ScriptEngine.Compile(Wrap("var s = 0; var k = 0; while (k < n) { s = s + k; k = k + 1; } return s;", "i % 40"));
            // Functions[0] is `f`; it has slottable var locals and no nested block.
            Assert.That(chunk.Functions[0].UsesSlots, Is.True, "eligible function should use slots");
            // The driver `<main>` is never slotted.
            Assert.That(chunk.UsesSlots, Is.False, "main chunk is not slotted");
        }

        [Test]
        public void ConstStaysNameBased()
            => Matches(Wrap("const base = 100; var s = 0; var k = 0; while (k < n) { s = s + base; k = k + 1; } return s;", "i % 20"));

        [Test]
        public void CapturedLocalNotSlottedButCorrect()
            // `t` is captured by a nested closure → must stay name-based (the closure
            // and frame share the live binding); result must still match.
            => Matches(
                "function f(n){ var t = n; var g = function(){ return t; }; t = t + 1; return g(); }\n" +
                "var r=0; var i=0; while(i<1500){ r = f(i % 30); i = i + 1; }\n__result__ = r;");

        [Test]
        public void UseBeforeDeclarationStaysCorrect()
            // `x` is read (typeof) before its declaration → not promoted; behaviour
            // must match the name-based path exactly.
            => Matches(Wrap("var before = typeof x; var x = n + 1; return before + ':' + x;", "i % 10"));

        [Test]
        public void NestedBlockFunctionStaysCorrect()
            // Two block scopes (an inner { }) → flat-map promotion is declined; result
            // must still match.
            => Matches(Wrap("var s = 0; { let a = n; { let b = a + 1; s = s + b; } } return s;", "i % 25"));

        [Test]
        public void SlottedAccumulatorAfterForOf()
        {
            // Regression: the promotion walk must stride over ForOfStep (a 4-byte
            // operand) correctly. An incomplete operand table mis-strided here,
            // corrupting the bytecode after the for-of (a slotted `s` whose read
            // stayed name-based while its write became SetLocal → divergence).
            var src = Wrap("var s = 0; for (const x of [1, 2, 3, 4, 5]) s += x; var extra = s + n; return extra;", "i % 7");
            Matches(src);
            ScriptEngine.EnableLocalSlots = true;
            Assert.That(ScriptEngine.Compile(src).Functions[0].UsesSlots, Is.True,
                "function with a for-of and a slotted local must actually slot (else the test is vacuous)");
        }

        [Test]
        public void ReassignedLocalLoop()
            => Matches(Wrap("var s = 0; var k = n; while (k > 0) { s = s + k; k = k - 1; } return s;", "i % 40"));

        [Test]
        public void SlottedParameterLoop()
            // A parameter read repeatedly in a loop is now slotted (not name-based).
            => Matches(Wrap("var s = 0; for (var k = 0; k < 50; k = k + 1) { s = s + n; } return s;", "i % 30"));

        [Test]
        public void SlottedParameterReassigned()
            // Writing a parameter must update its slot, not leak to an outer binding.
            => Matches(Wrap("n = n + 100; var s = 0; for (var k = 0; k < 10; k = k + 1) s = s + n; return s;", "i % 25"));

        [Test]
        public void EligibleFunctionSlotsParameters()
        {
            ScriptEngine.EnableLocalSlots = true;
            var src = Wrap("var s = 0; for (var k = 0; k < 20; k = k + 1) s = s + n; return s;", "i % 30");
            // `n` is the only parameter and is read in the loop → it should be slotted.
            var f = ScriptEngine.Compile(src).Functions[0];
            Assert.That(f.UsesSlots, Is.True, "function with a hot parameter read should slot it");
        }

        [Test]
        public void ParamSlottedCalleeStillInlines()
            // A monomorphic leaf callee whose parameters are slotted must still inline
            // (slot access becomes a positional arg read), not fall back to a full call.
            => Matches(
                "function add3(a, b, c){ return a + b + c; }\n" +
                "function f(n){ var s = 0; var k = 0; while (k < n) { s = add3(k, 1, 2); k = k + 1; } return s; }\n" +
                "var r=0; var i=0; while(i<1500){ r = f(i % 30); i = i + 1; }\n__result__ = r;");

        [Test]
        public void ParamWithArgumentsStaysNameBased()
            // A function that uses `arguments` must not slot its parameters (the
            // arguments object/aliasing path needs the named bindings); still correct.
            => Matches(Wrap("var s = 0; for (var k = 0; k < arguments.length + 3; k = k + 1) s = s + n; return s;", "i % 20"));

        [Test]
        public void SlottedBytecodeSurvivesSerialization()
        {
            // Slot metadata is recovered from the code on load; round-tripped slotted
            // bytecode must run and match the baseline.
            ScriptEngine.EnableLocalSlots = true;
            var src = Wrap("var s = 0; var k = 0; while (k < n) { s = s + k; k = k + 1; } return s;", "i % 40");
            var chunk = ScriptEngine.Compile(src);
            Assert.That(chunk.Functions[0].UsesSlots, Is.True);
            var reloaded = BytecodeSerializer.Load(BytecodeSerializer.Save(chunk));
            Assert.That(reloaded.Functions[0].UsesSlots, Is.True, "UsesSlots recovered on load");
            Assert.That(reloaded.Functions[0].SlotCount, Is.GreaterThan(0), "SlotCount recovered on load");
            var engine = new ScriptEngine();
            engine.Run(reloaded);
            ScriptEngine.EnableLocalSlots = false;
            Assert.That(engine.Root.GetParameter("__result__").String, Is.EqualTo(Run(src, slots: false, jit: null)));
        }

        [Test]
        public void NestedFunctionCallsWithSlots()
            // Two slotted functions; the outer calls the inner in a loop.
            => Matches(
                "function add(a, b){ var t = a + b; return t; }\n" +
                "function f(n){ var s = 0; var k = 0; while (k < n) { s = add(s, k); k = k + 1; } return s; }\n" +
                "var r=0; var i=0; while(i<1500){ r = f(i % 30); i = i + 1; }\n__result__ = r;");
    }
}
