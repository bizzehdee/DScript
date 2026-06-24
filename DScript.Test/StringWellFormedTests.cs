using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class StringWellFormedTests
    {
        private static readonly string LoneHighSurrogate = "\uD83D";        // lone high surrogate
        private static readonly string LoneLowSurrogate  = "\uDE00";        // lone low surrogate
        private static readonly string SurrogatePair     = "😀";  // valid pair 😀

        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Run(ScriptEngine.Compile(code));
            return engine.Root.GetParameter("result");
        }

        // ── isWellFormed ───────────────────────────────────────────────────────

        [Test]
        public void IsWellFormed_WellFormedAscii_ReturnsTrue()
        {
            var r = RunScript("var result = 'hello'.isWellFormed();");
            Assert.That(r.Bool, Is.True);
        }

        [Test]
        public void IsWellFormed_WellFormedSurrogatePair_ReturnsTrue()
        {
            var r = RunScript($"var result = '{SurrogatePair}'.isWellFormed();");
            Assert.That(r.Bool, Is.True);
        }

        [Test]
        public void IsWellFormed_LoneHighSurrogate_ReturnsFalse()
        {
            var r = RunScript($"var result = '{LoneHighSurrogate}'.isWellFormed();");
            Assert.That(r.Bool, Is.False);
        }

        [Test]
        public void IsWellFormed_LoneLowSurrogate_ReturnsFalse()
        {
            var r = RunScript($"var result = '{LoneLowSurrogate}'.isWellFormed();");
            Assert.That(r.Bool, Is.False);
        }

        [Test]
        public void IsWellFormed_EmptyString_ReturnsTrue()
        {
            var r = RunScript("var result = ''.isWellFormed();");
            Assert.That(r.Bool, Is.True);
        }

        // ── toWellFormed ───────────────────────────────────────────────────────

        [Test]
        public void ToWellFormed_WellFormedString_ReturnsUnchanged()
        {
            var r = RunScript("var result = 'hello'.toWellFormed();");
            Assert.That(r.String, Is.EqualTo("hello"));
        }

        [Test]
        public void ToWellFormed_LoneHighSurrogate_ReplacedWithReplacementChar()
        {
            var r = RunScript($"var result = '{LoneHighSurrogate}'.toWellFormed();");
            Assert.That(r.String, Is.EqualTo("�"));
        }

        [Test]
        public void ToWellFormed_LoneLowSurrogate_ReplacedWithReplacementChar()
        {
            var r = RunScript($"var result = '{LoneLowSurrogate}'.toWellFormed();");
            Assert.That(r.String, Is.EqualTo("�"));
        }

        [Test]
        public void ToWellFormed_ValidPairUnchanged()
        {
            var r = RunScript($"var result = '{SurrogatePair}'.toWellFormed();");
            Assert.That(r.String, Is.EqualTo("😀"));
        }

        [Test]
        public void ToWellFormed_EmptyString_ReturnsEmpty()
        {
            var r = RunScript("var result = ''.toWellFormed();");
            Assert.That(r.String, Is.EqualTo(""));
        }
    }
}
