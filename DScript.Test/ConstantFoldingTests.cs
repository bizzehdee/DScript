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
    /// <summary>Tests for compile-time constant folding of string and numeric literals.</summary>
    public class ConstantFoldingTests
    {
        private static string RunStr(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r").String;
        }

        [Test]
        public void StringConstantFolding_TwoLiterals()
        {
            var src = "var r = \"hello\" + \" world\";";
            Assert.That(RunStr(src), Is.EqualTo("hello world"));
        }

        [Test]
        public void StringConstantFolding_ThreeLiterals()
        {
            var src = "var r = \"a\" + \"b\" + \"c\";";
            Assert.That(RunStr(src), Is.EqualTo("abc"));
        }

        // --- Constant propagation tests ---

        [Test]
        public void ConstantPropagation_IntLiteral()
        {
            // const x = 5 should make uses of x compile to Constant 5
            var src = "const x = 5; var r = x;";
            Assert.That(RunStr(src), Is.EqualTo("5"));
        }

        [Test]
        public void ConstantPropagation_FoldsWithExpression()
        {
            // x + 1 should fold entirely to 6 at compile time
            var src = "const x = 5; var r = x + 1;";
            Assert.That(RunStr(src), Is.EqualTo("6"));
        }

        [Test]
        public void ConstantPropagation_StringLiteral()
        {
            var src = "const greeting = \"hello\"; var r = greeting + \" world\";";
            Assert.That(RunStr(src), Is.EqualTo("hello world"));
        }

        [Test]
        public void ConstantPropagation_ChainedConsts()
        {
            // y gets x's propagated value, so both fold
            var src = "const x = 10; const y = x; var r = y;";
            Assert.That(RunStr(src), Is.EqualTo("10"));
        }

        [Test]
        public void ConstantPropagation_InnerBlockShadows()
        {
            // The inner const x = 10 shadows outer x = 5 inside the block
            var src = "const x = 5; var r = 0; { const x = 10; r = x; }";
            Assert.That(RunStr(src), Is.EqualTo("10"));
        }

        [Test]
        public void ConstantPropagation_OuterConstVisibleAfterBlock()
        {
            // After the inner block, x should revert to 5
            var src = "const x = 5; { const x = 10; } var r = x;";
            Assert.That(RunStr(src), Is.EqualTo("5"));
        }

        [Test]
        public void ConstantPropagation_FunctionParameterBarrier()
        {
            // Inside f, x is the parameter (10), not the outer const (5)
            var src = "const x = 5; function f(x) { return x; } var r = f(10);";
            Assert.That(RunStr(src), Is.EqualTo("10"));
        }

        [Test]
        public void ConstantPropagation_ArrowFunctionParameterBarrier()
        {
            // Arrow function parameter barriers outer const
            var src = "const x = 5; var f = (x) => x; var r = f(99);";
            Assert.That(RunStr(src), Is.EqualTo("99"));
        }

        [Test]
        public void ConstantPropagation_FunctionSeesOuterConst()
        {
            // A function can capture an outer const that it does NOT shadow
            var src = "const PI = 3; function area(r) { return PI * r * r; } var r = area(2);";
            Assert.That(RunStr(src), Is.EqualTo("12"));
        }

        // --- Constant branch-fold tests ---

        [Test]
        public void ConstantBranchFold_IfTrue_BodyExecutes()
        {
            // if (true) branch taken; else body becomes dead and is swept away
            var src = "var r; if (true) { r = 1; } else { r = 2; }";
            Assert.That(RunStr(src), Is.EqualTo("1"));
        }

        [Test]
        public void ConstantBranchFold_IfFalse_ElseExecutes()
        {
            // if (false) body becomes dead; else branch executes
            var src = "var r; if (false) { r = 1; } else { r = 2; }";
            Assert.That(RunStr(src), Is.EqualTo("2"));
        }

        [Test]
        public void ConstantBranchFold_WhileTrue_BreaksOnCondition()
        {
            // while (true) folds to an unconditional loop; break exits it correctly
            var src = "var i = 0; while (true) { i = i + 1; if (i >= 5) break; } var r = i;";
            Assert.That(RunStr(src), Is.EqualTo("5"));
        }

        [Test]
        public void ConstantBranchFold_ConstPropThenFold()
        {
            // const propagation substitutes the literal; branch fold removes the conditional jump
            var src = "const ACTIVE = true; var r; if (ACTIVE) { r = 42; } else { r = 0; }";
            Assert.That(RunStr(src), Is.EqualTo("42"));
        }

        [Test]
        public void ConstantBranchFold_ConstFalsePropThenFold()
        {
            // const propagation + fold when constant is false: else branch wins
            var src = "const FLAG = false; var r; if (FLAG) { r = 1; } else { r = 99; }";
            Assert.That(RunStr(src), Is.EqualTo("99"));
        }

        [Test]
        public void ConstantBranchFold_NestedFolds()
        {
            // Both conditionals fold independently
            var src = "var r = 0; if (true) { r = r + 10; } if (false) { r = r + 1; } else { r = r + 2; }";
            Assert.That(RunStr(src), Is.EqualTo("12"));
        }
    }
}
