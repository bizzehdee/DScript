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
    public class TopLevelAwaitTests
    {
        private static int RunInt(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            ScriptEngine.DrainMicroTasks();
            return engine.Root.GetParameter("r").Int;
        }

        // --- Basic top-level await ---

        [Test]
        public void TopLevelAwait_AwaitResolvedPromise()
        {
            var r = RunInt(@"
var r = await Promise.resolve(42);
");
            Assert.That(r, Is.EqualTo(42));
        }

        [Test]
        public void TopLevelAwait_AwaitChainedPromises()
        {
            var r = RunInt(@"
var a = await Promise.resolve(10);
var b = await Promise.resolve(20);
var r = a + b;
");
            Assert.That(r, Is.EqualTo(30));
        }

        [Test]
        public void TopLevelAwait_ValueAccessibleAfterDrain()
        {
            var r = RunInt(@"
var r = 0;
var p = Promise.resolve(7);
r = await p;
");
            Assert.That(r, Is.EqualTo(7));
        }

        // --- Regular await inside functions is NOT treated as top-level ---

        [Test]
        public void RegularAwaitInAsyncFunction_StillWorks()
        {
            var r = RunInt(@"
var r = 0;
async function load() {
    r = await Promise.resolve(99);
}
load();
");
            Assert.That(r, Is.EqualTo(99));
        }

        // --- Programs without await are unaffected ---

        [Test]
        public void ProgramWithNoAwait_CompilesNormally()
        {
            var r = RunInt(@"
var r = 1 + 2;
");
            Assert.That(r, Is.EqualTo(3));
        }

        // --- Edge case: await in string literal does NOT trigger wrapper ---

        [Test]
        public void AwaitInStringLiteral_NotTopLevelAwait()
        {
            // The word "await" is inside a string — should NOT trigger async wrapping
            var r = RunInt(@"
var s = ""await"";
var r = s.length;
");
            Assert.That(r, Is.EqualTo(5));
        }
    }
}
