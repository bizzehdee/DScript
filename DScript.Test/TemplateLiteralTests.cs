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

using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class TemplateLiteralTests
    {
        private static ScriptEngine MakeEngine()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            return engine;
        }

        private static ScriptVar Run(string code)
        {
            var engine = MakeEngine();
            engine.Execute(code);
            return engine.Root;
        }

        // ── tagged template basics ─────────────────────────────────────────────

        [Test]
        public void TaggedTemplate_TagReceivesCorrectStringSegmentCount()
        {
            // Template with 2 expressions → 3 string segments
            var root = Run(@"
function tag(strings, a, b) { return strings.length; }
var r = tag`hello ${1} world ${2} end`;
");
            Assert.That(root.GetParameter("r").Int, Is.EqualTo(3));
        }

        [Test]
        public void TaggedTemplate_FirstSegmentIsCookedString()
        {
            var root = Run(@"
function tag(strings) { return strings[0]; }
var r = tag`hello world`;
");
            Assert.That(root.GetParameter("r").String, Is.EqualTo("hello world"));
        }

        [Test]
        public void TaggedTemplate_RawArrayPreservesEscapeSequences()
        {
            // \n in raw should be the two characters \ and n, not a newline
            var root = Run(@"
function tag(strings) { return strings.raw[0]; }
var r = tag`hello\nworld`;
");
            Assert.That(root.GetParameter("r").String, Is.EqualTo("hello\\nworld"));
        }

        [Test]
        public void TaggedTemplate_CookedSegmentProcessesEscapes()
        {
            // \n in cooked should be actual newline character (char code 10)
            var root = Run(@"
function tag(strings) { return strings[0].charCodeAt(5); }
var r = tag`hello\nworld`;
");
            Assert.That(root.GetParameter("r").Int, Is.EqualTo(10)); // '\n'
        }

        [Test]
        public void TaggedTemplate_InterpolatedValuesPassedAsArgs()
        {
            var root = Run(@"
function tag(strings, a, b) { return a + b; }
var r = tag`${10}plus${32}`;
");
            Assert.That(root.GetParameter("r").Int, Is.EqualTo(42));
        }

        [Test]
        public void TaggedTemplate_NoInterpolations_SingleStringSegment()
        {
            var root = Run(@"
function tag(strings) { return strings.length; }
var r = tag`no interpolations here`;
");
            Assert.That(root.GetParameter("r").Int, Is.EqualTo(1));
        }

        [Test]
        public void TaggedTemplate_UntaggedTemplateStillWorks()
        {
            var root = Run("var x = 42; var r = `value is ${x}`;");
            Assert.That(root.GetParameter("r").String, Is.EqualTo("value is 42"));
        }

        // ── String.raw ────────────────────────────────────────────────────────

        [Test]
        public void StringRaw_ReturnsRawStringWithoutProcessingEscapes()
        {
            // \n should appear as literal \n in the output, not as a newline
            var root = Run(@"var r = String.raw`hello\nworld`;");
            Assert.That(root.GetParameter("r").String, Is.EqualTo("hello\\nworld"));
        }

        [Test]
        public void StringRaw_WithSubstitutions_InterleaveCorrectly()
        {
            var root = Run(@"var name = 'World'; var r = String.raw`Hello\t${name}!`;");
            Assert.That(root.GetParameter("r").String, Is.EqualTo("Hello\\tWorld!"));
        }

        [Test]
        public void StringRaw_NoInterpolation_ReturnsRawContent()
        {
            var root = Run(@"var r = String.raw`no escapes here`;");
            Assert.That(root.GetParameter("r").String, Is.EqualTo("no escapes here"));
        }
    }
}
