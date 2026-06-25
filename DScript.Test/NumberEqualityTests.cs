using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    // Strict/loose equality on numbers, and negative zero. int and double are the same
    // JS "number" type, comparison is exact (not epsilon-based), and -0 is distinct
    // from +0 only under Object.is.
    public class NumberEqualityTests
    {
        private static bool RunBool(string expr)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute($"__result__ = ({expr}) ? 1 : 0;");
            return engine.Root.GetParameter("__result__").Int == 1;
        }

        [TestCase("5.0 === 5", true)]          // int and double, same value
        [TestCase("0.0 === 0", true)]
        [TestCase("2.5 === 2.5", true)]
        [TestCase("5.0 === 6", false)]
        [TestCase("(0.1 + 0.2) === 0.3", false)] // exact, not epsilon
        [TestCase("NaN === NaN", false)]
        [TestCase("-0 === 0", true)]            // === treats -0 and +0 as equal
        [TestCase("(1 / 0) === Infinity", true)]
        [TestCase("(1 / -0) === -Infinity", true)]
        [TestCase("-Infinity === -Infinity", true)]
        public void StrictEquality(string expr, bool expected)
        {
            Assert.That(RunBool(expr), Is.EqualTo(expected));
        }

        [TestCase("5.0 == 5", true)]
        [TestCase("(0.1 + 0.2) == 0.3", false)]
        [TestCase("NaN == NaN", false)]
        public void LooseEquality(string expr, bool expected)
        {
            Assert.That(RunBool(expr), Is.EqualTo(expected));
        }

        // ── negative zero ──────────────────────────────────────────────────────

        [Test]
        public void NegativeZero_ReciprocalIsNegativeInfinity()
        {
            Assert.That(RunBool("1 / -0 === -Infinity"), Is.True);
        }

        [Test]
        public void ObjectIs_DistinguishesSignedZero()
        {
            Assert.That(RunBool("Object.is(-0, 0)"), Is.False);
            Assert.That(RunBool("Object.is(-0, -0)"), Is.True);
            Assert.That(RunBool("Object.is(0, 0)"), Is.True);
        }

        [Test]
        public void ObjectIs_NaN()
        {
            Assert.That(RunBool("Object.is(NaN, NaN)"), Is.True);
        }
    }
}
