using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    // String concatenation builds a lazy "rope" (cons-string), flattened on read, so
    // building a string with += in a loop is O(n) rather than O(n^2). These verify
    // the result is correct — concatenation must not mutate the left operand, and
    // length/equality/comparison must see the flattened value.
    public class StringConcatRopeTests
    {
        private static ScriptEngine Run(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine;
        }

        private static string Str(string code, string varName) => Run(code).Root.GetParameter(varName).String;
        private static int IntOf(string code, string varName) => Run(code).Root.GetParameter(varName).Int;

        [Test]
        public void BuildWithPlusEquals()
            => Assert.That(Str("var t = ''; for (var i = 0; i < 5; i = i + 1) t += 'x' + i; result = t;", "result"),
                Is.EqualTo("x0x1x2x3x4"));

        [Test]
        public void LengthAfterBuild()
            => Assert.That(IntOf("var t = ''; for (var i = 0; i < 1000; i = i + 1) t += 'ab'; result = t.length;", "result"),
                Is.EqualTo(2000));

        [Test]
        public void ConcatenationDoesNotMutateLeftOperand()
        {
            // a must still be "foo" after b = a + "bar".
            Assert.That(Str("var a = 'foo'; var b = a + 'bar'; result = a;", "result"), Is.EqualTo("foo"));
            Assert.That(Str("var a = 'foo'; var b = a + 'bar'; result = b;", "result"), Is.EqualTo("foobar"));
        }

        [Test]
        public void MixedTypeConcatenation()
            => Assert.That(Str("result = 1 + 'a' + 2 + 'b';", "result"), Is.EqualTo("1a2b"));

        [Test]
        public void EqualityFlattensRopes()
            => Assert.That(IntOf("var x = 'ab' + 'cd'; result = (x === 'abcd') ? 1 : 0;", "result"), Is.EqualTo(1));

        [Test]
        public void ComparisonFlattensRopes()
            => Assert.That(IntOf("result = (('a' + 'b') < ('a' + 'c')) ? 1 : 0;", "result"), Is.EqualTo(1));

        [Test]
        public void RopeReadRepeatedlyIsStable()
        {
            // Reading a built string more than once must return the same value.
            Assert.That(Str("var t = ''; for (var i = 0; i < 3; i = i + 1) t += i; var x = t + ''; result = t + ':' + t;", "result"),
                Is.EqualTo("012:012"));
        }

        [Test]
        public void RopeUsedInMethodCall()
            => Assert.That(Str("var t = ''; for (var i = 0; i < 3; i = i + 1) t += 'ab'; result = t.toUpperCase();", "result"),
                Is.EqualTo("ABABAB"));
    }
}
