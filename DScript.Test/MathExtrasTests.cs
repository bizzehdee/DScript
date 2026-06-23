using System;
using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class MathExtrasTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        // Math.trunc
        [TestCase("Math.trunc(4.7)", 4)]
        [TestCase("Math.trunc(-4.7)", -4)]
        [TestCase("Math.trunc(0)", 0)]
        [TestCase("Math.trunc(4.0)", 4)]
        public void MathTrunc(string expr, int expected)
        {
            var r = RunScript($"var __result__ = {expr};");
            Assert.That(r.Int, Is.EqualTo(expected));
        }

        // Math.sign
        [TestCase("Math.sign(5)", 1)]
        [TestCase("Math.sign(-5)", -1)]
        [TestCase("Math.sign(0)", 0)]
        public void MathSign(string expr, int expected)
        {
            var r = RunScript($"var __result__ = {expr};");
            Assert.That(r.Int, Is.EqualTo(expected));
        }

        [Test]
        public void MathHypot_TwoArgs()
        {
            var r = RunScript("var __result__ = Math.hypot(3, 4);");
            Assert.That(r.Float, Is.EqualTo(5.0).Within(0.0001));
        }

        [Test]
        public void MathHypot_Zero()
        {
            var r = RunScript("var __result__ = Math.hypot(0, 0);");
            Assert.That(r.Float, Is.EqualTo(0.0));
        }

        [Test]
        public void MathLog2()
        {
            var r = RunScript("var __result__ = Math.log2(8);");
            Assert.That(r.Float, Is.EqualTo(3.0).Within(0.0001));
        }

        [Test]
        public void MathLog10()
        {
            var r = RunScript("var __result__ = Math.log10(1000);");
            Assert.That(r.Float, Is.EqualTo(3.0).Within(0.0001));
        }

        [Test]
        public void MathCbrt()
        {
            var r = RunScript("var __result__ = Math.cbrt(27);");
            Assert.That(r.Float, Is.EqualTo(3.0).Within(0.0001));
        }

        [Test]
        public void MathCbrt_Negative()
        {
            var r = RunScript("var __result__ = Math.cbrt(-8);");
            Assert.That(r.Float, Is.EqualTo(-2.0).Within(0.0001));
        }

        [TestCase("Math.clamp(5, 0, 10)", 5)]
        [TestCase("Math.clamp(-5, 0, 10)", 0)]
        [TestCase("Math.clamp(15, 0, 10)", 10)]
        public void MathClamp(string expr, int expected)
        {
            var r = RunScript($"var __result__ = {expr};");
            Assert.That(r.Int, Is.EqualTo(expected));
        }

        [Test]
        public void MathFround_RoundsToFloat32()
        {
            // 1.337 cannot be represented exactly in float32
            var r = RunScript("var __result__ = Math.fround(1.337);");
            Assert.That(r.Float, Is.Not.EqualTo(1.337).Within(1e-10));
        }

        [Test]
        public void MathImul()
        {
            var r = RunScript("var __result__ = Math.imul(3, 4);");
            Assert.That(r.Int, Is.EqualTo(12));
        }

        [Test]
        public void MathImul_Overflow_WrapsAround()
        {
            var r = RunScript("var __result__ = Math.imul(0xFFFFFFFF, 2);");
            Assert.That(r.Int, Is.EqualTo(-2));
        }

        [Test]
        public void PerformanceNow_ReturnsPositiveDouble()
        {
            var r = RunScript("var __result__ = performance.now();");
            Assert.That(r.Float, Is.GreaterThan(0.0));
        }

        [Test]
        public void PerformanceNow_Increases()
        {
            var r = RunScript("var t1 = performance.now(); var t2 = performance.now(); var __result__ = t2 >= t1;");
            Assert.That(r.Bool, Is.True);
        }

        [Test]
        public void StructuredClone_CopiesValue()
        {
            var r = RunScript("var a = {x:1}; var b = structuredClone(a); b.x = 99; var __result__ = a.x;");
            Assert.That(r.Int, Is.EqualTo(1));
        }

        [Test]
        public void StructuredClone_PrimitiveValue()
        {
            var r = RunScript("var __result__ = structuredClone(42);");
            Assert.That(r.Int, Is.EqualTo(42));
        }

        [Test]
        public void QueueMicrotask_ExecutesOnDrain()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute("var __result__ = 0; queueMicrotask(function() { __result__ = 42; });");
            Assert.That(engine.Root.GetParameter("__result__").Int, Is.EqualTo(0));
            ScriptEngine.DrainMicroTasks();
            Assert.That(engine.Root.GetParameter("__result__").Int, Is.EqualTo(42));
        }

        // ── Math constants ────────────────────────────────────────────────────

        [Test] public void MathPI() { var r = RunScript("var __result__ = Math.PI;"); Assert.That(r.Float, Is.EqualTo(Math.PI).Within(1e-10)); }
        [Test] public void MathE() { var r = RunScript("var __result__ = Math.E;"); Assert.That(r.Float, Is.EqualTo(Math.E).Within(1e-10)); }
        [Test] public void MathSQRT2() { var r = RunScript("var __result__ = Math.SQRT2;"); Assert.That(r.Float, Is.EqualTo(Math.Sqrt(2)).Within(1e-10)); }
        [Test] public void MathSQRT1_2() { var r = RunScript("var __result__ = Math.SQRT1_2;"); Assert.That(r.Float, Is.EqualTo(Math.Sqrt(0.5)).Within(1e-10)); }
        [Test] public void MathLN2() { var r = RunScript("var __result__ = Math.LN2;"); Assert.That(r.Float, Is.EqualTo(Math.Log(2)).Within(1e-10)); }
        [Test] public void MathLN10() { var r = RunScript("var __result__ = Math.LN10;"); Assert.That(r.Float, Is.EqualTo(Math.Log(10)).Within(1e-10)); }
        [Test] public void MathLOG2E() { var r = RunScript("var __result__ = Math.LOG2E;"); Assert.That(r.Float, Is.EqualTo(Math.Log(Math.E, 2)).Within(1e-10)); }
        [Test] public void MathLOG10E() { var r = RunScript("var __result__ = Math.LOG10E;"); Assert.That(r.Float, Is.EqualTo(Math.Log10(Math.E)).Within(1e-10)); }

        // ── Math functions ────────────────────────────────────────────────────

        [Test] public void MathMin() { var r = RunScript("var __result__ = Math.min(3, 7);"); Assert.That(r.Float, Is.EqualTo(3.0)); }
        [Test] public void MathMax() { var r = RunScript("var __result__ = Math.max(3, 7);"); Assert.That(r.Float, Is.EqualTo(7.0)); }
        [Test] public void MathFloor() { var r = RunScript("var __result__ = Math.floor(4.9);"); Assert.That(r.Float, Is.EqualTo(4.0)); }
        [Test] public void MathCeil() { var r = RunScript("var __result__ = Math.ceil(4.1);"); Assert.That(r.Float, Is.EqualTo(5.0)); }
        [Test] public void MathRound() { var r = RunScript("var __result__ = Math.round(4.7);"); Assert.That(r.Float, Is.EqualTo(5.0)); }
        [Test] public void MathAbs() { var r = RunScript("var __result__ = Math.abs(-7);"); Assert.That(r.Float, Is.EqualTo(7.0)); }
        [Test] public void MathSqrt() { var r = RunScript("var __result__ = Math.sqrt(9);"); Assert.That(r.Float, Is.EqualTo(3.0).Within(0.0001)); }
        [Test] public void MathPow() { var r = RunScript("var __result__ = Math.pow(2, 8);"); Assert.That(r.Float, Is.EqualTo(256.0)); }
        [Test] public void MathLog() { var r = RunScript("var __result__ = Math.log(1);"); Assert.That(r.Float, Is.EqualTo(0.0).Within(0.0001)); }
        [Test] public void MathExp() { var r = RunScript("var __result__ = Math.exp(0);"); Assert.That(r.Float, Is.EqualTo(1.0).Within(0.0001)); }
        [Test] public void MathSin() { var r = RunScript("var __result__ = Math.sin(0);"); Assert.That(r.Float, Is.EqualTo(0.0).Within(0.0001)); }
        [Test] public void MathCos() { var r = RunScript("var __result__ = Math.cos(0);"); Assert.That(r.Float, Is.EqualTo(1.0).Within(0.0001)); }
        [Test] public void MathTan() { var r = RunScript("var __result__ = Math.tan(0);"); Assert.That(r.Float, Is.EqualTo(0.0).Within(0.0001)); }
        [Test] public void MathAsin() { var r = RunScript("var __result__ = Math.asin(0);"); Assert.That(r.Float, Is.EqualTo(0.0).Within(0.0001)); }
        [Test] public void MathAcos() { var r = RunScript("var __result__ = Math.acos(1);"); Assert.That(r.Float, Is.EqualTo(0.0).Within(0.0001)); }
        [Test] public void MathAtan() { var r = RunScript("var __result__ = Math.atan(0);"); Assert.That(r.Float, Is.EqualTo(0.0).Within(0.0001)); }
        [Test] public void MathAtan2() { var r = RunScript("var __result__ = Math.atan2(0, 1);"); Assert.That(r.Float, Is.EqualTo(0.0).Within(0.0001)); }
        [Test] public void MathSinh() { var r = RunScript("var __result__ = Math.sinh(0);"); Assert.That(r.Float, Is.EqualTo(0.0).Within(0.0001)); }
        [Test] public void MathCosh() { var r = RunScript("var __result__ = Math.cosh(0);"); Assert.That(r.Float, Is.EqualTo(1.0).Within(0.0001)); }
        [Test] public void MathTanh() { var r = RunScript("var __result__ = Math.tanh(0);"); Assert.That(r.Float, Is.EqualTo(0.0).Within(0.0001)); }
        [Test] public void MathRandom_InRange() { var r = RunScript("var __result__ = Math.random();"); Assert.That(r.Float, Is.InRange(0.0, 1.0)); }
        [Test] public void MathRand_InRange() { var r = RunScript("var __result__ = Math.rand();"); Assert.That(r.Float, Is.InRange(0.0, 1.0)); }
        [Test] public void MathRandomInt() { var r = RunScript("var __result__ = Math.randomInt(1, 10);"); Assert.That(r.Int, Is.InRange(1, 9)); }
        [Test] public void MathRandInt() { var r = RunScript("var __result__ = Math.randInt(5, 15);"); Assert.That(r.Int, Is.InRange(5, 14)); }
        [Test] public void MathHypot_Array() { var r = RunScript("var __result__ = Math.hypot([3, 4]);"); Assert.That(r.Float, Is.EqualTo(5.0).Within(0.0001)); }
    }
}
