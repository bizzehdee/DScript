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
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class RegExpTests
    {
        private static ScriptEngine MakeEngine()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            return engine;
        }

        // --- constructor properties ---

        [Test]
        public void RegExp_Constructor_SetsSourceProperty()
        {
            var engine = MakeEngine();
            engine.Execute("var r = new RegExp('hello'); var result = r.source;");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("hello"));
        }

        [Test]
        public void RegExp_Constructor_SetsFlagsProperty()
        {
            var engine = MakeEngine();
            engine.Execute("var r = new RegExp('abc', 'gi'); var result = r.flags;");
            Assert.That(engine.Root.GetParameter("result").String, Does.Contain("g").And.Contain("i"));
        }

        [Test]
        public void RegExp_Constructor_SetsGlobalProperty()
        {
            var engine = MakeEngine();
            engine.Execute("var r = new RegExp('x', 'g'); var result = r.global;");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.True);
        }

        [Test]
        public void RegExp_Constructor_GlobalFalseWhenNotSet()
        {
            var engine = MakeEngine();
            engine.Execute("var r = new RegExp('x'); var result = r.global;");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.False);
        }

        [Test]
        public void RegExp_Constructor_SetsIgnoreCaseProperty()
        {
            var engine = MakeEngine();
            engine.Execute("var r = new RegExp('x', 'i'); var result = r.ignoreCase;");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.True);
        }

        [Test]
        public void RegExp_Constructor_SetsMultilineProperty()
        {
            var engine = MakeEngine();
            engine.Execute("var r = new RegExp('x', 'm'); var result = r.multiline;");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.True);
        }

        // --- test() ---

        [Test]
        public void RegExp_Test_MatchingString_ReturnsTrue()
        {
            var engine = MakeEngine();
            engine.Execute("var r = new RegExp('hello'); var result = r.test('say hello world');");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.True);
        }

        [Test]
        public void RegExp_Test_NonMatchingString_ReturnsFalse()
        {
            var engine = MakeEngine();
            engine.Execute("var r = new RegExp('xyz'); var result = r.test('hello world');");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.False);
        }

        [Test]
        public void RegExp_Test_CaseInsensitive_Matches()
        {
            var engine = MakeEngine();
            engine.Execute("var r = new RegExp('HELLO', 'i'); var result = r.test('hello');");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.True);
        }

        [Test]
        public void RegExp_Test_EmptyPattern_AlwaysMatches()
        {
            var engine = MakeEngine();
            engine.Execute("var r = new RegExp(''); var result = r.test('anything');");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.True);
        }

        // --- exec() ---

        [Test]
        public void RegExp_Exec_Match_ReturnsArray()
        {
            var engine = MakeEngine();
            engine.Execute("var r = new RegExp('(hel)lo'); var m = r.exec('say hello'); var result = m[0];");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("hello"));
        }

        [Test]
        public void RegExp_Exec_Match_ReturnsGroup()
        {
            var engine = MakeEngine();
            engine.Execute("var r = new RegExp('(hel)lo'); var m = r.exec('say hello'); var result = m[1];");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("hel"));
        }

        [Test]
        public void RegExp_Exec_Match_SetsIndex()
        {
            var engine = MakeEngine();
            engine.Execute("var r = new RegExp('hello'); var m = r.exec('say hello'); var result = m.index;");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(4));
        }

        [Test]
        public void RegExp_Exec_NoMatch_ReturnsUndefined()
        {
            var engine = MakeEngine();
            engine.Execute("var r = new RegExp('xyz'); var m = r.exec('hello'); var result = (m === undefined);");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.True);
        }

        // --- literal regex with existing string methods ---

        [Test]
        public void RegExpLiteral_StringMatch_Works()
        {
            var engine = MakeEngine();
            engine.Execute("var result = 'hello world'.match(/hello/)[0];");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("hello"));
        }

        [Test]
        public void RegExp_Test_DigitPattern_Matches()
        {
            var engine = MakeEngine();
            engine.Execute("var r = new RegExp('[0-9]+'); var result = r.test('abc 42 def');");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.True);
        }
    }
}
