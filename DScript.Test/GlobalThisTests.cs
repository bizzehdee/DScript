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
    [TestFixture]
    public class GlobalThisTests
    {
        private static ScriptVar Run(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r");
        }

        [Test]
        public void GlobalThis_AccessGlobalVar()
        {
            // var x = 1; globalThis.x should equal 1
            var r = Run("var x = 1; var r = globalThis.x;").Int;
            Assert.That(r, Is.EqualTo(1));
        }

        [Test]
        public void GlobalThis_SetGlobalVar()
        {
            // setting via globalThis should be visible as a top-level var
            var r = Run("globalThis.r = 42;").Int;
            Assert.That(r, Is.EqualTo(42));
        }

        [Test]
        public void GlobalThis_IsSelfIdentical()
        {
            // globalThis === globalThis should be true
            var r = Run("var r = globalThis === globalThis ? 1 : 0;").Int;
            Assert.That(r, Is.EqualTo(1));
        }
    }
}
