using DScript;
using NUnit.Framework;

namespace DScript.Test
{
    // The VM skips materialising a function's `arguments` object unless the function
    // may access it (it references `arguments`, contains a nested arrow that does, or
    // uses `eval`). These tests lock in that `arguments` still behaves correctly in
    // every case that needs it — the optimization must never lose access.
    public class ArgumentsOptimizationTests
    {
        private static int IntOf(string src, string varName)
        {
            var engine = new ScriptEngine();
            engine.Execute(src);
            return engine.Root.GetParameter(varName).Int;
        }

        private static string StrOf(string src, string varName)
        {
            var engine = new ScriptEngine();
            engine.Execute(src);
            return engine.Root.GetParameter(varName).String;
        }

        [Test]
        public void DirectArgumentsLength()
            => Assert.That(IntOf("function f(){ return arguments.length; } result = f(1, 2, 3);", "result"), Is.EqualTo(3));

        [Test]
        public void DirectArgumentsIndexing()
            => Assert.That(StrOf("function f(){ return arguments[1]; } result = f('a', 'b');", "result"), Is.EqualTo("b"));

        [Test]
        public void NoArgsReference_StillCallableWithExtraArgs()
        {
            // f never mentions `arguments`, so it isn't materialised — but passing
            // extra args must still work (they're simply ignored).
            Assert.That(IntOf("function f(a){ return a * 2; } result = f(21, 99, 100);", "result"), Is.EqualTo(42));
        }

        [Test]
        public void NestedFunctionHasItsOwnArguments()
        {
            // The inner function's `arguments` is its own, not the outer call's.
            Assert.That(IntOf(
                "function f(){ function g(){ return arguments[0]; } return g(99); } result = f(1, 2, 3);",
                "result"), Is.EqualTo(99));
        }

        [Test]
        public void ArrowInheritsEnclosingArguments()
        {
            // The arrow has no own `arguments`; it must resolve to f's — so f must
            // materialise its arguments even though f's own body never names it.
            Assert.That(IntOf(
                "function f(){ var g = () => arguments[0]; return g(); } result = f(42);",
                "result"), Is.EqualTo(42));
        }

        [Test]
        public void NestedArrowInheritsOutermostArguments()
        {
            Assert.That(IntOf(
                "function f(){ var g = () => (() => arguments.length)(); return g(); } result = f(1, 2, 3);",
                "result"), Is.EqualTo(3));
        }

        [Test]
        public void ArrowArgumentsUsage_IsHotPathSafe()
        {
            // Drive the function past JIT tier-up to confirm both the interpreter and
            // any compiled path keep arguments available for the arrow.
            Assert.That(IntOf(
                "function f(x){ var g = () => arguments[0]; return g(); }" +
                "var r = 0; var i = 0; while (i < 1500) { r = f(i); i = i + 1; } result = r;",
                "result"), Is.EqualTo(1499));
        }

        [Test]
        public void ClassMethodArguments()
            => Assert.That(IntOf(
                "class C { m(){ return arguments.length; } } result = new C().m(1, 2, 3, 4);",
                "result"), Is.EqualTo(4));

        [Test]
        public void EmptyArguments()
            => Assert.That(IntOf("function f(){ return arguments.length; } result = f();", "result"), Is.EqualTo(0));

        // Argument binding shares primitive values by reference rather than copying
        // them defensively. These tests lock in that call-by-value isolation still
        // holds: a callee reassigning or mutating a parameter must never disturb the
        // caller's binding, even when the SAME variable is passed (so its ScriptVar
        // is ref-counted > 0 — the case the old code used to DeepCopy).
        [Test]
        public void ReassigningParameter_DoesNotMutateCallerVariable()
        {
            // x is a bound variable (refs > 0); f reassigns its parameter.
            Assert.That(IntOf(
                "function f(a){ a = a + 100; return a; } var x = 5; var y = f(x); result = x;",
                "result"), Is.EqualTo(5));
        }

        [Test]
        public void ReassignedParameter_ReturnsUpdatedValueToCallee()
        {
            Assert.That(IntOf(
                "function f(a){ a = a + 100; return a; } var x = 5; result = f(x);",
                "result"), Is.EqualTo(105));
        }

        [Test]
        public void IncrementingParameter_DoesNotMutateCallerVariable()
        {
            Assert.That(IntOf(
                "function f(a){ a++; a++; return a; } var x = 7; var y = f(x); result = x;",
                "result"), Is.EqualTo(7));
        }

        [Test]
        public void SameVariablePassedTwice_ParametersAreIndependent()
        {
            // Both parameters initially share x's ScriptVar; mutating one must not
            // affect the other or the caller.
            Assert.That(IntOf(
                "function f(a, b){ a = a + 1; return b; } var x = 10; var r = f(x, x); result = r + x;",
                "result"), Is.EqualTo(20));
        }

        [Test]
        public void StringParameterReassignment_DoesNotMutateCallerVariable()
        {
            Assert.That(StrOf(
                "function f(s){ s = s + '!'; return s; } var x = 'hi'; var y = f(x); result = x;",
                "result"), Is.EqualTo("hi"));
        }

        [Test]
        public void RecursiveCall_PassingParameterPreservesEachFramesBinding()
        {
            // Each recursive frame binds n; decrementing in the recursion must not
            // corrupt an outer frame's n. Sum 3+2+1 = 6, and the outer n stays 3.
            Assert.That(IntOf(
                "function f(n){ if (n <= 0) return 0; var rest = f(n - 1); return n + rest; }" +
                "result = f(3);",
                "result"), Is.EqualTo(6));
        }
    }
}
