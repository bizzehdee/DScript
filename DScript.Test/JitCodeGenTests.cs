using NUnit.Framework;
using DScript.Vm;
using DScript.Jit;

namespace DScript.Test
{
    /// <summary>
    /// End-to-end correctness shared by every JIT back-end: each script is run once
    /// purely interpreted and once with the JIT registered; the results must be
    /// identical, and the function under test must reach the expected JIT state.
    /// Concrete fixtures supply the compiler under test via <see cref="NewCompiler"/>.
    /// NonParallelizable because JitRegistry is process-global.
    /// </summary>
    [NonParallelizable]
    public abstract class JitCodeGenTestsBase
    {
        /// <summary>The back-end under test.</summary>
        protected abstract IJitCompiler NewCompiler();

        [TearDown] public void Clear() => JitRegistry.Clear();

        private (string result, Chunk.JitStatus state) RunOnce(string script, string fn, bool jit)
        {
            if (jit) JitRegistry.Register(NewCompiler());
            else JitRegistry.Clear();

            var chunk = ScriptEngine.Compile(script);
            var engine = new ScriptEngine();
            engine.Run(chunk);

            return (engine.Root.GetParameter("__result__").String, FindState(chunk, fn));
        }

        // Search this chunk and all nested function chunks for one named `fn`.
        private static Chunk.JitStatus FindState(Chunk chunk, string fn)
        {
            foreach (var f in chunk.Functions)
            {
                if (f.Name == fn) return f.JitState;
                var nested = FindState(f, fn);
                if (nested != Chunk.JitStatus.Cold) return nested;
            }
            return Chunk.JitStatus.Cold;
        }

        // Run interpreted and JIT-compiled; assert identical result and that `fn`
        // reached `expectedState` under the JIT.
        private void AssertMatches(string script, string fn, Chunk.JitStatus expectedState)
        {
            var interp = RunOnce(script, fn, jit: false);
            var jit = RunOnce(script, fn, jit: true);
            Assert.That(jit.result, Is.EqualTo(interp.result),
                $"JIT result must match interpreter (fn={fn})");
            Assert.That(jit.state, Is.EqualTo(expectedState),
                $"fn={fn} should reach {expectedState}");
        }

        // Build a script that calls a one-expression function f(a,b) 1200 times
        // (crossing the invocation threshold) accumulating into r.
        private static string CallLoop(string body, string aExpr, string bExpr) =>
            "function f(a,b){ return " + body + "; }\n" +
            "var r=0; var i=0; while(i<1200){ r = f(" + aExpr + ", " + bExpr + "); i = i + 1; }\n" +
            "__result__ = r;";

        // ── integer arithmetic / comparison / bitwise ───────────────────────────

        [TestCase("a + b")]
        [TestCase("a - b")]
        [TestCase("a * b")]
        [TestCase("a & b")]
        [TestCase("a | b")]
        [TestCase("a ^ b")]
        [TestCase("a / b")]    // integer division
        [TestCase("a % b")]
        [TestCase("a < b")]
        [TestCase("a <= b")]
        [TestCase("a > b")]
        [TestCase("a >= b")]
        [TestCase("a == b")]
        [TestCase("a != b")]
        public void IntBinary(string body)
        {
            AssertMatches(CallLoop(body, "i", "3"), "f", Chunk.JitStatus.Compiled);
        }

        // ── double / mixed numeric arithmetic ───────────────────────────────────

        [TestCase("a + b", "i + 0.5", "2.25")]
        [TestCase("a - b", "i + 0.5", "2.25")]
        [TestCase("a * b", "i + 0.5", "2.0")]
        [TestCase("a / b", "i + 0.5", "4.0")]
        [TestCase("a / b", "i", "2.0")]      // int / double -> double
        public void DoubleBinary(string body, string a, string bb)
        {
            AssertMatches(CallLoop(body, a, bb), "f", Chunk.JitStatus.Compiled);
        }

        // ── string concatenation ────────────────────────────────────────────────

        [TestCase("'x', 'y'")]          // string + string -> fast path
        [TestCase("'n=', i")]           // string + number -> MathsOp coercion
        [TestCase("i, 'k'")]            // number + string -> MathsOp coercion
        public void StringConcat(string call)
        {
            var parts = call.Split(',');
            AssertMatches(CallLoop("a + b", parts[0].Trim(), parts[1].Trim()), "f", Chunk.JitStatus.Compiled);
        }

        // ── constants, fused binary forms, super-instruction ────────────────────

        [Test]
        public void ConstantsAndFusedForms()
        {
            // n < 10 -> GetVar + BinaryIntConst ; a + b -> GetVarGetVarBinary ;
            // literal -> Constant.
            AssertMatches(
                "function f(a,b){ return (a + b) * 2 - 1; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(i, 5); i = i + 1; }\n__result__ = r;",
                "f", Chunk.JitStatus.Compiled);
        }

        // ── variable resolution: globals and closures ───────────────────────────

        [Test]
        public void GlobalFreeVariable()
        {
            AssertMatches(
                "var BASE = 100; function f(x){ return x + BASE; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(i); i = i + 1; }\n__result__ = r;",
                "f", Chunk.JitStatus.Compiled);
        }

        [Test]
        public void ClosureCapture()
        {
            // The inner anonymous function reads outer capture k.
            AssertMatches(
                "function make(k){ return function inner(x){ return x * k; }; }\n" +
                "var g = make(7); var r=0; var i=0; while(i<1200){ r = g(i); i = i + 1; }\n__result__ = r;",
                "inner", Chunk.JitStatus.Compiled);
        }

        // ── unary not, booleans, pop, fall-through return ───────────────────────

        [Test]
        public void LogicalNot()
        {
            AssertMatches(
                "function f(x){ return !x; }\n" +
                "var r=0; var i=0; while(i<1200){ if (f(i - i)) { r = r + 1; } i = i + 1; }\n__result__ = r;",
                "f", Chunk.JitStatus.Compiled);
        }

        [Test]
        public void ExpressionStatementPopAndFallThrough()
        {
            // `a + 1;` is an expression statement (computed then popped); the function
            // has no explicit return on that path -> fall-through to undefined.
            AssertMatches(
                "function f(a){ a + 1; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(i); i = i + 1; }\n" +
                "__result__ = (r === undefined) ? 1 : 0;",
                "f", Chunk.JitStatus.Compiled);
        }

        // ── call dispatch ────────────────────────────────────────────────────────

        [Test]
        public void MonomorphicCall()
        {
            AssertMatches(
                "function g(x){ return x + 1; }\n" +
                "function f(x){ return g(x) + 0; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(i); i = i + 1; }\n__result__ = r;",
                "f", Chunk.JitStatus.Compiled);
        }

        [Test]
        public void MegamorphicCall()
        {
            AssertMatches(
                "function a(x){ return x + 1; }\n" +
                "function b(x){ return x + 2; }\n" +
                "function c(x){ return x + 3; }\n" +
                "function dispatch(fn, x){ return fn(x) + 0; }\n" +
                "var fns = [a, b, c];\n" +
                "var r=0; var i=0; while(i<1500){ r = r + dispatch(fns[i % 3], i); i = i + 1; }\n__result__ = r;",
                "dispatch", Chunk.JitStatus.Compiled);
        }

        // ── decline paths: unsupported constructs stay interpreted ──────────────

        // Control-flow cases (loops) are back-end-divergent (the closure back-end
        // declines them), so they live in JitControlFlowTests / JitAssignmentTests
        // rather than this shared both-back-ends matrix.

        [Test]
        public void TryCatchIsDeclined()
        {
            AssertMatches(
                "function f(x){ try { return x + 1; } catch (e) { return 0; } }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(i); i = i + 1; }\n__result__ = r;",
                "f", Chunk.JitStatus.Failed);
        }
    }

    /// <summary>Runs the full matrix against the Reflection.Emit back-end.</summary>
    [TestFixture]
    public sealed class ReflectionEmitJitCodeGenTests : JitCodeGenTestsBase
    {
        protected override IJitCompiler NewCompiler() => new ReflectionEmitJitCompiler();
    }

    /// <summary>Runs the full matrix against the closure-threaded (no-reflection) back-end.</summary>
    [TestFixture]
    public sealed class ClosureThreadedJitCodeGenTests : JitCodeGenTestsBase
    {
        protected override IJitCompiler NewCompiler() => new ClosureThreadedJitCompiler();
    }
}
