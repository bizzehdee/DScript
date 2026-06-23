using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class StrictModeTests
    {
        private static Chunk Compile(string source)
            => new DScriptCompiler().CompileProgram(source);

        // ── IsStrict flag detection ────────────────────────────────────────────

        [Test]
        public void UseStrict_SetsIsStrictOnChunk()
        {
            var chunk = Compile("\"use strict\"; var x = 1;");
            Assert.That(chunk.IsStrict, Is.True);
        }

        [Test]
        public void UseStrict_WithSemicolon_SetsIsStrict()
        {
            var chunk = Compile("'use strict'; var x = 1;");
            Assert.That(chunk.IsStrict, Is.True);
        }

        [Test]
        public void UseStrict_WithoutSemicolon_SetsIsStrict()
        {
            var chunk = Compile("\"use strict\"\nvar x = 1;");
            Assert.That(chunk.IsStrict, Is.True);
        }

        [Test]
        public void NoDirective_IsStrictRemainsfalse()
        {
            var chunk = Compile("var x = 1;");
            Assert.That(chunk.IsStrict, Is.False);
        }

        [Test]
        public void UseStrict_FunctionBody_SetsIsStrictOnFunctionChunk()
        {
            var chunk = Compile("function f() { \"use strict\"; }");
            var fnChunk = chunk.Functions[0];
            Assert.That(fnChunk.IsStrict, Is.True);
        }

        [Test]
        public void UseStrict_FunctionBody_DoesNotPolluteProgramChunk()
        {
            var chunk = Compile("function f() { \"use strict\"; }");
            Assert.That(chunk.IsStrict, Is.False);
        }

        [Test]
        public void UseStrict_ProgramLevel_PropagatesIntoNestedFunction()
        {
            var chunk = Compile("\"use strict\"; function f() {}");
            Assert.That(chunk.IsStrict, Is.True);
            var fnChunk = chunk.Functions[0];
            Assert.That(fnChunk.IsStrict, Is.True);
        }

        [Test]
        public void UseStrict_ScriptRunsNormally()
        {
            var engine = new ScriptEngine();
            var compiled = Compile("\"use strict\"; var x = 42;");
            new VirtualMachine(engine).Run(compiled, new Vm.Environment(engine.Root, null));
            Assert.That(engine.Root.GetParameter("x").Int, Is.EqualTo(42));
        }

        // ── T11: compile-time errors ───────────────────────────────────────────

        [Test]
        public void Strict_DuplicateParamNames_ThrowsSyntaxError()
        {
            Assert.Throws<ScriptException>(() =>
                Compile("\"use strict\"; function f(a, a) {}"));
        }

        [Test]
        public void Strict_DuplicateParamInFunctionDirective_ThrowsSyntaxError()
        {
            Assert.Throws<ScriptException>(() =>
                Compile("function f(a, a) { \"use strict\"; }"));
        }

        [Test]
        public void NonStrict_DuplicateParamNames_IsAllowed()
        {
            // No exception — duplicate params are valid outside strict mode
            Assert.DoesNotThrow(() => Compile("function f(a, a) {}"));
        }

        [Test]
        public void Strict_EvalAsParamName_ThrowsSyntaxError()
        {
            Assert.Throws<ScriptException>(() =>
                Compile("\"use strict\"; function f(eval) {}"));
        }

        [Test]
        public void Strict_ArgumentsAsParamName_ThrowsSyntaxError()
        {
            Assert.Throws<ScriptException>(() =>
                Compile("\"use strict\"; function f(arguments) {}"));
        }

        [Test]
        public void Strict_EvalAsVarName_ThrowsSyntaxError()
        {
            Assert.Throws<ScriptException>(() =>
                Compile("\"use strict\"; var eval = 1;"));
        }

        [Test]
        public void Strict_ArgumentsAsVarName_ThrowsSyntaxError()
        {
            Assert.Throws<ScriptException>(() =>
                Compile("\"use strict\"; var arguments = 1;"));
        }

        [Test]
        public void Strict_DeleteIdentifier_ThrowsSyntaxError()
        {
            Assert.Throws<ScriptException>(() =>
                Compile("\"use strict\"; var x = 1; delete x;"));
        }

        [Test]
        public void NonStrict_DeleteIdentifier_ReturnsFalse()
        {
            var engine = new ScriptEngine();
            var compiled = Compile("var x = 1; var r = delete x;");
            new VirtualMachine(engine).Run(compiled, new Vm.Environment(engine.Root, null));
            Assert.That(engine.Root.GetParameter("r").Bool, Is.False);
        }

        [Test]
        public void Strict_OctalLiteral_ThrowsSyntaxError()
        {
            Assert.Throws<ScriptException>(() =>
                Compile("\"use strict\"; var x = 0777;"));
        }

        [Test]
        public void NonStrict_OctalLiteral_IsAllowed()
        {
            Assert.DoesNotThrow(() => Compile("var x = 0777;"));
        }

        [Test]
        public void Strict_DeleteProperty_IsAllowed()
        {
            // delete obj.prop is fine even in strict mode
            var engine = new ScriptEngine();
            var compiled = Compile("\"use strict\"; var obj = {x:1}; var r = delete obj.x;");
            new VirtualMachine(engine).Run(compiled, new Vm.Environment(engine.Root, null));
            Assert.That(engine.Root.GetParameter("r").Bool, Is.True);
        }
    }
}
