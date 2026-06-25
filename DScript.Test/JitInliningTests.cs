using NUnit.Framework;
using DScript.Vm;
using DScript.Jit;

namespace DScript.Test
{
    /// <summary>
    /// Monomorphic inlining of pure-parameter leaf callees. A JIT-compiled caller
    /// that calls an eligible small helper splices it inline; ineligible callees fall
    /// back to dispatch. All results must match the interpreter.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class JitInliningTests
    {
        [TearDown] public void Clear() => JitRegistry.Clear();

        private static string Run(string src, IJitCompiler c)
        {
            if (c != null) JitRegistry.Register(c); else JitRegistry.Clear();
            var chunk = ScriptEngine.Compile(src);
            var engine = new ScriptEngine();
            engine.Run(chunk);
            return engine.Root.GetParameter("__result__").String;
        }

        // Wrap a helper + a JIT-compiled caller `f` driven past the threshold.
        private static string Wrap(string helpers, string fBody, string arg) =>
            helpers + "\n" +
            "function f(n){ var s = 0; var i = 0; while (i < n) { " + fBody + " i = i + 1; } return s; }\n" +
            "var r=0; var i=0; while(i<1200){ r = f(" + arg + "); i = i + 1; }\n__result__ = r;";

        private void Matches(string src)
            => Assert.That(Run(src, new ReflectionEmitJitCompiler()), Is.EqualTo(Run(src, null)));

        [Test]
        public void SingleParamHelperInlined()
            => Matches(Wrap("function sq(x){ return x * x; }", "s = s + sq(i);", "i % 40"));

        [Test]
        public void MultiParamHelperInlined()
            => Matches(Wrap("function dist2(a,b){ return a * a + b * b; }", "s = s + dist2(i, i + 1);", "i % 25"));

        [Test]
        public void HelperReadingParamPropertyInlined()
            => Matches(Wrap("function getx(o){ return o.x; }", "s = s + getx({ x: i });", "i % 30"));

        [Test]
        public void HelperWithConstantsInlined()
            => Matches(Wrap("function f2(x){ return x * 2 + 1; }", "s = s + f2(i);", "i % 50"));

        [Test]
        public void GlobalReadingHelperInlined()
            // Phase 7: a leaf helper that reads a global is now inlined — the free
            // variable resolves against the callee's captured (global) scope.
            => Matches("var BASE = 10; " + Wrap("function helper(x){ return x + BASE; }", "s = s + helper(i);", "i % 40"));

        [Test]
        public void BranchyHelperInlined()
            // Phase 7: a leaf helper containing control flow (no local mutation) is
            // spliced inline with a fresh label set.
            => Matches(Wrap(
                "function clamp(x){ if (x > 20) { return 20; } if (x < 5) { return 5; } return x; }",
                "s = s + clamp(i);", "i % 40"));

        [Test]
        public void BranchyGlobalHelperInlined()
            // Phase 7: control flow AND a global read combined in one inlined leaf.
            => Matches("var LIMIT = 15; " + Wrap(
                "function cap(x){ if (x > LIMIT) { return LIMIT; } return x; }",
                "s = s + cap(i);", "i % 40"));

        [Test]
        public void LoopingHelperWithLocalsFallsBack()
            // Local declarations/assignments are still not inlined (the body would need
            // its own environment) — it falls back to dispatch but matches the interpreter.
            => Matches(Wrap(
                "function tri(x){ var t = 0; var k = 0; while (k < x) { t = t + k; k = k + 1; } return t; }",
                "s = s + tri(i % 8);", "i % 20"));

        [Test]
        public void ClosureMakingHelperFallsBack()
            // A helper that defines an inner function (MakesClosure) is not inlined
            // (its body has a MakeClosure opcode the inliner declines); the result
            // still matches the interpreter.
            => Matches(Wrap(
                "function cap(x){ var g = function(){ return 1; }; return x + g(); }",
                "s = s + cap(i);", "i % 30"));

        [Test]
        public void NestedHelperCallFallsBack()
        {
            // outer() itself calls another function -> not a leaf -> dispatched.
            Matches(Wrap(
                "function inc(x){ return x + 1; } function outer(x){ return inc(x) + 1; }",
                "s = s + outer(i);", "i % 30"));
        }

        // ── bimorphic inlining: a site that sees two callees inlines both ─────────

        private static string RunSrc(string src, IJitCompiler c)
        {
            if (c != null) JitRegistry.Register(c); else JitRegistry.Clear();
            var chunk = ScriptEngine.Compile(src);
            var engine = new ScriptEngine();
            engine.Run(chunk);
            return engine.Root.GetParameter("__result__").String;
        }

        private void BimorphicMatches(string src)
            => Assert.That(RunSrc(src, new ReflectionEmitJitCompiler()), Is.EqualTo(RunSrc(src, null)));

        [Test]
        public void BimorphicBothInlined()
        {
            // f's call site `fn(x)` alternates between two pure-param leaves -> the
            // site is bimorphic; both bodies are guarded and inlined.
            BimorphicMatches(
                "function sq(x){ return x * x; }\n" +
                "function dbl(x){ return x + x; }\n" +
                "function f(fn, x){ return fn(x) + 0; }\n" +
                "var fns = [sq, dbl];\n" +
                "var r=0; var i=0; while(i<1500){ r = r + f(fns[i % 2], i % 30); i = i + 1; }\n__result__=r;");
        }

        [Test]
        public void BimorphicMixedCalleesInlined()
        {
            // A bimorphic site whose two callees are a pure leaf and a global-reading
            // leaf — both are now inline-eligible and guarded+inlined (Phase 7).
            BimorphicMatches(
                "var BASE = 7;\n" +
                "function sq(x){ return x * x; }\n" +
                "function glob(x){ return x + BASE; }\n" +
                "function f(fn, x){ return fn(x) + 0; }\n" +
                "var fns = [sq, glob];\n" +
                "var r=0; var i=0; while(i<1500){ r = r + f(fns[i % 2], i % 30); i = i + 1; }\n__result__=r;");
        }

        [Test]
        public void MegamorphicFallsBack()
        {
            // Three callees -> megamorphic -> no baked guard, general dispatch only.
            BimorphicMatches(
                "function a(x){ return x + 1; }\n" +
                "function b(x){ return x + 2; }\n" +
                "function c(x){ return x + 3; }\n" +
                "function f(fn, x){ return fn(x) + 0; }\n" +
                "var fns = [a, b, c];\n" +
                "var r=0; var i=0; while(i<1500){ r = r + f(fns[i % 3], i % 30); i = i + 1; }\n__result__=r;");
        }
    }
}
