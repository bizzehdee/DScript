using DScript.Compiler;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class ExponentiationTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            engine.Run(ScriptEngine.Compile(code));
            return engine.Root.GetParameter("result");
        }

        private static void CompileExpect(string code)
            => new DScriptCompiler().CompileProgram(code);

        // ── basic arithmetic ───────────────────────────────────────────────────

        [Test]
        public void Exponentiation_IntBase_IntExponent()
        {
            var r = RunScript("var result = 2 ** 10;");
            Assert.That(r.Int, Is.EqualTo(1024));
        }

        [Test]
        public void Exponentiation_ZeroExponent_ReturnsOne()
        {
            var r = RunScript("var result = 99 ** 0;");
            Assert.That(r.Int, Is.EqualTo(1));
        }

        [Test]
        public void Exponentiation_OneBase_ReturnsOne()
        {
            var r = RunScript("var result = 1 ** 1000;");
            Assert.That(r.Int, Is.EqualTo(1));
        }

        [Test]
        public void Exponentiation_FloatBase()
        {
            var r = RunScript("var result = 2.5 ** 2;");
            Assert.That(r.Float, Is.EqualTo(6.25).Within(0.0001));
        }

        [Test]
        public void Exponentiation_NegativeExponent_ReturnsDouble()
        {
            var r = RunScript("var result = 2 ** -1;");
            Assert.That(r.Float, Is.EqualTo(0.5).Within(0.0001));
        }

        [Test]
        public void Exponentiation_ZeroBase()
        {
            var r = RunScript("var result = 0 ** 5;");
            Assert.That(r.Int, Is.EqualTo(0));
        }

        // ── right-associativity ────────────────────────────────────────────────

        [Test]
        public void Exponentiation_IsRightAssociative()
        {
            // 2 ** 3 ** 2 == 2 ** (3 ** 2) == 2 ** 9 == 512, not (2**3)**2 == 64
            var r = RunScript("var result = 2 ** 3 ** 2;");
            Assert.That(r.Int, Is.EqualTo(512));
        }

        [Test]
        public void Exponentiation_ExplicitParens_Left()
        {
            var r = RunScript("var result = (2 ** 3) ** 2;");
            Assert.That(r.Int, Is.EqualTo(64));
        }

        // ── precedence relative to * / % ──────────────────────────────────────

        [Test]
        public void Exponentiation_HigherPrecedenceThanMultiply()
        {
            // 2 * 3 ** 2 == 2 * (3**2) == 2 * 9 == 18, not (2*3)**2 == 36
            var r = RunScript("var result = 2 * 3 ** 2;");
            Assert.That(r.Int, Is.EqualTo(18));
        }

        [Test]
        public void Exponentiation_HigherPrecedenceThanDivide()
        {
            // 81 / 3 ** 2 == 81 / 9 == 9
            var r = RunScript("var result = 81 / 3 ** 2;");
            Assert.That(r.Int, Is.EqualTo(9));
        }

        // ── unary before ** is a syntax error ─────────────────────────────────

        [Test]
        public void Exponentiation_UnaryMinusBeforePower_ThrowsSyntaxError()
        {
            Assert.Throws<ScriptException>(() => CompileExpect("var result = -2 ** 2;"));
        }

        [Test]
        public void Exponentiation_UnaryNotBeforePower_ThrowsSyntaxError()
        {
            Assert.Throws<ScriptException>(() => CompileExpect("var result = !2 ** 2;"));
        }

        [Test]
        public void Exponentiation_UnaryMinusWithParens_Works()
        {
            var r = RunScript("var result = (-2) ** 2;");
            Assert.That(r.Int, Is.EqualTo(4));
        }

        // ── compound assignment **= ────────────────────────────────────────────

        [Test]
        public void PowerEqual_Variable()
        {
            var r = RunScript("var result = 3; result **= 4;");
            Assert.That(r.Int, Is.EqualTo(81));
        }

        [Test]
        public void PowerEqual_Property()
        {
            var r = RunScript("var o = {v: 2}; o.v **= 8; var result = o.v;");
            Assert.That(r.Int, Is.EqualTo(256));
        }

        [Test]
        public void PowerEqual_Index()
        {
            var r = RunScript("var a = [3]; a[0] **= 3; var result = a[0];");
            Assert.That(r.Int, Is.EqualTo(27));
        }

        // ── BigInt ────────────────────────────────────────────────────────────

        [Test]
        public void Exponentiation_BigInt()
        {
            var r = RunScript("var result = 2n ** 64n;");
            Assert.That(r.BigIntData.ToString(), Is.EqualTo("18446744073709551616"));
        }

        [Test]
        public void Exponentiation_BigInt_NegativeExponent_Throws()
        {
            // **= on BigInt with negative exponent is a runtime ScriptException
            var engine = new ScriptEngine();
            Assert.Throws<ScriptException>(() =>
                engine.Run(ScriptEngine.Compile("var result = 2n ** -1n;")));
        }
    }
}
