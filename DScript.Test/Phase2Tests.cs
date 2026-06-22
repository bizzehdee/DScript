using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    /// <summary>Smoke tests for the Phase 2 feature set.</summary>
    public class Phase2Tests
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

        // ---- 1. Nullish coalescing (??) ------------------------------------

        [Test]
        public void NullCoalesce_NullGivesRhs()
        {
            Assert.That(RunStr("var x = null; var r = x ?? \"default\";"), Is.EqualTo("default"));
        }

        [Test]
        public void NullCoalesce_UndefinedGivesRhs()
        {
            Assert.That(RunStr("var x; var r = x ?? \"default\";"), Is.EqualTo("default"));
        }

        [Test]
        public void NullCoalesce_DefinedKeepsLhs()
        {
            Assert.That(RunInt("var x = 42; var r = x ?? 99;"), Is.EqualTo(42));
        }

        [Test]
        public void NullCoalesce_FalsyZeroKeepsLhs()
        {
            // 0 is not null/undefined — ?? should keep it
            Assert.That(RunInt("var x = 0; var r = x ?? 99;"), Is.EqualTo(0));
        }

        [Test]
        public void NullCoalesce_EmptyStringKeepsLhs()
        {
            Assert.That(RunStr("var x = \"\"; var r = x ?? \"fallback\";"), Is.EqualTo(""));
        }

        // ---- 2. Optional chaining (?.) ------------------------------------

        [Test]
        public void OptionalChain_MemberOnNull_ReturnsUndefined()
        {
            var src = "var obj = null; var r = obj?.name;";
            var engine = new ScriptEngine();
            Run(src, engine);
            Assert.That(engine.Root.GetParameter("r").IsUndefined, Is.True);
        }

        [Test]
        public void OptionalChain_MemberOnUndefined_ReturnsUndefined()
        {
            var src = "var obj; var r = obj?.name;";
            var engine = new ScriptEngine();
            Run(src, engine);
            Assert.That(engine.Root.GetParameter("r").IsUndefined, Is.True);
        }

        [Test]
        public void OptionalChain_MemberOnObject_ReturnsValue()
        {
            Assert.That(RunInt("var obj = {name: 42}; var r = obj?.name;"), Is.EqualTo(42));
        }

        [Test]
        public void OptionalChain_ChainedAccess()
        {
            Assert.That(RunInt("var a = {b: {c: 7}}; var r = a?.b?.c;"), Is.EqualTo(7));
        }

        [Test]
        public void OptionalChain_ChainedAccess_BreaksOnNull()
        {
            var src = "var a = {b: null}; var r = a?.b?.c;";
            var engine = new ScriptEngine();
            Run(src, engine);
            Assert.That(engine.Root.GetParameter("r").IsUndefined, Is.True);
        }

        [Test]
        public void OptionalChain_IndexAccess()
        {
            Assert.That(RunInt("var a = [1,2,3]; var r = a?.[1];"), Is.EqualTo(2));
        }

        [Test]
        public void OptionalChain_IndexOnNull_ReturnsUndefined()
        {
            var src = "var a = null; var r = a?.[0];";
            var engine = new ScriptEngine();
            Run(src, engine);
            Assert.That(engine.Root.GetParameter("r").IsUndefined, Is.True);
        }

        [Test]
        public void OptionalChain_CombinedWithNullCoalesce()
        {
            Assert.That(RunStr("var obj = null; var r = obj?.name ?? \"anon\";"), Is.EqualTo("anon"));
        }

        // ---- 3. Computed object properties ---------------------------------

        [Test]
        public void ComputedProp_StringKey()
        {
            Assert.That(RunInt("var k = \"score\"; var obj = { [k]: 100 }; var r = obj.score;"), Is.EqualTo(100));
        }

        [Test]
        public void ComputedProp_ExpressionKey()
        {
            Assert.That(RunInt("var obj = { [1+1]: 42 }; var r = obj[2];"), Is.EqualTo(42));
        }

        [Test]
        public void ComputedProp_MixedWithRegular()
        {
            Assert.That(RunInt("var k = \"b\"; var obj = { a: 1, [k]: 2 }; var r = obj.a + obj.b;"), Is.EqualTo(3));
        }
    }
}
