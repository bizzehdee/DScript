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
    public class LogicalAssignmentTests
    {
        private static ScriptVar Run(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r");
        }

        private static int RunInt(string source) => Run(source).Int;
        private static bool RunBool(string source) => Run(source).Bool;
        private static string RunStr(string source) => Run(source).String;

        // --- &&= ---

        [Test]
        public void AndAndAssign_TruthyLhs_AssignsRhs()
        {
            Assert.That(RunInt("var r = 1; r &&= 99;"), Is.EqualTo(99));
        }

        [Test]
        public void AndAndAssign_FalsyLhs_KeepsLhs()
        {
            Assert.That(RunInt("var r = 0; r &&= 99;"), Is.EqualTo(0));
        }

        [Test]
        public void AndAndAssign_FalsyLhs_RhsNotEvaluated()
        {
            // side-effect counter stays 0 because RHS is never evaluated
            var r = RunInt(@"
                var side = 0;
                var r = 0;
                function inc() { side = side + 1; return 99; }
                r &&= inc();
                r = side;
            ");
            Assert.That(r, Is.EqualTo(0));
        }

        [Test]
        public void AndAndAssign_TruthyLhs_RhsEvaluatedOnce()
        {
            var r = RunInt(@"
                var side = 0;
                var r = 1;
                function inc() { side = side + 1; return 99; }
                r &&= inc();
                r = side;
            ");
            Assert.That(r, Is.EqualTo(1));
        }

        // --- ||= ---

        [Test]
        public void OrOrAssign_FalsyLhs_AssignsRhs()
        {
            Assert.That(RunInt("var r = 0; r ||= 42;"), Is.EqualTo(42));
        }

        [Test]
        public void OrOrAssign_TruthyLhs_KeepsLhs()
        {
            Assert.That(RunInt("var r = 7; r ||= 42;"), Is.EqualTo(7));
        }

        [Test]
        public void OrOrAssign_TruthyLhs_RhsNotEvaluated()
        {
            var r = RunInt(@"
                var side = 0;
                var r = 1;
                function inc() { side = side + 1; return 42; }
                r ||= inc();
                r = side;
            ");
            Assert.That(r, Is.EqualTo(0));
        }

        // --- ??= ---

        [Test]
        public void NullCoalesceAssign_NullLhs_AssignsRhs()
        {
            Assert.That(RunInt("var r = null; r ??= 55;"), Is.EqualTo(55));
        }

        [Test]
        public void NullCoalesceAssign_UndefinedLhs_AssignsRhs()
        {
            Assert.That(RunInt("var r; r ??= 55;"), Is.EqualTo(55));
        }

        [Test]
        public void NullCoalesceAssign_DefinedLhs_KeepsLhs()
        {
            Assert.That(RunInt("var r = 3; r ??= 55;"), Is.EqualTo(3));
        }

        [Test]
        public void NullCoalesceAssign_ZeroLhs_KeepsZero()
        {
            // 0 is defined (not null/undefined), so should not be replaced
            Assert.That(RunInt("var r = 0; r ??= 55;"), Is.EqualTo(0));
        }

        [Test]
        public void NullCoalesceAssign_DefinedLhs_RhsNotEvaluated()
        {
            var r = RunInt(@"
                var side = 0;
                var r = 5;
                function inc() { side = side + 1; return 55; }
                r ??= inc();
                r = side;
            ");
            Assert.That(r, Is.EqualTo(0));
        }

        // --- property access ---

        [Test]
        public void AndAndAssign_Property_TruthyAssigns()
        {
            Assert.That(RunInt(@"
                var obj = { x: 1 };
                obj.x &&= 99;
                var r = obj.x;
            "), Is.EqualTo(99));
        }

        [Test]
        public void AndAndAssign_Property_FalsyKeeps()
        {
            Assert.That(RunInt(@"
                var obj = { x: 0 };
                obj.x &&= 99;
                var r = obj.x;
            "), Is.EqualTo(0));
        }

        [Test]
        public void NullCoalesceAssign_Property_NullAssigns()
        {
            Assert.That(RunInt(@"
                var obj = { x: null };
                obj.x ??= 77;
                var r = obj.x;
            "), Is.EqualTo(77));
        }

        [Test]
        public void NullCoalesceAssign_Property_DefinedKeeps()
        {
            Assert.That(RunInt(@"
                var obj = { x: 3 };
                obj.x ??= 77;
                var r = obj.x;
            "), Is.EqualTo(3));
        }

        // --- index access ---

        [Test]
        public void OrOrAssign_Index_FalsyAssigns()
        {
            Assert.That(RunInt(@"
                var arr = [0, 0, 0];
                arr[1] ||= 88;
                var r = arr[1];
            "), Is.EqualTo(88));
        }

        [Test]
        public void OrOrAssign_Index_TruthyKeeps()
        {
            Assert.That(RunInt(@"
                var arr = [5, 5, 5];
                arr[1] ||= 88;
                var r = arr[1];
            "), Is.EqualTo(5));
        }
    }
}
