using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class FunctionMethodTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        // ── Function.prototype.call ───────────────────────────────────────────

        [Test]
        public void Call_InvokesWithExplicitThis()
        {
            var result = RunScript(@"
                function greet() { return this.name; }
                var obj = { name: 'Alice' };
                var __result__ = greet.call(obj);
            ");
            Assert.That(result.String, Is.EqualTo("Alice"));
        }

        [Test]
        public void Call_ForwardsArguments()
        {
            var result = RunScript(@"
                function add(a, b) { return a + b; }
                var __result__ = add.call(null, 3, 4);
            ");
            Assert.That(result.Int, Is.EqualTo(7));
        }

        [Test]
        public void Call_NoArgs_ReturnsUndefinedForParams()
        {
            var result = RunScript(@"
                function check(x) { return x === undefined ? 1 : 0; }
                var __result__ = check.call(null);
            ");
            Assert.That(result.Int, Is.EqualTo(1));
        }

        // ── Function.prototype.apply ──────────────────────────────────────────

        [Test]
        public void Apply_InvokesWithExplicitThis()
        {
            var result = RunScript(@"
                function greet() { return this.name; }
                var obj = { name: 'Bob' };
                var __result__ = greet.apply(obj);
            ");
            Assert.That(result.String, Is.EqualTo("Bob"));
        }

        [Test]
        public void Apply_ForwardsArgsArray()
        {
            var result = RunScript(@"
                function sum(a, b, c) { return a + b + c; }
                var __result__ = sum.apply(null, [1, 2, 3]);
            ");
            Assert.That(result.Int, Is.EqualTo(6));
        }

        [Test]
        public void Apply_EmptyArgsArray()
        {
            var result = RunScript(@"
                function count() { return 0; }
                var __result__ = count.apply(null, []);
            ");
            Assert.That(result.Int, Is.EqualTo(0));
        }

        // ── Function.prototype.bind ───────────────────────────────────────────

        [Test]
        public void Bind_BasicInvocation()
        {
            var result = RunScript(@"
                function greet() { return this.name; }
                var obj = { name: 'Carol' };
                var bound = greet.bind(obj);
                var __result__ = bound();
            ");
            Assert.That(result.String, Is.EqualTo("Carol"));
        }

        [Test]
        public void Bind_WithPartialArgs()
        {
            var result = RunScript(@"
                function add(a, b) { return a + b; }
                var add5 = add.bind(null, 5);
                var __result__ = add5(3);
            ");
            Assert.That(result.Int, Is.EqualTo(8));
        }

        [Test]
        public void Bind_ResultName()
        {
            var result = RunScript(@"
                function myFunc() {}
                var bound = myFunc.bind(null);
                var __result__ = bound.name;
            ");
            Assert.That(result.String, Is.EqualTo("bound myFunc"));
        }

        [Test]
        public void Bind_ResultLength()
        {
            var result = RunScript(@"
                function myFunc(a, b, c) {}
                var bound = myFunc.bind(null, 1);
                var __result__ = bound.length;
            ");
            Assert.That(result.Int, Is.EqualTo(2));
        }

        [Test]
        public void Bind_ChainedBindPreservesFirstThis()
        {
            var result = RunScript(@"
                function greet() { return this.name; }
                var obj1 = { name: 'First' };
                var obj2 = { name: 'Second' };
                var bound1 = greet.bind(obj1);
                var bound2 = bound1.bind(obj2);
                var __result__ = bound2();
            ");
            Assert.That(result.String, Is.EqualTo("First"));
        }

        // ── Function.prototype.toString ───────────────────────────────────────

        [Test]
        public void ToString_CompiledFunctionReturnsSource()
        {
            var result = RunScript(@"
                function add(a, b) { return a + b; }
                var __result__ = add.toString();
            ");
            Assert.That(result.String, Does.Contain("add"));
        }

        [Test]
        public void ToString_NativeFunctionContainsNativeCode()
        {
            var result = RunScript(@"
                var __result__ = Math.abs.toString();
            ");
            Assert.That(result.String, Does.Contain("[native code]"));
        }

        [Test]
        public void ToString_AnonymousFunctionHasEmptyName()
        {
            var result = RunScript(@"
                var fn = function() { return 1; };
                var __result__ = fn.toString();
            ");
            Assert.That(result.String, Does.Contain("function"));
        }

        [Test]
        public void ToString_BoundFunctionContainsNativeCode()
        {
            var result = RunScript(@"
                function add(a, b) { return a + b; }
                var bound = add.bind(null, 1);
                var __result__ = bound.toString();
            ");
            Assert.That(result.String, Does.Contain("[native code]"));
        }
    }
}
