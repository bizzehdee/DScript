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

        [Test]
        public void Eval_StringConcatAndTypeof()
        {
            Assert.That(Eval("\"a\" + \"b\"").String, Is.EqualTo("ab"));
            Assert.That(Eval("typeof 5").String, Is.EqualTo("number"));
            Assert.That(Eval("typeof \"x\"").String, Is.EqualTo("string"));
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
