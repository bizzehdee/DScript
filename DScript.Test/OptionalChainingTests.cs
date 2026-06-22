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
    /// <summary>Tests for the optional chaining operator <c>?.</c> in member, index, and call forms.</summary>
    public class OptionalChainingTests
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

        private static bool IsUndefined(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r").IsUndefined;
        }

        [Test]
        public void OptionalChain_MemberOnNull_ReturnsUndefined()
        {
            Assert.That(IsUndefined("var obj = null; var r = obj?.name;"), Is.True);
        }

        [Test]
        public void OptionalChain_MemberOnUndefined_ReturnsUndefined()
        {
            Assert.That(IsUndefined("var obj; var r = obj?.name;"), Is.True);
        }

        [Test]
        public void OptionalChain_MemberOnObject_ReturnsValue()
        {
            Assert.That(RunInt("var obj = {name: 42}; var r = obj?.name;"), Is.EqualTo(42));
        }

        [Test]
        public void OptionalChain_ChainedAccess()
        {
            Assert.That(RunInt("var a = {b: {c: 7}}; var r = a?.b?.c;"), Is.EqualTo(7));
        }

        [Test]
        public void OptionalChain_ChainedAccess_BreaksOnNull()
        {
            Assert.That(IsUndefined("var a = {b: null}; var r = a?.b?.c;"), Is.True);
        }

        [Test]
        public void OptionalChain_IndexAccess()
        {
            Assert.That(RunInt("var a = [1,2,3]; var r = a?.[1];"), Is.EqualTo(2));
        }

        [Test]
        public void OptionalChain_IndexOnNull_ReturnsUndefined()
        {
            Assert.That(IsUndefined("var a = null; var r = a?.[0];"), Is.True);
        }

        [Test]
        public void OptionalChain_CombinedWithNullCoalesce()
        {
            Assert.That(RunStr("var obj = null; var r = obj?.name ?? \"anon\";"), Is.EqualTo("anon"));
        }
    }
}
