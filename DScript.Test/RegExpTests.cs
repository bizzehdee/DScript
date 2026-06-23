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

        // ── named capture groups (ES2018) ─────────────────────────────────────

        [Test]
        public void NamedCaptureGroup_Exec_GroupsObjectPopulated()
        {
            var engine = MakeEngine();
            engine.Execute(@"
                var re = new RegExp('(?<year>\\d{4})-(?<month>\\d{2})', '');
                var m = re.exec('2024-01');
                var result = m.groups.year;
            ");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("2024"));
        }

        [Test]
        public void NamedCaptureGroup_Match_GroupsObjectPopulated()
        {
            var engine = MakeEngine();
            engine.Execute(@"
                var re = new RegExp('(?<word>\\w+)', '');
                var m = 'hello'.match(re);
                var result = m.groups.word;
            ");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("hello"));
        }

        [Test]
        public void NoNamedGroups_Exec_GroupsIsUndefined()
        {
            var engine = MakeEngine();
            engine.Execute(@"
                var re = new RegExp('(\\d+)', '');
                var m = re.exec('42');
                var result = (m.groups === undefined) ? 'yes' : 'no';
            ");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("yes"));
        }

        // ── lookahead / lookbehind (ES2018) ───────────────────────────────────

        [Test]
        public void PositiveLookahead_Matches()
        {
            var engine = MakeEngine();
            engine.Execute("var re = new RegExp('foo(?=bar)', ''); var m = re.exec('foobar'); var result = m[0];");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("foo"));
        }

        [Test]
        public void NegativeLookahead_MatchesWhenNotFollowed()
        {
            var engine = MakeEngine();
            engine.Execute("var re = new RegExp('foo(?!bar)', ''); var m = re.exec('foobaz'); var result = m[0];");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("foo"));
        }

        [Test]
        public void NegativeLookahead_DoesNotMatchWhenFollowedByBar()
        {
            var engine = MakeEngine();
            engine.Execute("var re = new RegExp('foo(?!bar)', ''); var m = re.exec('foobar'); var result = (m === undefined) ? 'null' : 'match';");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("null"));
        }

        [Test]
        public void PositiveLookbehind_Matches()
        {
            var engine = MakeEngine();
            engine.Execute("var re = new RegExp('(?<=foo)bar', ''); var m = re.exec('foobar'); var result = m[0];");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("bar"));
        }

        [Test]
        public void NegativeLookbehind_MatchesWhenNotPreceded()
        {
            var engine = MakeEngine();
            engine.Execute("var re = new RegExp('(?<!foo)bar', ''); var m = re.exec('bazbar'); var result = m[0];");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("bar"));
        }

        // ── s (dotAll) flag (ES2018) ──────────────────────────────────────────

        [Test]
        public void DotAllFlag_DotMatchesNewline()
        {
            var engine = MakeEngine();
            // Use a multiline string via concat so the newline is a real JS newline character
            engine.Execute("var re = new RegExp('a.b', 's'); var nl = 'a' + String.fromCharCode(10) + 'b'; var result = re.test(nl) ? 'yes' : 'no';");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("yes"));
        }

        [Test]
        public void DotAllFlag_AbsentByDefault_DotDoesNotMatchNewline()
        {
            var engine = MakeEngine();
            engine.Execute("var re = new RegExp('a.b', ''); var nl = 'a' + String.fromCharCode(10) + 'b'; var result = re.test(nl) ? 'yes' : 'no';");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("no"));
        }

        // ── sorted flags property ─────────────────────────────────────────────

        [Test]
        public void FlagsProperty_ReturnsSortedCanonicalString()
        {
            var engine = MakeEngine();
            engine.Execute("var re = new RegExp('x', 'mig'); var result = re.flags;");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("gim"));
        }

        // ── matchAll (ES2020) ─────────────────────────────────────────────────

        [Test]
        public void MatchAll_GlobalFlag_ReturnsAllMatches()
        {
            var engine = MakeEngine();
            engine.Execute(@"
                var re = new RegExp('\\d+', 'g');
                var matches = 'a1b2c3'.matchAll(re);
                var result = matches.length;
            ");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(3));
        }

        [Test]
        public void MatchAll_EachMatch_HasIndexProperty()
        {
            var engine = MakeEngine();
            engine.Execute(@"
                var re = new RegExp('\\d+', 'g');
                var matches = 'a1b2'.matchAll(re);
                var result = matches[0].index;
            ");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(1));
        }

        [Test]
        public void MatchAll_EachMatch_HasInputProperty()
        {
            var engine = MakeEngine();
            engine.Execute(@"
                var re = new RegExp('\\d+', 'g');
                var matches = 'a1b2'.matchAll(re);
                var result = matches[0].input;
            ");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("a1b2"));
        }

        [Test]
        public void MatchAll_NamedGroups_PopulatedPerMatch()
        {
            var engine = MakeEngine();
            engine.Execute(@"
                var re = new RegExp('(?<n>\\d+)', 'g');
                var matches = 'a1b2'.matchAll(re);
                var result = matches[1].groups.n;
            ");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("2"));
        }

        [Test]
        public void MatchAll_WithoutGlobalFlag_ThrowsScriptException()
        {
            // engine.Execute() swallows exceptions — use Run() to propagate them
            var engine = MakeEngine();
            var chunk = ScriptEngine.Compile("var re = new RegExp('x'); 'axb'.matchAll(re);");
            Assert.Throws<ScriptException>(() => engine.Run(chunk));
        }

        // --- d (indices) flag ---

        [Test]
        public void DFlag_ExecPopulatesIndices()
        {
            var engine = MakeEngine();
            engine.Execute("var re = new RegExp('ab(c)', 'd'); var m = re.exec('xabcy'); var result = m.indices[0][0];");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(1));
        }

        [Test]
        public void DFlag_ExecIndicesEndIsExclusive()
        {
            var engine = MakeEngine();
            engine.Execute("var re = new RegExp('ab(c)', 'd'); var m = re.exec('xabcy'); var result = m.indices[0][1];");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(4)); // "abc" from index 1, length 3 → end 4
        }

        [Test]
        public void DFlag_ExecIndicesGroupCapture()
        {
            var engine = MakeEngine();
            engine.Execute("var re = new RegExp('ab(c)', 'd'); var m = re.exec('xabcy'); var result = m.indices[1][0];");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(3)); // "c" at index 3
        }

        [Test]
        public void DFlag_ExecHasGroupsObject()
        {
            var engine = MakeEngine();
            engine.Execute("var re = new RegExp('(?<word>\\\\w+)', 'd'); var m = re.exec('hello world'); var result = m.indices.groups.word[0];");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(0));
        }

        [Test]
        public void DFlag_WithoutDFlagNoIndicesProperty()
        {
            var engine = MakeEngine();
            engine.Execute("var re = new RegExp('abc'); var m = re.exec('xabcy'); var result = (m.indices === undefined) ? 1 : 0;");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(1));
        }

        [Test]
        public void DFlag_StringMatchPopulatesIndices()
        {
            var engine = MakeEngine();
            engine.Execute("var re = new RegExp('ab(c)', 'd'); var m = 'xabcy'.match(re); var result = m.indices[0][0];");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(1));
        }
    }
}
