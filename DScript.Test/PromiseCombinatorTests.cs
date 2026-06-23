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
    public class PromiseCombinatorTests
    {
        private static int RunInt(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new DScript.Vm.Environment(engine.Root, null));
            ScriptEngine.DrainMicroTasks();
            return engine.Root.GetParameter("r").Int;
        }

        private static string RunStr(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new DScript.Vm.Environment(engine.Root, null));
            ScriptEngine.DrainMicroTasks();
            return engine.Root.GetParameter("r").String;
        }

        private static bool RunBool(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new DScript.Vm.Environment(engine.Root, null));
            ScriptEngine.DrainMicroTasks();
            return engine.Root.GetParameter("r").Bool;
        }

        // --- Promise.all ---

        [Test]
        public void Promise_All_AllResolve_ResolvesWithArray()
        {
            var r = RunInt(@"
                var r = 0;
                Promise.all([Promise.resolve(1), Promise.resolve(2), Promise.resolve(3)])
                    .then(function(arr) { r = arr[0] + arr[1] + arr[2]; });
            ");
            Assert.That(r, Is.EqualTo(6));
        }

        [Test]
        public void Promise_All_EmptyArray_ResolvesWithEmpty()
        {
            var r = RunInt(@"
                var r = 0;
                Promise.all([]).then(function(arr) { r = arr.length; });
            ");
            Assert.That(r, Is.EqualTo(0));
        }

        [Test]
        public void Promise_All_OneRejects_RejectsCombined()
        {
            var r = RunInt(@"
                var r = 0;
                Promise.all([Promise.resolve(1), Promise.reject('fail')])
                    .catch(function(e) { r = 99; });
            ");
            Assert.That(r, Is.EqualTo(99));
        }

        // --- Promise.allSettled ---

        [Test]
        public void Promise_AllSettled_MixedResults_AlwaysResolves()
        {
            var r = RunInt(@"
                var r = 0;
                Promise.allSettled([Promise.resolve(1), Promise.reject('err')])
                    .then(function(arr) { r = arr.length; });
            ");
            Assert.That(r, Is.EqualTo(2));
        }

        [Test]
        public void Promise_AllSettled_FulfilledEntry_HasStatusFulfilled()
        {
            var r = RunStr(@"
                var r = '';
                Promise.allSettled([Promise.resolve(42)])
                    .then(function(arr) { r = arr[0].status; });
            ");
            Assert.That(r, Is.EqualTo("fulfilled"));
        }

        [Test]
        public void Promise_AllSettled_RejectedEntry_HasStatusRejected()
        {
            var r = RunStr(@"
                var r = '';
                Promise.allSettled([Promise.reject('nope')])
                    .then(function(arr) { r = arr[0].status; });
            ");
            Assert.That(r, Is.EqualTo("rejected"));
        }

        // --- Promise.race ---

        [Test]
        public void Promise_Race_FirstSettles_ResolvesWithFirst()
        {
            var r = RunInt(@"
                var r = 0;
                Promise.race([Promise.resolve(7), Promise.resolve(8)])
                    .then(function(v) { r = v; });
            ");
            Assert.That(r, Is.EqualTo(7));
        }

        [Test]
        public void Promise_Race_FirstRejects_RejectsRace()
        {
            var r = RunInt(@"
                var r = 0;
                Promise.race([Promise.reject('fail'), Promise.resolve(1)])
                    .catch(function(e) { r = 55; });
            ");
            Assert.That(r, Is.EqualTo(55));
        }

        // --- Promise.any ---

        [Test]
        public void Promise_Any_FirstFulfills_ResolvesWith()
        {
            var r = RunInt(@"
                var r = 0;
                Promise.any([Promise.reject('a'), Promise.resolve(5)])
                    .then(function(v) { r = v; });
            ");
            Assert.That(r, Is.EqualTo(5));
        }

        [Test]
        public void Promise_Any_AllReject_RejectsWithAggregateError()
        {
            var r = RunInt(@"
                var r = 0;
                Promise.any([Promise.reject('a'), Promise.reject('b')])
                    .catch(function(e) { r = 77; });
            ");
            Assert.That(r, Is.EqualTo(77));
        }

        [Test]
        public void Promise_Any_EmptyArray_Rejects()
        {
            var r = RunInt(@"
                var r = 0;
                Promise.any([]).catch(function(e) { r = 88; });
            ");
            Assert.That(r, Is.EqualTo(88));
        }
    }
}
