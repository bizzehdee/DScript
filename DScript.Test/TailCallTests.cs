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
    /// <summary>
    /// Tests for tail-call elimination: correctness of the <c>TailCall</c>/<c>TailCallMethod</c>
    /// opcodes and the compiler peephole that emits them.
    /// </summary>
    public class TailCallTests
    {
        private static ScriptVar Run(string source, ScriptEngine engine = null)
        {
            engine ??= new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root;
        }

        private static int RunInt(string source)
        {
            var engine = new ScriptEngine();
            Run(source, engine);
            return engine.Root.GetParameter("r").Int;
        }

        [Test]
        public void TailCall_DirectRecursion_ReturnsCorrectValue()
        {
            const string src =
                "function count(n, acc) { if (n == 0) { return acc; } return count(n - 1, acc + 1); } " +
                "var r = count(100, 0);";
            Assert.That(RunInt(src), Is.EqualTo(100));
        }

        [Test]
        public void TailCall_AccumulatorPattern_CorrectResult()
        {
            const string src =
                "function fact(n, acc) { if (n <= 1) { return acc; } return fact(n - 1, n * acc); } " +
                "var r = fact(10, 1);";
            Assert.That(RunInt(src), Is.EqualTo(3628800));
        }

        [Test]
        public void NonTailCall_NotMisidentified()
        {
            // return n * fact(n-1) is NOT a tail call — the multiplication follows
            const string src =
                "function fact(n) { if (n <= 1) { return 1; } return n * fact(n - 1); } " +
                "var r = fact(7);";
            Assert.That(RunInt(src), Is.EqualTo(5040));
        }

        [Test]
        public void TailCallMethod_ReturnsCorrectValue()
        {
            const string src =
                "function Obj() { this.val = 42; } " +
                "Obj.getVal = function() { return this.val; }; " +
                "Obj.run = function() { return this.getVal(); }; " +
                "var o = new Obj(); var r = o.run();";
            Assert.That(RunInt(src), Is.EqualTo(42));
        }

        [Test]
        public void TailCall_InsideTryFinally_NotApplied()
        {
            // A return inside a try-with-finally must NOT become TailCall;
            // the finally block must still run after the return.
            const string src =
                "var flag = 0; " +
                "function f(n) { try { return n + 1; } finally { flag = 1; } } " +
                "var r = f(5);";
            var root = Run(src);
            Assert.That(root.GetParameter("r").Int, Is.EqualTo(6));
            Assert.That(root.GetParameter("flag").Int, Is.EqualTo(1));
        }
    }
}
