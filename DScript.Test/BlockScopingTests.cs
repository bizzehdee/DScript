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
    /// Tests for block-scoped <c>let</c> and <c>const</c> declarations,
    /// shadowing, hoisting behaviour of <c>var</c>, and nested block scopes.
    /// </summary>
    public class BlockScopingTests
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

        // ── let basics ────────────────────────────────────────────────────────

        [Test]
        public void Let_Declaration_WorksLikeVar()
        {
            Assert.That(RunInt("let x = 42; var r = x;"), Is.EqualTo(42));
        }

        [Test]
        public void Let_Declaration_MultipleBindings()
        {
            Assert.That(RunInt("let a = 1, b = 2; var r = a + b;"), Is.EqualTo(3));
        }

        // ── let block scoping ─────────────────────────────────────────────────

        [Test]
        public void Let_InsideBlock_IsVisibleInsideBlock()
        {
            Assert.That(RunInt("{ let x = 42; var r = x; }"), Is.EqualTo(42));
        }

        [Test]
        public void Let_InsideBlock_AssignedValuePersistedBeforeLeave()
        {
            var src = "var r = 0; { let x = 99; r = x; } r = r + 1;";
            Assert.That(RunInt(src), Is.EqualTo(100));
        }

        [Test]
        public void Let_BlockWithoutLetConst_DoesNotPushNewScope()
        {
            // A block containing only var assignments should not push a new scope frame
            Assert.That(RunInt("var x = 5; { x = 10; } var r = x;"), Is.EqualTo(10));
        }

        [Test]
        public void Let_ShadowsOuterVar_OuterUnchanged()
        {
            Assert.That(RunInt("var x = 1; { let x = 99; } var r = x;"), Is.EqualTo(1));
        }

        [Test]
        public void Let_ShadowsOuterVar_InnerValueCorrect()
        {
            Assert.That(RunInt("var x = 1; var r = 0; { let x = 99; r = x; }"), Is.EqualTo(99));
        }

        [Test]
        public void Let_MultipleDeclarationsInBlock()
        {
            Assert.That(RunInt("var r = 0; { let a = 3; let b = 7; r = a + b; }"), Is.EqualTo(10));
        }

        // ── const immutability ────────────────────────────────────────────────

        [Test]
        public void Const_CanBeRead()
        {
            Assert.That(RunInt("const x = 42; var r = x;"), Is.EqualTo(42));
        }

        [Test]
        public void Const_ThrowsOnReassignment()
        {
            Assert.Throws<JITException>(() => RunInt("const x = 1; x = 2;"));
        }

        [Test]
        public void Const_BlockScoped_ThrowsOnReassignment()
        {
            Assert.Throws<JITException>(() => Run("{ const x = 1; x = 2; }"));
        }

        [Test]
        public void Const_InitialAssignmentAllowed()
        {
            Assert.That(RunInt("const x = 55; var r = x;"), Is.EqualTo(55));
        }

        // ── nested block scoping ──────────────────────────────────────────────

        [Test]
        public void Let_NestedBlocks_InnermostShadowsAll()
        {
            var src = @"
                var r = 0;
                let x = 1;
                { let x = 2; { let x = 3; r = x; } }
            ";
            Assert.That(RunInt(src), Is.EqualTo(3));
        }

        [Test]
        public void Let_NestedBlocks_OuterRestoredAfterInner()
        {
            var src = @"
                var r = 0;
                let x = 1;
                { let x = 2; { let x = 3; } r = x; }
            ";
            Assert.That(RunInt(src), Is.EqualTo(2));
        }

        [Test]
        public void Let_NestedBlocks_OutermostRestoredAfterBoth()
        {
            var src = @"
                let x = 1;
                { let x = 2; { let x = 3; } }
                var r = x;
            ";
            Assert.That(RunInt(src), Is.EqualTo(1));
        }

        // ── let in for loops ──────────────────────────────────────────────────

        [Test]
        public void Let_ForLoopInit_AccumulatesCorrectly()
        {
            Assert.That(RunInt("var r = 0; for (let i = 0; i < 5; i = i + 1) { r = r + i; }"), Is.EqualTo(10));
        }

        [Test]
        public void Let_InsideForBody_NewBindingEachIteration()
        {
            Assert.That(RunInt("var r = 0; for (var i = 0; i < 3; i = i + 1) { let v = i * 2; r = r + v; }"), Is.EqualTo(6));
        }

        // ── var still hoists past block scopes ────────────────────────────────

        [Test]
        public void Var_StillVisibleOutsideBlock()
        {
            Assert.That(RunInt("{ var x = 7; } var r = x;"), Is.EqualTo(7));
        }

        [Test]
        public void Var_ForLoopCounter_FunctionScoped()
        {
            Assert.That(RunInt("for (var i = 0; i < 3; i = i + 1) {} var r = i;"), Is.EqualTo(3));
        }

        // ── let in if/else ────────────────────────────────────────────────────

        [Test]
        public void Let_InIfBlock_ScopedToIf()
        {
            Assert.That(RunInt("var r = 0; if (true) { let x = 5; r = x; }"), Is.EqualTo(5));
        }

        [Test]
        public void Let_InIfElse_EachBranchIndependent()
        {
            Assert.That(RunInt("var r = 0; if (false) { let x = 1; r = x; } else { let x = 2; r = x; }"), Is.EqualTo(2));
        }
    }
}
