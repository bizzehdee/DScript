using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    /// <summary>
    /// Tests that eval() compiles its argument as a program (statement sequence),
    /// not as a single expression. Previously eval() called CompileExpression(),
    /// which rejected statement-level syntax with parse errors.
    /// </summary>
    [TestFixture]
    public class EvalStatementTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        [Test]
        public void Eval_VarDeclaration_Succeeds()
        {
            var result = RunScript("eval('var x = 42;'); __result__ = x;");
            Assert.That(result.Int, Is.EqualTo(42));
        }

        [Test]
        public void Eval_LetDeclaration_Succeeds()
        {
            // let inside eval is block-scoped to the eval body; the binding
            // is not visible outside, but eval must not throw a parse error.
            Assert.DoesNotThrow(() => RunScript("eval('let x = 1;');  __result__ = 1;"));
        }

        [Test]
        public void Eval_IfStatement_Succeeds()
        {
            var result = RunScript("eval('if (true) { __result__ = 7; }');");
            Assert.That(result.Int, Is.EqualTo(7));
        }

        [Test]
        public void Eval_SwitchStatement_Succeeds()
        {
            var result = RunScript(@"
                eval('switch (2) { case 2: __result__ = 99; break; }');
            ");
            Assert.That(result.Int, Is.EqualTo(99));
        }

        [Test]
        public void Eval_ForStatement_Succeeds()
        {
            var result = RunScript(@"
                eval('var s = 0; for (var i = 0; i < 4; i++) s += i; __result__ = s;');
            ");
            Assert.That(result.Int, Is.EqualTo(6));
        }

        [Test]
        public void Eval_BlockWithFunctionDecl_Succeeds()
        {
            // { function f() {} } is a block statement with a function declaration.
            // Previously the expression parser treated { } as an object literal and
            // choked on the function keyword.
            Assert.DoesNotThrow(() =>
                RunScript("eval('{ function f() { return 1; } }'); __result__ = 1;"));
        }

        [Test]
        public void Eval_FunctionDeclaration_IsCallable()
        {
            var result = RunScript(@"
                eval('function greet() { return 42; }');
                __result__ = greet();
            ");
            Assert.That(result.Int, Is.EqualTo(42));
        }

        // ── object literal keyword keys (Step 3: MatchPropertyName) ──────────

        [Test]
        public void ObjectLiteral_ReservedWordAsPropertyKey_Succeeds()
        {
            var result = RunScript(@"
                var obj = { if: 1, var: 2, function: 3, switch: 4 };
                __result__ = obj.if + obj.var + obj.function + obj.switch;
            ");
            Assert.That(result.Int, Is.EqualTo(10));
        }

        [Test]
        public void ObjectLiteral_ReturnAsPropertyKey_Succeeds()
        {
            var result = RunScript(@"
                var obj = { return: 99 };
                __result__ = obj.return;
            ");
            Assert.That(result.Int, Is.EqualTo(99));
        }

        // ── String() callable type coercion ───────────────────────────────────

        [Test]
        public void String_CalledAsFunction_ConvertsUndefined()
        {
            var result = RunScript("__result__ = String(undefined);");
            Assert.That(result.String, Is.EqualTo("undefined"));
        }

        [Test]
        public void String_CalledAsFunction_ConvertsNull()
        {
            var result = RunScript("__result__ = String(null);");
            Assert.That(result.String, Is.EqualTo("null"));
        }

        [Test]
        public void String_CalledAsFunction_ConvertsNumber()
        {
            var result = RunScript("__result__ = String(42);");
            Assert.That(result.String, Is.EqualTo("42"));
        }

        [Test]
        public void String_CalledAsFunction_PassthroughString()
        {
            var result = RunScript("__result__ = String('hello');");
            Assert.That(result.String, Is.EqualTo("hello"));
        }

        [Test]
        public void String_TypeofIsFunction()
        {
            var result = RunScript("__result__ = typeof String;");
            Assert.That(result.String, Is.EqualTo("function"));
        }

        [Test]
        public void String_MethodsStillAccessible()
        {
            var result = RunScript("__result__ = 'hello'.indexOf('l');");
            Assert.That(result.Int, Is.EqualTo(2));
        }

        // ── Eval function hoisting ────────────────────────────────────────────

        [Test]
        public void Eval_TopLevelFunctionDecl_IsHoistedBeforeUse()
        {
            // init = f before function f() {} should work via hoisting.
            var result = RunScript(@"
                var init;
                eval('init = f; function f() { return 42; }');
                __result__ = init();
            ");
            Assert.That(result.Int, Is.EqualTo(42));
        }

        [Test]
        public void Eval_BlockFunctionDecl_DoesNotShadowTopLevelHoistedFunction()
        {
            // Outer function f (top-level) is hoisted; the block-scoped f stays
            // local to the block; init captures the outer f before the block runs.
            var result = RunScript(@"
                var init;
                eval('init = f; { function f() { return 1; } } function f() { return 2; }');
                __result__ = init();
            ");
            Assert.That(result.Int, Is.EqualTo(2));
        }

        [Test]
        public void Eval_LetConflict_BlockFunctionDoesNotOverwriteLetBinding()
        {
            // When a let binding exists for a name, a block-level function
            // declaration with the same name must NOT clobber the let binding
            // (Annex B.3.3 conflict rule).
            var result = RunScript(@"
                var after;
                eval('let f = 123; { function f() {} } after = f;');
                __result__ = after;
            ");
            Assert.That(result.Int, Is.EqualTo(123));
        }
    }
}
