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
    /// <summary>Tests for default parameter values in regular and arrow functions.</summary>
    public class DefaultParameterTests
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
        public void DefaultParam_UsedWhenNotProvided()
        {
            var src = "function greet(name, greeting = 'Hello') { return greeting + ' ' + name; } var r = greet('World');";
            Assert.That(RunStr(src), Is.EqualTo("Hello World"));
        }

        [Test]
        public void DefaultParam_OverriddenWhenProvided()
        {
            var src = "function greet(name, greeting = 'Hello') { return greeting + ' ' + name; } var r = greet('World', 'Hi');";
            Assert.That(RunStr(src), Is.EqualTo("Hi World"));
        }

        [Test]
        public void DefaultParam_ArrowFunction_UsesDefault()
        {
            var src = "var add = (a, b = 10) => a + b; var r = add(5);";
            Assert.That(RunInt(src), Is.EqualTo(15));
        }

        [Test]
        public void DefaultParam_ArrowFunction_OverriddenWhenProvided()
        {
            var src = "var add = (a, b = 10) => a + b; var r = add(5, 3);";
            Assert.That(RunInt(src), Is.EqualTo(8));
        }
    }
}
