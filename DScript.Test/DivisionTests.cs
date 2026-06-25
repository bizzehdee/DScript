using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;
using Environment = DScript.Vm.Environment;

namespace DScript.Test
{
    // JS '/' is always real (floating) division — 1/3 is 0.333…, not 0. The VM had
    // an int-fast-path that truncated. Modulo and division-by-zero are also checked.
    public class DivisionTests
    {
        private static ScriptVar Eval(string expr)
            => new VirtualMachine().Run(new DScriptCompiler().CompileExpression(expr));

        private static double EvalF(string expr) => Eval(expr).Float;

        [Test]
        public void Division_NonExact_ProducesFraction()
        {
            Assert.That(EvalF("1 / 3"), Is.EqualTo(1.0 / 3.0));
            Assert.That(EvalF("10 / 4"), Is.EqualTo(2.5));
            Assert.That(EvalF("7 / 2"), Is.EqualTo(3.5));
        }

        [Test]
        public void Division_Exact_StaysInteger()
        {
            var v = Eval("6 / 3");
            Assert.That(v.IsInt, Is.True);
            Assert.That(v.Int, Is.EqualTo(2));
        }

        [Test]
        public void Division_ByZero_YieldsInfinityOrNaN()
        {
            Assert.That(EvalF("1 / 0"), Is.EqualTo(double.PositiveInfinity));
            Assert.That(EvalF("-1 / 0"), Is.EqualTo(double.NegativeInfinity));
            Assert.That(double.IsNaN(EvalF("0 / 0")), Is.True);
        }

        [Test]
        public void Division_MinValueByNegativeOne_PromotesToDouble()
        {
            // int MinValue / -1 = 2147483648, which overflows int32 → must be a double.
            // Build MinValue by computation so both operands stay on the int fast path.
            Assert.That(EvalF("(-2147483647 - 1) / -1"), Is.EqualTo(2147483648.0));
        }

        [Test]
        public void Modulo_Basic()
        {
            Assert.That(EvalF("10 % 3"), Is.EqualTo(1.0));
            Assert.That(EvalF("10 % 2"), Is.EqualTo(0.0));
            Assert.That(EvalF("-7 % 3"), Is.EqualTo(-1.0)); // JS keeps the dividend's sign
        }

        [Test]
        public void Modulo_ByZero_IsNaN()
        {
            Assert.That(double.IsNaN(EvalF("5 % 0")), Is.True);
        }

        [Test]
        public void Modulo_MinValueByNegativeOne_IsZero()
        {
            // int MinValue % -1 would overflow a raw int rem; result is 0. Build
            // MinValue by computation so the int fast path (and its guard) is used.
            Assert.That(EvalF("(-2147483647 - 1) % -1"), Is.EqualTo(0.0));
        }

        [Test]
        public void Modulo_Doubles()
        {
            // The double branch of MathsOp previously had no '%' case at all.
            Assert.That(EvalF("5.5 % 2"), Is.EqualTo(1.5));
        }

        [Test]
        public void Division_InLoop_AccumulatesFractions()
        {
            // Forces the hot path / potential JIT tier-up to also be correct.
            var chunk = new DScriptCompiler().CompileProgram(
                "var s = 0; for (var i = 1; i <= 4; i = i + 1) s = s + 1 / i; result = s;");
            var globals = ScriptVar.CreateObject();
            new VirtualMachine().Run(chunk, new Environment(globals, null));
            Assert.That(globals.GetParameter("result").Float,
                Is.EqualTo(1.0 + 1.0 / 2 + 1.0 / 3 + 1.0 / 4).Within(1e-12));
        }
    }
}
