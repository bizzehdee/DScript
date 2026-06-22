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
    /// <summary>Tests for the nullish coalescing operator <c>??</c>.</summary>
    public class NullishCoalescingTests
    {
        private static int RunInt(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r").Int;
        }

        private static string RunStr(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r").String;
        }

        [Test]
        public void NullCoalesce_NullGivesRhs()
        {
            Assert.That(RunStr("var x = null; var r = x ?? \"default\";"), Is.EqualTo("default"));
        }

        [Test]
        public void NullCoalesce_UndefinedGivesRhs()
        {
            Assert.That(RunStr("var x; var r = x ?? \"default\";"), Is.EqualTo("default"));
        }

        [Test]
        public void NullCoalesce_DefinedKeepsLhs()
        {
            Assert.That(RunInt("var x = 42; var r = x ?? 99;"), Is.EqualTo(42));
        }

        [Test]
        public void NullCoalesce_FalsyZeroKeepsLhs()
        {
            // 0 is not null/undefined — ?? must keep it
            Assert.That(RunInt("var x = 0; var r = x ?? 99;"), Is.EqualTo(0));
        }

        [Test]
        public void NullCoalesce_EmptyStringKeepsLhs()
        {
            Assert.That(RunStr("var x = \"\"; var r = x ?? \"fallback\";"), Is.EqualTo(""));
        }
    }
}
