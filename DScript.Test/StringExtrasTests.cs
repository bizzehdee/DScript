using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class StringExtrasTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        // startsWith
        [TestCase("'hello'.startsWith('hel')", true)]
        [TestCase("'hello'.startsWith('llo')", false)]
        [TestCase("'hello'.startsWith('')", true)]
        [TestCase("'hello'.startsWith('hello')", true)]
        public void StartsWith(string expr, bool expected)
            => Assert.That(RunScript($"var __result__ = {expr};").Bool, Is.EqualTo(expected));

        // endsWith
        [TestCase("'hello'.endsWith('llo')", true)]
        [TestCase("'hello'.endsWith('hel')", false)]
        [TestCase("'hello'.endsWith('')", true)]
        public void EndsWith(string expr, bool expected)
            => Assert.That(RunScript($"var __result__ = {expr};").Bool, Is.EqualTo(expected));

        // includes
        [TestCase("'hello world'.includes('world')", true)]
        [TestCase("'hello world'.includes('xyz')", false)]
        [TestCase("'hello world'.includes('')", true)]
        public void Includes(string expr, bool expected)
            => Assert.That(RunScript($"var __result__ = {expr};").Bool, Is.EqualTo(expected));

        // repeat
        [TestCase("'ab'.repeat(3)", "ababab")]
        [TestCase("'x'.repeat(0)", "")]
        [TestCase("'x'.repeat(1)", "x")]
        public void Repeat(string expr, string expected)
            => Assert.That(RunScript($"var __result__ = {expr};").String, Is.EqualTo(expected));

        // padStart
        [TestCase("'5'.padStart(3, '0')", "005")]
        [TestCase("'hi'.padStart(5)", "   hi")]
        [TestCase("'hello'.padStart(3)", "hello")]
        public void PadStart(string expr, string expected)
            => Assert.That(RunScript($"var __result__ = {expr};").String, Is.EqualTo(expected));

        // padEnd
        [TestCase("'5'.padEnd(3, '0')", "500")]
        [TestCase("'hi'.padEnd(5)", "hi   ")]
        [TestCase("'hello'.padEnd(3)", "hello")]
        public void PadEnd(string expr, string expected)
            => Assert.That(RunScript($"var __result__ = {expr};").String, Is.EqualTo(expected));

        // slice
        [Test]
        public void Slice_PositiveIndices()
            => Assert.That(RunScript("var __result__ = 'hello'.slice(1, 3);").String, Is.EqualTo("el"));

        [Test]
        public void Slice_NegativeStart()
            => Assert.That(RunScript("var __result__ = 'hello'.slice(-3);").String, Is.EqualTo("llo"));

        [Test]
        public void Slice_NegativeEnd()
            => Assert.That(RunScript("var __result__ = 'hello'.slice(0, -1);").String, Is.EqualTo("hell"));

        [Test]
        public void Slice_NoEnd()
            => Assert.That(RunScript("var __result__ = 'hello'.slice(2);").String, Is.EqualTo("llo"));

        // trimStart / trimEnd
        [Test]
        public void TrimStart()
            => Assert.That(RunScript("var __result__ = '  hi  '.trimStart();").String, Is.EqualTo("hi  "));

        [Test]
        public void TrimEnd()
            => Assert.That(RunScript("var __result__ = '  hi  '.trimEnd();").String, Is.EqualTo("  hi"));

        // replaceAll
        [Test]
        public void ReplaceAll()
            => Assert.That(RunScript("var __result__ = 'aabbaa'.replaceAll('a', 'x');").String, Is.EqualTo("xxbbxx"));

        [Test]
        public void ReplaceAll_NoMatch()
            => Assert.That(RunScript("var __result__ = 'hello'.replaceAll('z', 'x');").String, Is.EqualTo("hello"));

        // at
        [Test]
        public void At_PositiveIndex()
            => Assert.That(RunScript("var __result__ = 'hello'.at(0);").String, Is.EqualTo("h"));

        [Test]
        public void At_NegativeIndex()
            => Assert.That(RunScript("var __result__ = 'hello'.at(-1);").String, Is.EqualTo("o"));

        [Test]
        public void At_OutOfRange()
            => Assert.That(RunScript("var __result__ = 'hello'.at(99);").IsUndefined, Is.True);

        // search
        [Test]
        public void Search_Match()
            => Assert.That(RunScript("var __result__ = 'hello world'.search('world');").Int, Is.EqualTo(6));

        [Test]
        public void Search_NoMatch()
            => Assert.That(RunScript("var __result__ = 'hello'.search('xyz');").Int, Is.EqualTo(-1));

        // matchAll
        [Test]
        public void MatchAll_ReturnsArray()
        {
            var r = RunScript("var m = 'test1 test2'.matchAll('test\\\\d'); var __result__ = m.length;");
            Assert.That(r.Int, Is.EqualTo(2));
        }

        [Test]
        public void MatchAll_NoMatches()
        {
            var r = RunScript("var m = 'hello'.matchAll('xyz'); var __result__ = m.length;");
            Assert.That(r.Int, Is.EqualTo(0));
        }
    }
}
