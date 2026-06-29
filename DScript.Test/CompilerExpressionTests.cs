using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    // Phase 2: compile expression source to bytecode and run it on the VM.
    public class CompilerExpressionTests
    {
        private static ScriptVar Eval(string source)
        {
            var chunk = new DScriptCompiler().CompileExpression(source);
            return new VirtualMachine().Run(chunk);
        }

        [TestCase("2 + 3 * 4", 14)]
        [TestCase("(2 + 3) * 4", 20)]
        [TestCase("10 - 2 - 3", 5)]
        [TestCase("17 % 5", 2)]
        [TestCase("1 << 4", 16)]
        [TestCase("~0", -1)]
        [TestCase("-5", -5)]
        [TestCase("6 & 3", 2)]
        [TestCase("1 | 4", 5)]
        public void Eval_IntegerExpressions(string src, int expected)
        {
            Assert.That(Eval(src).Int, Is.EqualTo(expected));
        }

        [TestCase("1 < 2", true)]
        [TestCase("2 < 1", false)]
        [TestCase("2 == 2", true)]
        [TestCase("2 != 2", false)]
        [TestCase("true && false", false)]
        [TestCase("true && 5", true)]
        [TestCase("false || 0", false)]
        [TestCase("!0", true)]
        [TestCase("!5", false)]
        public void Eval_BooleanExpressions(string src, bool expected)
        {
            Assert.That(Eval(src).Bool, Is.EqualTo(expected));
        }

        [Test]
        public void Eval_ShortCircuitReturnsOperand()
        {
            Assert.That(Eval("false || 7").Int, Is.EqualTo(7));
            Assert.That(Eval("3 && 9").Int, Is.EqualTo(9));
        }

        [Test]
        public void Eval_UnaryPlusCoercesString()
        {
            Assert.That(Eval("+\"42\"").Int, Is.EqualTo(42));
        }

        [Test]
        public void Eval_Ternary()
        {
            Assert.That(Eval("1 ? 100 : 200").Int, Is.EqualTo(100));
            Assert.That(Eval("0 ? 100 : 200").Int, Is.EqualTo(200));
        }

        // --- Constant;Binary -> BinaryConst fusion correctness ------------------

        // A ternary whose arms are literals must NOT be mis-fused with a
        // surrounding binary op: the right operand is the whole ternary (control
        // flow), not the literal it happens to end on.
        [TestCase("1 + (1 ? 10 : 20)", 11)]
        [TestCase("1 + (0 ? 10 : 20)", 21)]
        [TestCase("100 - (0 ? 1 : 2)", 98)]
        public void Eval_BinaryWithTernaryOperandNotMisfused(string src, int expected)
        {
            Assert.That(Eval(src).Int, Is.EqualTo(expected));
        }

        // The int fast path in BinaryConst must reproduce MathsOp's int semantics
        // exactly, including JS-style division/modulo by zero.
        [TestCase("10 / 3", 3)]
        [TestCase("10 % 3", 1)]
        [TestCase("7 * 6", 42)]
        [TestCase("8 - 100", -92)]
        public void Eval_BinaryConstIntSemantics(string src, int expected)
        {
            Assert.That(Eval(src).Int, Is.EqualTo(expected));
        }

        [Test]
        public void Eval_BinaryConstDivAndModByZeroMatchMathsOp()
        {
            // int / 0 -> +/-Infinity (double), int % 0 -> NaN — same as the
            // unfused path, just reached through BinaryConst.
            Assert.That(double.IsPositiveInfinity(Eval("5 / 0").Float), Is.True);
            Assert.That(double.IsNaN(Eval("5 % 0").Float), Is.True);
        }

        [Test]
        public void Eval_BinaryConstFallsBackForNonIntOperand()
        {
            // double op int-literal and string op string-literal take the
            // MathsOp fallback (constant.Kind != Int or a not int).
            Assert.That(Eval("2.5 + 1").Float, Is.EqualTo(3.5).Within(1e-9));
            Assert.That(Eval("\"x\" + 1").String, Is.EqualTo("x1"));
        }

        [Test]
        public void Eval_StringConcatAndTypeof()
        {
            Assert.That(Eval("\"a\" + \"b\"").String, Is.EqualTo("ab"));
            Assert.That(Eval("typeof 5").String, Is.EqualTo("number"));
            Assert.That(Eval("typeof \"x\"").String, Is.EqualTo("string"));
        }

        [Test]
        public void Eval_VoidOperator_YieldsUndefined()
        {
            // void evaluates its operand and produces undefined — previously the lexer
            // had no `void` keyword, so `void 0` was a parse error (ES-COMPATIBILITY
            // listed it as supported). Covers operator forms and that `typeof void` is
            // "undefined".
            Assert.That(Eval("void 0").IsUndefined, Is.True);
            Assert.That(Eval("void \"anything\"").IsUndefined, Is.True);
            Assert.That(Eval("void 0 === undefined").Bool, Is.True);
            Assert.That(Eval("typeof void 0").String, Is.EqualTo("undefined"));
        }

        [Test]
        public void Eval_VoidOperator_EvaluatesOperandSideEffect()
        {
            // The operand must still run (for its side effects); only the value is discarded.
            var v = Eval("(function(){ var n = 1; var r = void (n = 7); return [r === undefined, n]; })()");
            Assert.That(v.GetArrayIndex(0).Bool, Is.True);
            Assert.That(v.GetArrayIndex(1).Int, Is.EqualTo(7));
        }

        [Test]
        public void Eval_VoidIsValidPropertyNameAfterDot()
        {
            // Reserved words remain usable as member names (MatchPropertyName), so adding
            // the `void` keyword must not break `obj.void`.
            Assert.That(Eval("(function(){ var o = {}; o.void = 42; return o.void; })()").Int, Is.EqualTo(42));
        }

        [Test]
        public void Eval_ArrayLiteral()
        {
            var arr = Eval("[10, 20, 30]");
            Assert.That(arr.IsArray, Is.True);
            Assert.That(arr.GetArrayLength(), Is.EqualTo(3));
            Assert.That(arr.GetArrayIndex(1).Int, Is.EqualTo(20));
        }

        [Test]
        public void Eval_ObjectLiteral()
        {
            var obj = Eval("{ x: 1, y: 2 }");
            Assert.That(obj.IsObject, Is.True);
            Assert.That(obj.GetParameter("x").Int, Is.EqualTo(1));
            Assert.That(obj.GetParameter("y").Int, Is.EqualTo(2));
        }
    }
}
