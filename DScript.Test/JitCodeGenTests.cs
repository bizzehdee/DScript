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

        // ── MakeClosure: a hot function that declares a nested function ─────────

        [Test]
        public void HotFunctionDeclaringNestedFunction()
        {
            // `outer` is the hot (compiled) function and contains a MakeClosure for
            // the nested `inner` declaration — exercises the JIT's MakeClosure path.
            AssertMatches(
                "function outer(n){ function inner(x){ return x + 1; } return inner(n); }\n" +
                "var r=0; var i=0; while(i<1200){ r = outer(i); i = i + 1; }\n__result__ = r;",
                "outer", Chunk.JitStatus.Compiled);
        }

        [Test]
        public void HotFunctionMakingCapturingClosure()
        {
            // The nested `inner` captures `outer`'s parameter `n`; the closure is
            // created inside the compiled `outer`, so the JIT-emitted MakeClosure
            // must capture the live frame environment for the capture to resolve.
            AssertMatches(
                "function outer(n){ function inner(){ return n * 2; } return inner(); }\n" +
                "var r=0; var i=0; while(i<1200){ r = outer(i); i = i + 1; }\n__result__ = r;",
                "outer", Chunk.JitStatus.Compiled);
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

        // ── property access through the JIT inline cache (Lever 2a) ─────────────

        [Test]
        public void PropertyReadObjectLiteral()
        {
            // o.a/o.b/o.c read a shaped object-literal through the per-site inline cache.
            AssertMatches(
                "function f(o){ return o.a + o.b + o.c; }\n" +
                "var obj = { a: 1, b: 2, c: 3 };\n" +
                "var r=0; var i=0; while(i<1200){ r = f(obj); i = i + 1; }\n__result__ = r;",
                "f", Chunk.JitStatus.Compiled);
        }

        [Test]
        public void PropertyReadClassInstance()
        {
            // Class instances are shaped; the inline cache hits on the shape-keyed path.
            AssertMatches(
                "class P { constructor(x){ this.x = x; this.y = x + 1; } }\n" +
                "var p = new P(10);\n" +
                "function f(o){ return o.x + o.y; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(p); i = i + 1; }\n__result__ = r;",
                "f", Chunk.JitStatus.Compiled);
        }

        [Test]
        public void PropertyReadGetterFallsBack()
        {
            // Accessor properties must bypass the data-property fast path and fall
            // through to JitGetPropCached, which invokes the getter.
            AssertMatches(
                "var obj = { _v: 5, get a(){ return this._v + 1; } };\n" +
                "function f(o){ return o.a; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(obj); i = i + 1; }\n__result__ = r;",
                "f", Chunk.JitStatus.Compiled);
        }

        [Test]
        public void PropertyReadPolymorphicShapes()
        {
            // Same read site sees two different shapes (distinct property orders) ->
            // bimorphic cache; results must stay correct across the alternation.
            AssertMatches(
                "var o1 = { a: 1, b: 2 };\n" +
                "var o2 = { b: 10, a: 20 };\n" +
                "function f(o){ return o.a + o.b; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f((i % 2 === 0) ? o1 : o2); i = i + 1; }\n__result__ = r;",
                "f", Chunk.JitStatus.Compiled);
        }

        [Test]
        public void PropertyReadMissingProperty()
        {
            // Reading an absent property must yield undefined (cache miss -> resolve).
            // Keep f branch-free (the closure back-end declines control flow); do the
            // undefined check at the interpreted call site.
            AssertMatches(
                "var obj = { a: 1 };\n" +
                "function f(o){ return o.missing; }\n" +
                "var r; var i=0; while(i<1200){ r = f(obj); i = i + 1; }\n" +
                "__result__ = (r === undefined) ? 1 : 0;",
                "f", Chunk.JitStatus.Compiled);
        }

        // ── property writes through the JIT inline cache (Lever 2b) ─────────────

        [Test]
        public void PropertyWriteExisting()
        {
            // Overwrites an existing own data property in place via the write cache.
            AssertMatches(
                "var obj = { a: 0, b: 99 };\n" +
                "function f(o){ o.a = o.a + 1; return o.a; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(obj); i = i + 1; }\n__result__ = r;",
                "f", Chunk.JitStatus.Compiled);
        }

        [Test]
        public void PropertyWriteClassInstanceField()
        {
            AssertMatches(
                "class P { constructor(){ this.x = 0; } }\n" +
                "var p = new P();\n" +
                "function f(o){ o.x = o.x + 2; return o.x; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(p); i = i + 1; }\n__result__ = r;",
                "f", Chunk.JitStatus.Compiled);
        }

        [Test]
        public void PropertyWriteNewPropertyTransitionsShape()
        {
            // Adding a brand-new property transitions the shape -> the write cache must
            // miss and fall back to the full define path, staying correct.
            AssertMatches(
                "function f(o){ o.z = 7; return o.z; }\n" +
                "var r=0; var i=0; while(i<1200){ var obj = { a: 1 }; r = f(obj); i = i + 1; }\n__result__ = r;",
                "f", Chunk.JitStatus.Compiled);
        }

        // (Non-writable / frozen property writes need Object.freeze from DScript.Extras,
        // which this bare-engine harness does not register; that case is covered in
        // JitPropertyCacheExtrasTests, which runs a full Extras-enabled engine.)

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
