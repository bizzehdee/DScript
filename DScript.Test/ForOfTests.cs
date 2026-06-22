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
    /// <summary>Tests for the <c>for...of</c> loop over arrays and generator iterators.</summary>
    public class ForOfTests
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
        public void ForOf_Array_SumsAllElements()
        {
            Assert.That(RunInt("var r = 0; for (var x of [1, 2, 3, 4, 5]) { r += x; }"), Is.EqualTo(15));
        }

        [Test]
        public void ForOf_Array_ConcatenatesStrings()
        {
            Assert.That(RunStr("var r = ''; for (var s of ['a', 'b', 'c']) { r += s; }"), Is.EqualTo("abc"));
        }

        [Test]
        public void ForOf_Generator_IteratesYieldedValues()
        {
            var src = @"
                function* range(n) {
                    var i = 0;
                    while (i < n) { yield i; i++; }
                }
                var r = 0;
                for (var v of range(5)) { r += v; }
            ";
            Assert.That(RunInt(src), Is.EqualTo(10)); // 0+1+2+3+4
        }

        [Test]
        public void ForOf_Break_StopsIteration()
        {
            var src = @"
                var r = 0;
                for (var x of [10, 20, 30, 40, 50]) {
                    r++;
                    if (x === 30) break;
                }
            ";
            Assert.That(RunInt(src), Is.EqualTo(3));
        }

        [Test]
        public void ForOf_Continue_SkipsCurrentBody()
        {
            var src = @"
                var r = 0;
                for (var x of [1, 2, 3, 4, 5]) {
                    if (x === 3) continue;
                    r += x;
                }
            ";
            Assert.That(RunInt(src), Is.EqualTo(12)); // 1+2+4+5
        }
    }
}
