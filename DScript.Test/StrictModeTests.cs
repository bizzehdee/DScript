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
    }
}
