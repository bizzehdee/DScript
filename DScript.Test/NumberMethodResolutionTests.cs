using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    // Number instance methods (toFixed, toString, toPrecision, toExponential) must
    // resolve on plain number values/literals — these are bare ScriptVars not linked
    // to the Number class, so the engine resolves them against the Number constructor.
    public class NumberMethodResolutionTests
    {
        private static string Run(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("result").String;
        }

        [Test]
        public void ToFixedOnParenthesisedLiteral()
            => Assert.That(Run("result = (3.14159).toFixed(2);"), Is.EqualTo("3.14"));

        [Test]
        public void ToFixedDefaultZeroDigits()
            => Assert.That(Run("result = (3.7).toFixed();"), Is.EqualTo("4"));

        [Test]
        public void ToFixedOnVariable()
            => Assert.That(Run("var x = 1.005; result = x.toFixed(2);"), Is.EqualTo("1.00").Or.EqualTo("1.01"));

        [Test]
        public void ToFixedOnExpressionResult()
            => Assert.That(Run("result = (1.5 + 1.833).toFixed(3);"), Is.EqualTo("3.333"));

        [Test]
        public void ToFixedOnIntegerValue()
            => Assert.That(Run("var n = 42; result = n.toFixed(2);"), Is.EqualTo("42.00"));

        [Test]
        public void ToStringOnNumber()
            => Assert.That(Run("result = (255).toString(16);"), Is.EqualTo("ff"));

        [Test]
        public void ToPrecisionOnNumber()
            => Assert.That(Run("result = (123.456).toPrecision(4);"), Is.EqualTo("123.5"));

        [Test]
        public void TypeofToFixedIsFunction()
            => Assert.That(Run("result = typeof (1).toFixed;"), Is.EqualTo("function"));
    }
}
