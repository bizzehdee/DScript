using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class ErrorTypesAndProcessTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        // ── Error constructors ────────────────────────────────────────────────

        [TestCase("Error", "Error")]
        [TestCase("TypeError", "TypeError")]
        [TestCase("RangeError", "RangeError")]
        [TestCase("ReferenceError", "ReferenceError")]
        [TestCase("SyntaxError", "SyntaxError")]
        [TestCase("URIError", "URIError")]
        [TestCase("EvalError", "EvalError")]
        public void ErrorConstructor_SetsName(string ctor, string expectedName)
        {
            var r = RunScript($"var e = {ctor}('msg'); var __result__ = e.name;");
            Assert.That(r.String, Is.EqualTo(expectedName));
        }

        [TestCase("Error")]
        [TestCase("TypeError")]
        [TestCase("RangeError")]
        [TestCase("ReferenceError")]
        [TestCase("SyntaxError")]
        [TestCase("URIError")]
        [TestCase("EvalError")]
        public void ErrorConstructor_SetsMessage(string ctor)
        {
            var r = RunScript($"var e = {ctor}('my message'); var __result__ = e.message;");
            Assert.That(r.String, Is.EqualTo("my message"));
        }

        [TestCase("Error")]
        [TestCase("TypeError")]
        public void ErrorConstructor_HasStackProperty(string ctor)
        {
            var r = RunScript($"var e = {ctor}('x'); var __result__ = typeof e.stack;");
            Assert.That(r.String, Is.EqualTo("string"));
        }

        [Test]
        public void ErrorConstructor_NoMessage_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript("var e = Error(); var __result__ = e.name;"));
        }

        [Test]
        public void ErrorCanBeThrown_AndCaught()
        {
            var r = RunScript(@"
                var __result__ = '';
                try {
                    throw TypeError('bad type');
                } catch(e) {
                    __result__ = e.name;
                }
            ");
            Assert.That(r.String, Is.EqualTo("TypeError"));
        }

        // ── process ──────────────────────────────────────────────────────────

        [Test]
        public void Process_Platform_IsKnownValue()
        {
            var r = RunScript("var __result__ = process.platform;");
            var known = new[] { "win32", "linux", "darwin", "freebsd", "unknown" };
            Assert.That(known, Does.Contain(r.String));
        }

        [Test]
        public void Process_Version_IsNonEmpty()
        {
            var r = RunScript("var __result__ = process.version;");
            Assert.That(r.String, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void Process_Argv_IsArray()
        {
            var r = RunScript("var __result__ = Array.isArray(process.argv());");
            Assert.That(r.Bool, Is.True);
        }

        [Test]
        public void Process_Getenv_ReturnsStringOrUndefined()
        {
            // PATH should exist on all platforms; just verify type
            var r = RunScript("var v = process.getenv('PATH'); var __result__ = typeof v;");
            Assert.That(r.String, Is.EqualTo("string").Or.EqualTo("undefined"));
        }

        [Test]
        public void Process_Getenv_NonExistentVar_ReturnsUndefined()
        {
            var r = RunScript("var v = process.getenv('__DSCRIPT_NONEXISTENT_12345__'); var __result__ = v === undefined;");
            Assert.That(r.Bool, Is.True);
        }
    }
}
