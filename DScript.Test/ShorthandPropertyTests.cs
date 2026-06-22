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
    /// <summary>Tests for shorthand object property syntax: <c>{ x, y }</c>.</summary>
    public class ShorthandPropertyTests
    {
        private static int RunInt(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r").Int;
        }

        [Test]
        public void ShorthandObjectProperty_SingleKey()
        {
            var src = "var x = 42; var obj = {x}; var r = obj.x;";
            Assert.That(RunInt(src), Is.EqualTo(42));
        }

        [Test]
        public void ShorthandObjectProperty_MultipleKeys()
        {
            var src = "var a = 1; var b = 2; var obj = {a, b}; var r = obj.a + obj.b;";
            Assert.That(RunInt(src), Is.EqualTo(3));
        }

        [Test]
        public void ShorthandObjectProperty_Mixed()
        {
            var src = "var x = 10; var obj = {x, y: 20}; var r = obj.x + obj.y;";
            Assert.That(RunInt(src), Is.EqualTo(30));
        }
    }
}
