using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;
using Environment = DScript.Vm.Environment;

namespace DScript.Test
{
    // Number → string uses the ECMAScript Number::toString algorithm, not .NET's
    // default (which switches to exponential around 1e15). Exercised via string
    // coercion ("" + n) so it goes through ScriptVar.GetString → FormatDouble.
    public class NumberFormattingTests
    {
        private static string Str(string expr)
        {
            var chunk = new DScriptCompiler().CompileProgram($"result = \"\" + ({expr});");
            var globals = ScriptVar.CreateObject();
            new VirtualMachine().Run(chunk, new Environment(globals, null));
            return globals.GetParameter("result").String;
        }

        [TestCase("173685591705600000", "173685591705600000")] // large integer, no exponent
        [TestCase("1234567890123456", "1234567890123456")]
        [TestCase("1e21", "1e+21")]                            // >= 1e21 → exponential
        [TestCase("1000000000000000000000", "1e+21")]
        [TestCase("0.1", "0.1")]
        [TestCase("123.456", "123.456")]
        [TestCase("0.000001", "0.000001")]                     // 1e-6 stays fixed
        [TestCase("0.0000001", "1e-7")]                        // < 1e-6 → exponential
        [TestCase("100", "100")]
        [TestCase("0", "0")]
        public void Coercion_MatchesEcmaScriptFormat(string expr, string expected)
        {
            Assert.That(Str(expr), Is.EqualTo(expected));
        }

        [Test]
        public void Coercion_NegativeLargeInteger()
        {
            Assert.That(Str("-173685591705600000"), Is.EqualTo("-173685591705600000"));
        }

        [Test]
        public void Coercion_RepeatingFraction()
        {
            Assert.That(Str("1 / 3"), Is.EqualTo("0.3333333333333333"));
        }

        [Test]
        public void Coercion_FloatRoundingArtifactPreserved()
        {
            // 0.1 + 0.2 must render the full IEEE-754 artifact, matching V8.
            Assert.That(Str("0.1 + 0.2"), Is.EqualTo("0.30000000000000004"));
        }

        [Test]
        public void Coercion_SpecialValues()
        {
            Assert.That(Str("0 / 0"), Is.EqualTo("NaN"));
            Assert.That(Str("1 / 0"), Is.EqualTo("Infinity"));
            Assert.That(Str("-1 / 0"), Is.EqualTo("-Infinity"));
        }
    }
}
