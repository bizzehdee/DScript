using DScript.Jit;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    /// <summary>
    /// The closure long-register loop tier under positional local slots
    /// (EnableLocalSlots, as used by NativeAOT builds). The main test suite runs with
    /// slots off, so these exercise the slot path: GetLocal/SetLocal registers loaded
    /// and guarded from the frame at the OSR resume, int-leaf call inlining with slot
    /// parameters, comparison branches, and the callee-identity / non-integer fallbacks.
    /// Each result must match the interpreter. NonParallelizable: JitRegistry and the
    /// EnableLocalSlots flag are process-global.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class JitLongLoopSlotTests
    {
        [SetUp]    public void On()  => ScriptEngine.EnableLocalSlots = true;
        [TearDown] public void Off() { JitRegistry.Clear(); ScriptEngine.EnableLocalSlots = false; }

        private static string Interp(string s)
        {
            JitRegistry.Clear();
            var e = new ScriptEngine();
            e.Run(ScriptEngine.Compile(s));
            return e.Root.GetParameter("__result__").GetParsableString();
        }

        private static string Closure(string s)
        {
            JitRegistry.Clear();
            JitRegistry.Register(new ClosureThreadedJitCompiler());
            var e = new ScriptEngine();
            e.Run(ScriptEngine.Compile(s));
            return e.Root.GetParameter("__result__").GetParsableString();
        }

        private static void Matches(string s) => Assert.That(Closure(s), Is.EqualTo(Interp(s)));

        [Test]
        public void SingleBigLoop()
            => Matches("function big(n){ var s=0; var i=0; while(i<n){ s=s+i; i=i+1; } return s; }\n" +
                       "__result__ = big(2000000);");

        [Test]
        public void IntLeafCallInlinedInLoop()
            => Matches("function f(a,b,c){ return a+b+c; }\n" +
                       "function bench(n){ var s=0; var i=0; while(i<n){ s = s + f(i,1,2); i=i+1; } return s; }\n" +
                       "__result__ = bench(2000000);");

        [Test]
        public void LoopWithComparisonBranch()
            => Matches("function f(n){ var s=0; var i=0; while(i<n){ if(i<1000){ s=s+1; } else { s=s+2; } i=i+1; } return s; }\n" +
                       "__result__ = f(2000000);");

        [Test]
        public void MultiplyAccumulateBeyondInt32()
            => Matches("function f(n){ var s=0; var i=0; while(i<n){ s = s + i*i; i=i+1; } return s; }\n" +
                       "__result__ = f(200000);");

        [Test]
        public void NonIntegerArgFallsBack()
            // Loop bound is a non-integer: the entry guard must fail and the boxed path run.
            => Matches("function f(n){ var s=0; var i=0; while(i<n){ s=s+i; i=i+1; } return s; }\n" +
                       "__result__ = f(2000000.5);");

        [Test]
        public void CalleeReassignedBetweenCallsFallsBack()
            => Matches("function a(x){ return x+1; }\nfunction b(x){ return x+1000; }\nvar fn=a;\n" +
                       "function g(n){ var s=0; var i=0; while(i<n){ s = s + fn(i); i=i+1; } return s; }\n" +
                       "var r=0; var c=0; while(c<3000){ if(c==2000){ fn=b; } r=g(50); c=c+1; }\n__result__ = r;");

        // ── nested function declaration inlined from its compile-time chunk ──
        // This is the bench.ds "Functions" shape: a helper declared inside the function
        // running the loop. Each invocation makes a fresh closure, so the callee is
        // resolved by chunk (not runtime identity) and its pure body is spliced.

        [Test]
        public void NestedFunctionDeclarationInlined()
            => Matches("var g = () => { function f(a,b,c){ return a+b+c; } var s=0; for(let i=0;i<2000000;i++){ s += f(i,1,2); } return s; };\n" +
                       "__result__ = g();");

        [Test]
        public void NestedCalleeReassignedFallsBack()
            // f is declared, then reassigned mid-loop: must not splice the stale declaration.
            => Matches("var g = () => { function f(x){ return x+1; } var s=0; for(let i=0;i<200000;i++){ if(i==1000){ f = function(x){ return x+1000; }; } s += f(i); } return s; };\n" +
                       "__result__ = g();");

        [Test]
        public void NestedCalleeCapturingFreeVarFallsBack()
            // f reads an outer variable (not a pure parameter-only leaf): must decline inlining.
            => Matches("var g = () => { var k=7; function f(x){ return x+k; } var s=0; for(let i=0;i<200000;i++){ s += f(i); } return s; };\n" +
                       "__result__ = g();");

        [Test]
        public void NestedCalleeUsedAsValueFallsBack()
            // f's closure escapes (aliased), so its creation cannot be elided.
            => Matches("var g = () => { function f(x){ return x+1; } var h=f; var s=0; for(let i=0;i<200000;i++){ s += f(i); } return s + h(0); };\n" +
                       "__result__ = g();");

        [Test]
        public void DoubleLoopBoundInlined()
            // 5e5 is a double literal, so the i<bound comparison profiles Int<Double; the
            // long tier must still admit it (the integral-double constant becomes a long).
            // This is exactly the bench.ds Functions shape.
            => Matches("var g = () => { function f(a,b,c){ return a+b+c; } var s=0; for(let i=0;i<5e5;i++){ s += f(i,1,2); } return s; };\n" +
                       "__result__ = g();");

        [Test]
        public void ExactBenchFunctionsShapeInlined()
            // Verbatim bench.ds "Functions": const arrow, nested f, let s (which wraps the
            // body in a block scope), for(let i) with a double bound, and += . Exercises the
            // block-scope no-op handling together with nested-callee inlining.
            => Matches("const bench = () => { function f(a,b,c){ return a+b+c; } let s=0; for(let i=0;i<5e5;i++) s += f(i,1,2); return s; };\n" +
                       "__result__ = bench();");

        [Test]
        public void BlockScopedLetLoop()
            // let-scoped accumulator and counter, no helper — the body block scope must be
            // transparent to the register frame.
            => Matches("const g = () => { let s=0; for(let i=0;i<2000000;i++){ let t=i+1; s += t; } return s; };\n" +
                       "__result__ = g();");

        [Test]
        public void StringConcatLoopFallsBack()
            // The += sees strings, so the (relaxed) numeric profile gate must still decline.
            => Matches("function g(n){ var s=\"\"; var i=0; while(i<n){ s = s + \"x\"; i=i+1; } return s.length; }\n" +
                       "__result__ = g(5000);");

        // ── constant folding in the long tier ──────────────────────────────────────
        // The long tier folds const-const operations and bakes a single constant operand
        // directly into the arithmetic, removing per-iteration delegate dispatch. The
        // folded result must equal the interpreter's runtime arithmetic exactly.

        [Test]
        public void ConstChainFoldedInLoop()
            // (i + 1) + 2 + 3 — a chain mixing a runtime operand with several constants;
            // the constant tail must fold without changing the result.
            => Matches("function f(n){ var s=0; var i=0; while(i<n){ s = s + (i + 1 + 2 + 3); i=i+1; } return s; }\n" +
                       "__result__ = f(2000000);");

        [Test]
        public void AllConstantLeafFullyFolded()
            // Every argument is constant, so the inlined leaf a+b+c folds to a single
            // constant per iteration (no runtime operand survives in the callee body).
            => Matches("function f(a,b,c){ return a+b+c; }\n" +
                       "function bench(n){ var s=0; var i=0; while(i<n){ s = s + f(10,20,30); i=i+1; } return s; }\n" +
                       "__result__ = bench(2000000);");

        [Test]
        public void ConstMultiplyFoldWrapsLikeRuntime()
            // 100000 * 100000 = 1e10 overflows int32; the compile-time fold and the
            // runtime int64 multiply must wrap identically.
            => Matches("function f(n){ var s=0; var i=0; while(i<n){ s = s + (100000 * 100000) - i; i=i+1; } return s; }\n" +
                       "__result__ = f(200000);");

        [Test]
        public void ConstOnLeftOfSubtraction()
            // Constant on the left of a non-commutative operator must keep operand order
            // (1000 - i, not i - 1000).
            => Matches("function f(n){ var s=0; var i=0; while(i<n){ s = s + (1000 - i); i=i+1; } return s; }\n" +
                       "__result__ = f(200000);");
    }
}
