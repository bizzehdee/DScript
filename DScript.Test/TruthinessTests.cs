using DScript;
using NUnit.Framework;

namespace DScript.Test
{
    // JavaScript ToBoolean semantics: objects/arrays/functions are always truthy, a
    // non-empty string is truthy, numbers are truthy unless 0/NaN, and null/undefined
    // are falsy. Previously truthiness was `Int != 0`, which made arrays, objects,
    // non-empty non-numeric strings, and fractional numbers all wrongly falsy.
    public class TruthinessTests
    {
        private static string Truth(string expr)
        {
            var engine = new ScriptEngine();
            engine.Execute("result = (" + expr + ") ? 'T' : 'F';");
            return engine.Root.GetParameter("result").String;
        }

        [TestCase("[1,2,3]", "T")]
        [TestCase("[]", "T")]            // arrays are always truthy, even empty
        [TestCase("({})", "T")]          // objects are always truthy
        [TestCase("'abc'", "T")]
        [TestCase("'0'", "T")]           // non-empty string, truthy (even "0")
        [TestCase("''", "F")]            // empty string is falsy
        [TestCase("0", "F")]
        [TestCase("0.0", "F")]
        [TestCase("1", "T")]
        [TestCase("0.5", "T")]           // fractional number is truthy
        [TestCase("-1", "T")]
        [TestCase("null", "F")]
        [TestCase("undefined", "F")]
        [TestCase("true", "T")]
        [TestCase("false", "F")]
        [TestCase("NaN", "F")]
        public void Truthiness(string expr, string expected)
            => Assert.That(Truth(expr), Is.EqualTo(expected));

        [Test]
        public void FunctionIsTruthy()
            => Assert.That(Truth("function(){}"), Is.EqualTo("T"));

        [Test]
        public void ArrayInIfCondition()
        {
            var engine = new ScriptEngine();
            engine.Execute("var a = [1]; if (a) { result = 'taken'; } else { result = 'skipped'; }");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("taken"));
        }

        [Test]
        public void ObjectInLogicalAnd()
        {
            // `obj && obj.x` — the object must be truthy to reach the property.
            var engine = new ScriptEngine();
            engine.Execute("var o = { x: 7 }; result = (o && o.x);");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(7));
        }
    }
}
