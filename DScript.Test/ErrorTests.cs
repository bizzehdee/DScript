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
    public class ErrorTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        // ── instanceof chain ──────────────────────────────────────────────────

        [Test]
        public void TypeError_IsInstanceOfTypeError()
        {
            var r = RunScript("var e = new TypeError('msg'); __result__ = e instanceof TypeError ? 'yes' : 'no';");
            Assert.That(r.String, Is.EqualTo("yes"));
        }

        [Test]
        public void TypeError_IsInstanceOfError()
        {
            var r = RunScript("var e = new TypeError('msg'); __result__ = e instanceof Error ? 'yes' : 'no';");
            Assert.That(r.String, Is.EqualTo("yes"));
        }

        [Test]
        public void RangeError_IsInstanceOfError()
        {
            var r = RunScript("var e = new RangeError('msg'); __result__ = e instanceof Error ? 'yes' : 'no';");
            Assert.That(r.String, Is.EqualTo("yes"));
        }

        [Test]
        public void ReferenceError_IsInstanceOfError()
        {
            var r = RunScript("var e = new ReferenceError('msg'); __result__ = e instanceof Error ? 'yes' : 'no';");
            Assert.That(r.String, Is.EqualTo("yes"));
        }

        [Test]
        public void SyntaxError_IsInstanceOfError()
        {
            var r = RunScript("var e = new SyntaxError('msg'); __result__ = e instanceof Error ? 'yes' : 'no';");
            Assert.That(r.String, Is.EqualTo("yes"));
        }

        // ── stack property ────────────────────────────────────────────────────

        [Test]
        public void Error_StackProperty_IsString()
        {
            var r = RunScript("var e = new Error('x'); __result__ = typeof e.stack;");
            Assert.That(r.String, Is.EqualTo("string"));
        }

        [Test]
        public void Error_StackProperty_ContainsErrorTypeName()
        {
            var r = RunScript("var e = new TypeError('x'); __result__ = e.stack.indexOf('TypeError') >= 0 ? 'yes' : 'no';");
            Assert.That(r.String, Is.EqualTo("yes"));
        }

        // ── message and name ──────────────────────────────────────────────────

        [Test]
        public void Error_MessageProperty_IsPreserved()
        {
            var r = RunScript("var e = new Error('hello world'); __result__ = e.message;");
            Assert.That(r.String, Is.EqualTo("hello world"));
        }

        [Test]
        public void TypeError_NameProperty_IsTypeName()
        {
            var r = RunScript("var e = new TypeError('x'); __result__ = e.name;");
            Assert.That(r.String, Is.EqualTo("TypeError"));
        }
    }
}
