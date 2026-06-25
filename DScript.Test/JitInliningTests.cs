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
        public void GlobalReadingHelperFallsBack()
            => Matches("var BASE = 10; " + Wrap("function helper(x){ return x + BASE; }", "s = s + helper(i);", "i % 40"));

        [Test]
        public void LoopingHelperFallsBack()
            => Matches(Wrap(
                "function tri(x){ var t = 0; var k = 0; while (k < x) { t = t + k; k = k + 1; } return t; }",
                "s = s + tri(i % 8);", "i % 20"));

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
        public void BimorphicOneInlinableOneNot()
        {
            // One callee reads a global (not inlinable) -> only the eligible one is
            // guarded+inlined; the other and any miss go through general dispatch.
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
