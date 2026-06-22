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
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    /// <summary>Tests for array and object destructuring in declarations and assignments.</summary>
    public class DestructuringTests
    {
        private static int RunInt(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r").Int;
        }

        // ── array destructuring ───────────────────────────────────────────────

        [Test]
        public void ArrayDestructuring_BasicPattern()
        {
            Assert.That(RunInt("var [a, b] = [1, 2]; var r = a + b;"), Is.EqualTo(3));
        }

        [Test]
        public void ArrayDestructuring_WithDefault()
        {
            Assert.That(RunInt("var [a, b = 10] = [1]; var r = a + b;"), Is.EqualTo(11));
        }

        [Test]
        public void ArrayDestructuring_RestElement_Length()
        {
            Assert.That(RunInt("var [a, ...rest] = [1, 2, 3, 4]; var r = rest.length;"), Is.EqualTo(3));
        }

        [Test]
        public void ArrayDestructuring_RestElement_FirstValue()
        {
            Assert.That(RunInt("var [a, ...rest] = [1, 2, 3]; var r = rest[0];"), Is.EqualTo(2));
        }

        // ── object destructuring ──────────────────────────────────────────────

        [Test]
        public void ObjectDestructuring_BasicPattern()
        {
            Assert.That(RunInt("var {x, y} = {x: 1, y: 2}; var r = x + y;"), Is.EqualTo(3));
        }

        [Test]
        public void ObjectDestructuring_Rename()
        {
            Assert.That(RunInt("var {x: a, y: b} = {x: 10, y: 20}; var r = a + b;"), Is.EqualTo(30));
        }

        [Test]
        public void ObjectDestructuring_WithDefault()
        {
            Assert.That(RunInt("var {x, y = 5} = {x: 10}; var r = x + y;"), Is.EqualTo(15));
        }
    }
}
