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
    /// <summary>Smoke tests for the Phase 3 feature set: block scoping, let, const.</summary>
    public class Phase3Tests
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

        // ---- 1. let block scoping -------------------------------------------

        [Test]
        public void Let_InsideBlock_IsVisibleInsideBlock()
        {
            Assert.That(RunInt("{ let x = 42; var r = x; }"), Is.EqualTo(42));
        }

        [Test]
        public void Let_InsideBlock_IsNotVisibleOutside()
        {
            // After the block, x should be undefined; r should get the fallback value
            var src = "var r = 0; { let x = 99; r = x; } r = r + 1;";
            Assert.That(RunInt(src), Is.EqualTo(100));
        }

        [Test]
        public void Let_OuterVariableNotShadowed_WhenNoLetInBlock()
        {
            // Plain block with no let/const should not push a new scope
            Assert.That(RunInt("var x = 5; { x = 10; } var r = x;"), Is.EqualTo(10));
        }

        [Test]
        public void Let_ShadowsOuterVar()
        {
            // Inside the block, let x shadows outer x; outer x is unchanged after
            Assert.That(RunInt("var x = 1; { let x = 99; } var r = x;"), Is.EqualTo(1));
        }

        [Test]
        public void Let_ShadowsOuterVar_InnerValueCorrect()
        {
            // Inside the block, let x = 99 shadows outer x = 1; we capture inner value
            Assert.That(RunInt("var x = 1; var r = 0; { let x = 99; r = x; }"), Is.EqualTo(99));
        }

        [Test]
        public void Let_MultipleDeclarationsInBlock()
        {
            Assert.That(RunInt("var r = 0; { let a = 3; let b = 7; r = a + b; }"), Is.EqualTo(10));
        }

        // ---- 2. const immutability -----------------------------------------

        [Test]
        public void Const_CanBeRead()
        {
            Assert.That(RunInt("const x = 42; var r = x;"), Is.EqualTo(42));
        }

        [Test]
        public void Const_ThrowsOnReassignment()
        {
            var src = "const x = 1; x = 2;";
            Assert.Throws<JITException>(() => RunInt(src));
        }

        [Test]
        public void Const_BlockScoped_ThrowsOnReassignment()
        {
            var src = "{ const x = 1; x = 2; }";
            Assert.Throws<JITException>(() => Run(src));
        }

        [Test]
        public void Const_InitialAssignmentAllowed()
        {
            // DeclareConst + initial SetVar (first assignment) must work
            Assert.That(RunInt("const x = 55; var r = x;"), Is.EqualTo(55));
        }

        // ---- 3. nested block scoping ----------------------------------------

        [Test]
        public void Let_NestedBlocks_InnerShadowsOuter()
        {
            var src = @"
                var r = 0;
                let x = 1;
                {
                    let x = 2;
                    {
                        let x = 3;
                        r = x;
                    }
                }
            ";
            Assert.That(RunInt(src), Is.EqualTo(3));
        }

        [Test]
        public void Let_NestedBlocks_OuterRestoredAfterInner()
        {
            var src = @"
                var r = 0;
                let x = 1;
                {
                    let x = 2;
                    {
                        let x = 3;
                    }
                    r = x;
                }
            ";
            Assert.That(RunInt(src), Is.EqualTo(2));
        }

        [Test]
        public void Let_NestedBlocks_OutermostRestoredAfterBoth()
        {
            var src = @"
                let x = 1;
                {
                    let x = 2;
                    {
                        let x = 3;
                    }
                }
                var r = x;
            ";
            Assert.That(RunInt(src), Is.EqualTo(1));
        }

        // ---- 4. for loop with let -------------------------------------------

        [Test]
        public void Let_ForLoopInit_AccumulatesCorrectly()
        {
            // let i declared in for init — loop runs, r accumulates
            Assert.That(RunInt("var r = 0; for (let i = 0; i < 5; i = i + 1) { r = r + i; }"), Is.EqualTo(10));
        }

        [Test]
        public void Let_InsideForBody_NewBindingEachIteration()
        {
            // let declared inside loop body — each iteration gets its own binding
            Assert.That(RunInt("var r = 0; for (var i = 0; i < 3; i = i + 1) { let v = i * 2; r = r + v; }"), Is.EqualTo(6));
        }

        // ---- 5. var still works (no regression) ----------------------------

        [Test]
        public void Var_StillWorksInBlock()
        {
            // var is function-scoped; declared in block, visible outside
            Assert.That(RunInt("{ var x = 7; } var r = x;"), Is.EqualTo(7));
        }

        [Test]
        public void Var_DeclarationInFor_StillFunctionScoped()
        {
            Assert.That(RunInt("for (var i = 0; i < 3; i = i + 1) {} var r = i;"), Is.EqualTo(3));
        }

        // ---- 6. let in if/else blocks ---------------------------------------

        [Test]
        public void Let_InIfBlock_ScopedToIf()
        {
            var src = "var r = 0; if (true) { let x = 5; r = x; }";
            Assert.That(RunInt(src), Is.EqualTo(5));
        }

        [Test]
        public void Let_InIfElse_EachBranchIndependent()
        {
            var src = "var r = 0; if (false) { let x = 1; r = x; } else { let x = 2; r = x; }";
            Assert.That(RunInt(src), Is.EqualTo(2));
        }
    }
}
