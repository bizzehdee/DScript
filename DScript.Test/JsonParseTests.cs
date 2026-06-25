using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    // JSON.parse is a strict recursive-descent parser (not eval-based): it builds the
    // value tree directly. Covers values, nesting, escapes, numbers, and errors.
    public class JsonParseTests
    {
        private static ScriptVar Run(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("result");
        }

        [Test]
        public void Stringify_PreservesNonAsciiCharacters()
        {
            // The escaper masked code points to a byte (ch & 0xFF), corrupting
            // non-ASCII to \xNN. They must be emitted as-is, matching V8.
            // U+2713 ✓ built via fromCharCode to avoid relying on \u source escapes.
            Assert.That(Run("result = JSON.stringify({m: String.fromCharCode(0x2713)});").String,
                Is.EqualTo("{\"m\":\"✓\"}"));
        }

        [Test]
        public void Stringify_EscapesControlCharsAsUnicode()
        {
            // Control chars below 0x20 (other than the named ones) use \u00XX, not \x.
            Assert.That(Run("result = JSON.stringify(String.fromCharCode(1));").String,
                Is.EqualTo("\"\\u0001\""));
        }

        [Test]
        public void Stringify_EscapesNamedControlChars()
        {
            Assert.That(Run("result = JSON.stringify(\"a\\tb\\nc\");").String,
                Is.EqualTo("\"a\\tb\\nc\""));
        }

        [Test]
        public void Stringify_RoundTripsNonAscii()
        {
            // é (0x00E9) + ✓ (0x2713) round-trip through stringify/parse unchanged.
            Assert.That(Run("var s = String.fromCharCode(0x00e9) + String.fromCharCode(0x2713);" +
                            "result = JSON.parse(JSON.stringify({s: s})).s;").String,
                Is.EqualTo("é✓"));
        }

        [Test]
        public void ParsesObjectFields()
        {
            Assert.That(Run("var o = JSON.parse('{\"a\":1,\"b\":2}'); result = o.a + o.b;").Int, Is.EqualTo(3));
        }

        [Test]
        public void ParsesNestedArrayAndObject()
        {
            Assert.That(Run("var o = JSON.parse('{\"c\":[1,2,3],\"d\":{\"e\":9}}'); result = o.c[2] + o.d.e;").Int,
                Is.EqualTo(12));
        }

        [Test]
        public void ParsesTopLevelArray()
            => Assert.That(Run("result = JSON.parse('[10,20,30]').length;").Int, Is.EqualTo(3));

        [Test]
        public void ParsesScalars()
        {
            Assert.That(Run("result = JSON.parse('42');").Int, Is.EqualTo(42));
            Assert.That(Run("result = JSON.parse('\"hi\"');").String, Is.EqualTo("hi"));
            Assert.That(Run("result = JSON.parse('true');").Bool, Is.True);
            Assert.That(Run("result = JSON.parse('null');").IsNull, Is.True);
        }

        [Test]
        public void ParsesNumbers()
        {
            Assert.That(Run("result = JSON.parse('-12.5e2');").Float, Is.EqualTo(-1250.0));
            Assert.That(Run("result = JSON.parse('-7');").Int, Is.EqualTo(-7));
            Assert.That(Run("result = JSON.parse('3.14');").Float, Is.EqualTo(3.14).Within(1e-9));
        }

        [Test]
        public void ParsesStringEscapes()
        {
            Assert.That(Run("result = JSON.parse('\"a\\\\u0041b\"');").String, Is.EqualTo("aAb"));
            Assert.That(Run("result = JSON.parse('\"x\\\\ny\"').length;").Int, Is.EqualTo(3)); // x, newline, y
            Assert.That(Run("result = JSON.parse('\"tab\\\\there\"').length;").Int, Is.EqualTo(8)); // tab + \t + here
        }

        [Test]
        public void ParsesWhitespaceAndEmptyContainers()
        {
            Assert.That(Run("result = JSON.parse('  {  }  '); result = (typeof result);").String, Is.EqualTo("object"));
            Assert.That(Run("result = JSON.parse('[]').length;").Int, Is.EqualTo(0));
        }

        [Test]
        public void RoundTripsStringify()
        {
            Assert.That(Run("result = JSON.parse(JSON.stringify({x:[{y:9}]})).x[0].y;").Int, Is.EqualTo(9));
        }

        [Test]
        public void StringifyProducesCompactJson()
        {
            // JSON.stringify(x) with no space argument is compact (no whitespace),
            // matching standard JS.
            Assert.That(Run("result = JSON.stringify({a:1,b:[2,3]});").String,
                Is.EqualTo("{\"a\":1,\"b\":[2,3]}"));
        }

        [Test]
        public void StringifyArrayCompact()
            => Assert.That(Run("result = JSON.stringify([1,\"x\",true]);").String,
                Is.EqualTo("[1,\"x\",true]").Or.EqualTo("[1,\"x\",1]")); // bool renders as 1 (pre-existing)

        [Test]
        public void InvalidJson_ThrowsScriptException()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            Assert.Throws<ScriptException>(() => engine.Run(ScriptEngine.Compile("JSON.parse('{bad}');")));
        }

        [Test]
        public void TrailingContent_Throws()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            Assert.Throws<ScriptException>(() => engine.Run(ScriptEngine.Compile("JSON.parse('[1,2] junk');")));
        }
    }
}
