using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;
using Environment = DScript.Vm.Environment;

namespace DScript.Test
{
    // The comma (sequence) operator: each operand is evaluated left-to-right and the
    // value of the whole expression is the last operand. Valid wherever the grammar
    // allows a full Expression; ',' stays a SEPARATOR in argument/array/object/
    // declarator lists.
    public class CommaOperatorTests
    {
        private static ScriptVar Eval(string source)
            => new VirtualMachine().Run(new DScriptCompiler().CompileExpression(source));

        private static ScriptVar RunProgram(string source)
        {
            var chunk = new DScriptCompiler().CompileProgram(source);
            var globals = ScriptVar.CreateObject();
            new VirtualMachine().Run(chunk, new Environment(globals, null));
            return globals;
        }

        private static int IntOf(string source, string varName)
            => RunProgram(source).GetParameter(varName).Int;

        // ── value semantics ──────────────────────────────────────────────────────

        [TestCase("1, 2, 3", 3)]
        [TestCase("(1, 2, 3)", 3)]
        [TestCase("1 + 1, 2 + 2", 4)]
        [TestCase("(5), (6)", 6)]
        public void Sequence_YieldsLastOperand(string src, int expected)
            => Assert.That(Eval(src).Int, Is.EqualTo(expected));

        [Test]
        public void Sequence_EvaluatesEveryOperandLeftToRight()
        {
            // Side effects of all operands happen; the statement value is discarded.
            Assert.That(IntOf("var a = 0; var b = 0; a = 1, b = 2;", "a"), Is.EqualTo(1));
            Assert.That(IntOf("var a = 0; var b = 0; a = 1, b = 2;", "b"), Is.EqualTo(2));
        }

        [Test]
        public void Sequence_AssignmentBindsTighterThanComma()
        {
            // `x = (1, 2)` parses as x = 2 ; `x = 1, 2` parses as (x = 1), 2 -> x is 1.
            Assert.That(IntOf("var x; x = (1, 2);", "x"), Is.EqualTo(2));
            Assert.That(IntOf("var x; x = 1, 2;", "x"), Is.EqualTo(1));
        }

        // ── statement / control-flow contexts ──────────────────────────────────────

        [Test]
        public void Sequence_InExpressionStatement()
        {
            // The reported failing pattern: `cond || side(), other()` as a statement.
            const string src =
                "var hit = 0; var n = 0;" +
                "function side(){ hit = hit + 1; return 1; }" +
                "function other(){ n = n + 10; return 2; }" +
                "false || side(), other();";
            Assert.That(IntOf(src, "hit"), Is.EqualTo(1));
            Assert.That(IntOf(src, "n"), Is.EqualTo(10));
        }

        [Test]
        public void Sequence_InForIncrement()
        {
            Assert.That(IntOf(
                "var s = 0; var j = 100;" +
                "for (var i = 0; i < 5; i = i + 1, j = j - 1) { s = s + j; }",
                "j"), Is.EqualTo(95));
        }

        [Test]
        public void Sequence_InForInitAndIncrement()
        {
            Assert.That(IntOf(
                "var sum = 0; var i, k;" +
                "for (i = 0, k = 10; i < 3; i = i + 1, k = k + 1) { sum = sum + k; }",
                "sum"), Is.EqualTo(33)); // 10 + 11 + 12
        }

        [Test]
        public void Sequence_InWhileCondition()
        {
            // The condition value is the last operand (n < 3); the first has a side effect.
            Assert.That(IntOf(
                "var n = 0; var ticks = 0;" +
                "while (ticks = ticks + 1, n < 3) { n = n + 1; }",
                "ticks"), Is.EqualTo(4));
        }

        [Test]
        public void Sequence_InReturn()
        {
            Assert.That(IntOf(
                "var log = 0;" +
                "function f(){ return (log = 7, 42); }" +
                "var r = f();",
                "r"), Is.EqualTo(42));
            Assert.That(IntOf(
                "var log = 0;" +
                "function f(){ return (log = 7, 42); }" +
                "f();",
                "log"), Is.EqualTo(7));
        }

        // ── ',' must remain a SEPARATOR in these contexts ──────────────────────────

        [Test]
        public void Comma_StaysSeparatorInArgumentList()
        {
            // If comma were the sequence operator here, f would receive only `3`.
            Assert.That(IntOf(
                "function f(a, b){ return a * 100 + b; }" +
                "var r = f(1, 3);",
                "r"), Is.EqualTo(103));
        }

        [Test]
        public void Comma_StaysSeparatorInArrayLiteral()
            => Assert.That(IntOf("var a = [1, 2, 3]; var r = a.length;", "r"), Is.EqualTo(3));

        [Test]
        public void Comma_StaysSeparatorInVarDeclarators()
        {
            Assert.That(IntOf("var a = 1, b = 2, c = 3;", "a"), Is.EqualTo(1));
            Assert.That(IntOf("var a = 1, b = 2, c = 3;", "c"), Is.EqualTo(3));
        }

        [Test]
        public void Comma_StaysSeparatorInObjectLiteral()
            => Assert.That(IntOf("var o = { x: 1, y: 2 }; var r = o.x + o.y;", "r"), Is.EqualTo(3));

        [Test]
        public void Comma_TernaryArmsAreCommaFree()
        {
            // `x = cond ? a : b, c` parses as `(x = (cond ? a : b)), c` — the ternary
            // arm is an AssignmentExpression (no comma), so x gets the arm value (1)
            // and the trailing `9` is a discarded sequence operand.
            Assert.That(IntOf("var x; x = true ? 1 : 2, 9;", "x"), Is.EqualTo(1));
        }
    }
}
