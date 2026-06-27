using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;
using System.Linq;

namespace DScript.Test
{
    // Lever A / phase A1: the compiler assigns each parameter and local a frame slot and
    // marks captured slots. These are metadata only in A1 (the runtime still resolves by
    // name); the tests lock in that the analysis is correct so phases A2+ can rely on it.
    [TestFixture]
    public class SlotAnalysisTests
    {
        private static Chunk Compile(string src) => new DScriptCompiler().CompileProgram(src);

        // The single nested function compiled by MakeClosure, for inspecting a function body.
        private static Chunk FirstFunction(Chunk c) => c.Functions[0];

        [Test]
        public void TopLevelLocals_GetSequentialSlots()
        {
            var c = Compile("let a = 1; const b = 2; var d = 3;");
            Assert.That(c.SlotMap.ContainsKey("a"), Is.True);
            Assert.That(c.SlotMap.ContainsKey("b"), Is.True);
            Assert.That(c.SlotMap.ContainsKey("d"), Is.True);
            Assert.That(c.SlotCount, Is.EqualTo(3));
            // Distinct slots.
            Assert.That(c.SlotMap.Values.Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public void Parameters_OccupyLowestSlots()
        {
            var fn = FirstFunction(Compile("function f(x, y) { let z = x + y; return z; }"));
            Assert.That(fn.SlotMap["x"], Is.EqualTo(0));
            Assert.That(fn.SlotMap["y"], Is.EqualTo(1));
            Assert.That(fn.SlotMap["z"], Is.EqualTo(2));
            Assert.That(fn.SlotCount, Is.EqualTo(3));
        }

        [Test]
        public void NonCapturingFunction_HasNoCapturedSlots()
        {
            // No nested function exists, so nothing is captured — the all-plain-slot case.
            var fn = FirstFunction(Compile("function f(a, b) { let s = 0; for (let i = 0; i < 10; i++) s += a + b + i; return s; }"));
            Assert.That(fn.CapturedSlots, Is.Empty);
            Assert.That(fn.SlotEligible, Is.True);
            Assert.That(fn.RecyclableFrame, Is.True);
        }

        [Test]
        public void CapturedLocal_IsMarked()
        {
            // The inner arrow references `x`, an outer local, so x's slot is captured.
            var outer = FirstFunction(Compile("function f() { let x = 1; let g = () => x; return g; }"));
            Assert.That(outer.SlotMap.ContainsKey("x"), Is.True);
            Assert.That(outer.CapturedSlots.Contains(outer.SlotMap["x"]), Is.True);
        }

        [Test]
        public void UncapturedLocal_AlongsideCaptured_IsNotMarked()
        {
            // `x` is captured by the arrow; `y` is only used locally and must stay plain.
            var outer = FirstFunction(Compile("function f() { let x = 1; let y = 2; let g = () => x; return g() + y; }"));
            Assert.That(outer.CapturedSlots.Contains(outer.SlotMap["x"]), Is.True);
            Assert.That(outer.CapturedSlots.Contains(outer.SlotMap["y"]), Is.False);
        }

        [Test]
        public void DirectEval_DisablesSlotting()
        {
            var fn = FirstFunction(Compile("function f(a) { let b = 1; return eval('a + b'); }"));
            Assert.That(fn.SlotEligible, Is.False);
        }
    }
}
