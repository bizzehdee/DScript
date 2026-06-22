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
    /// <summary>Smoke tests for Phase 4: destructuring, rest params, and spread.</summary>
    public class Phase4Tests
    {
        private static ScriptVar Run(string source, ScriptEngine engine = null)
        {
            engine ??= new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            var vm = new VirtualMachine(engine);
            vm.Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root;
        }

        private static int RunInt(string source)
        {
            var engine = new ScriptEngine();
            Run(source, engine);
            return engine.Root.GetParameter("r").Int;
        }

        private static string RunStr(string source)
        {
            var engine = new ScriptEngine();
            Run(source, engine);
            return engine.Root.GetParameter("r").String;
        }

        private static bool RunBool(string source)
        {
            var engine = new ScriptEngine();
            Run(source, engine);
            return engine.Root.GetParameter("r").Bool;
        }

        // --- rest parameters ---

        [Test]
        public void RestParam_CollectsExtraArgs()
        {
            Assert.That(RunInt("function f(a, ...rest) { return rest.length; } var r = f(1, 2, 3);"), Is.EqualTo(2));
        }

        [Test]
        public void RestParam_EmptyWhenNoExtraArgs()
        {
            Assert.That(RunInt("function f(a, ...rest) { return rest.length; } var r = f(1);"), Is.EqualTo(0));
        }

        [Test]
        public void RestParam_FirstElement()
        {
            Assert.That(RunInt("function f(...args) { return args[0]; } var r = f(42, 99);"), Is.EqualTo(42));
        }

        [Test]
        public void RestParam_ArrowFunction()
        {
            Assert.That(RunInt("var f = (...args) => args.length; var r = f(1, 2, 3, 4);"), Is.EqualTo(4));
        }

        // --- spread in function calls ---

        [Test]
        public void SpreadCall_SpreadArrayIntoArgs()
        {
            Assert.That(RunInt("function add(a, b, c) { return a + b + c; } var arr = [1, 2, 3]; var r = add(...arr);"), Is.EqualTo(6));
        }

        [Test]
        public void SpreadCall_MixedArgsAndSpread()
        {
            Assert.That(RunInt("function f(a, b, c) { return a + b + c; } var arr = [2, 3]; var r = f(1, ...arr);"), Is.EqualTo(6));
        }

        // --- spread in array literals ---

        [Test]
        public void SpreadArray_CombinesTwoArrays()
        {
            Assert.That(RunInt("var a = [1, 2]; var b = [3, 4]; var c = [...a, ...b]; var r = c.length;"), Is.EqualTo(4));
        }

        [Test]
        public void SpreadArray_Element()
        {
            Assert.That(RunInt("var a = [1, 2]; var b = [...a, 3]; var r = b[2];"), Is.EqualTo(3));
        }

        // --- spread in object literals ---

        [Test]
        public void SpreadObject_CopiesProperties()
        {
            Assert.That(RunInt("var a = {x: 1, y: 2}; var b = {...a, z: 3}; var r = b.x + b.y + b.z;"), Is.EqualTo(6));
        }

        // --- array destructuring ---

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
        public void ArrayDestructuring_RestElement()
        {
            Assert.That(RunInt("var [a, ...rest] = [1, 2, 3, 4]; var r = rest.length;"), Is.EqualTo(3));
        }

        [Test]
        public void ArrayDestructuring_RestElement_FirstValue()
        {
            Assert.That(RunInt("var [a, ...rest] = [1, 2, 3]; var r = rest[0];"), Is.EqualTo(2));
        }

        // --- object destructuring ---

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
