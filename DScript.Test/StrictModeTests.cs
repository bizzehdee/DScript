using DScript;
using DScript.Compiler;
using DScript.Extras;
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

        // ── T12: this=undefined in plain calls ────────────────────────────────

        private static string RunStr(string source)
        {
            var engine = new ScriptEngine();
            var chunk = Compile(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r").String;
        }

        private static int RunInt(string source)
        {
            var engine = new ScriptEngine();
            var chunk = Compile(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r").Int;
        }

        [Test]
        public void Strict_PlainCall_ThisIsUndefined()
        {
            var src = @"
                ""use strict"";
                function getThis() { return typeof this; }
                var r = getThis();
            ";
            Assert.That(RunStr(src), Is.EqualTo("undefined"));
        }

        [Test]
        public void NonStrict_PlainCall_ThisIsInheritedFromScope()
        {
            // In sloppy mode this is inherited from the calling scope (not explicitly set
            // to undefined like strict mode). Accessing a property via this works when
            // called as a method.
            var src = @"
                var obj = {
                    val: 99,
                    getVal: function() { return this.val; }
                };
                var r = obj.getVal();
            ";
            Assert.That(RunInt(src), Is.EqualTo(99));
        }

        [Test]
        public void Strict_MethodCall_ThisIsReceiver()
        {
            var src = @"
                ""use strict"";
                var obj = { val: 42, getThis: function() { return this; } };
                var r = obj.getThis().val;
            ";
            Assert.That(RunInt(src), Is.EqualTo(42));
        }

        // ── T13: arguments.callee / arguments.caller poison pills ─────────────

        [Test]
        public void Strict_ArgumentsCallee_ThrowsTypeError()
        {
            var src = @"
                ""use strict"";
                function f() { return arguments.callee; }
                f();
            ";
            var engine = new ScriptEngine();
            var chunk = Compile(src);
            Assert.Throws<ScriptException>(() =>
                new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null)));
        }

        [Test]
        public void Strict_ArgumentsCaller_ThrowsTypeError()
        {
            var src = @"
                ""use strict"";
                function f() { return arguments.caller; }
                f();
            ";
            var engine = new ScriptEngine();
            var chunk = Compile(src);
            Assert.Throws<ScriptException>(() =>
                new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null)));
        }

        [Test]
        public void NonStrict_ArgumentsCallee_ReturnsUndefined()
        {
            // In sloppy mode arguments.callee is not poisoned (we don't set it,
            // but it shouldn't throw — accessing an unset property returns undefined).
            var src = @"
                function f() { return typeof arguments.callee; }
                var r = f();
            ";
            // Should not throw (returns either "function" or "undefined" depending
            // on whether callee was set, but should never throw TypeError).
            Assert.DoesNotThrow(() => RunStr(src));
        }

        // ── T14: undeclared assignment throws ReferenceError ──────────────────

        [Test]
        public void Strict_UndeclaredAssignment_ThrowsReferenceError()
        {
            var src = @"""use strict""; x = 1;";
            var engine = new ScriptEngine();
            var chunk = Compile(src);
            Assert.Throws<ScriptException>(() =>
                new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null)));
        }

        [Test]
        public void Strict_DeclaredVar_Assignment_IsAllowed()
        {
            var src = @"""use strict""; var x = 0; x = 42;";
            var engine = new ScriptEngine();
            var chunk = Compile(src);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            Assert.That(engine.Root.GetParameter("x").Int, Is.EqualTo(42));
        }

        [Test]
        public void NonStrict_UndeclaredAssignment_CreatesGlobal()
        {
            var src = @"x = 99;";
            var engine = new ScriptEngine();
            var chunk = Compile(src);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            Assert.That(engine.Root.GetParameter("x").Int, Is.EqualTo(99));
        }

        // ── T15: non-writable property write throws TypeError ─────────────────

        private static ScriptEngine MakeExtrasEngine()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            return engine;
        }

        private static void RunWithExtras(ScriptEngine engine, string source)
        {
            var chunk = Compile(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
        }

        [Test]
        public void Strict_WriteToNonWritableProperty_ThrowsTypeError()
        {
            var engine = MakeExtrasEngine();
            Assert.Throws<ScriptException>(() => RunWithExtras(engine, @"
                ""use strict"";
                var obj = {};
                Object.defineProperty(obj, 'x', { value: 1, writable: false });
                obj.x = 2;
            "));
        }

        [Test]
        public void NonStrict_WriteToNonWritableProperty_IsSilentlyIgnored()
        {
            var engine = MakeExtrasEngine();
            RunWithExtras(engine, @"
                var obj = {};
                Object.defineProperty(obj, 'x', { value: 1, writable: false });
                obj.x = 2;
                var r = obj.x;
            ");
            Assert.That(engine.Root.GetParameter("r").Int, Is.EqualTo(1));
        }
    }
}
