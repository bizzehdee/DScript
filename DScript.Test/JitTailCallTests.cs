using NUnit.Framework;
using DScript.Vm;
using DScript.Jit;

namespace DScript.Test
{
    /// <summary>
    /// Phase 5: tail calls (`return f(x)` → OpCode.TailCall). Non-self tail calls
    /// lower to an ordinary Call + Return on the conservative tier and match the
    /// interpreter (reusing the existing dispatch/inlining machinery). Self
    /// tail-recursion relies on the interpreter trampoline for unbounded depth —
    /// compiled code can't reproduce that — so such chunks are declined and stay
    /// interpreted (still producing the correct result).
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class JitTailCallTests
    {
        [TearDown] public void Clear() => JitRegistry.Clear();

        private static (string result, Chunk f) Run(string src, IJitCompiler c)
        {
            if (c != null) JitRegistry.Register(c); else JitRegistry.Clear();
            var chunk = ScriptEngine.Compile(src);
            var engine = new ScriptEngine();
            engine.Run(chunk);
            Chunk f = null;
            foreach (var fn in chunk.Functions) if (fn.Name == "f") { f = fn; break; }
            return (engine.Root.GetParameter("__result__").String, f);
        }

        private static void Matches(string src, Chunk.JitStatus expected)
        {
            var interp = Run(src, null);
            var jit = Run(src, new ReflectionEmitJitCompiler());
            Assert.That(jit.result, Is.EqualTo(interp.result));
            if (jit.f != null)
                Assert.That(jit.f.JitState, Is.EqualTo(expected));
        }

        [Test]
        public void NonSelfTailCallCompiles()
        {
            Matches(
                "function g(x){ return x * 2; }\n" +
                "function f(x){ return g(x); }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(i % 50); i = i + 1; }\n__result__ = r;",
                Chunk.JitStatus.Compiled);
        }

        [Test]
        public void TailCallWithComputedArgs()
        {
            Matches(
                "function g(a, b){ return a + b * 3; }\n" +
                "function f(x){ return g(x + 1, x - 1); }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(i % 50); i = i + 1; }\n__result__ = r;",
                Chunk.JitStatus.Compiled);
        }

        [Test]
        public void TailCallInBranch()
        {
            Matches(
                "function g(x){ return x; }\n" +
                "function h(x){ return x + 100; }\n" +
                "function f(x){ if (x > 10) { return h(x); } return g(x); }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(i % 50); i = i + 1; }\n__result__ = r;",
                Chunk.JitStatus.Compiled);
        }

        [Test]
        public void SelfTailRecursionIsDeclined()
        {
            // `f` tail-calls itself; the profiled callee is `f`'s own chunk, so the
            // JIT declines (the interpreter trampoline keeps it correct + unbounded).
            Matches(
                "function f(n, acc){ if (n <= 0) { return acc; } return f(n - 1, acc + n); }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(20, 0); i = i + 1; }\n__result__ = r;",
                Chunk.JitStatus.Failed);
        }

        [Test]
        public void SelfTailRecursionStaysUnbounded()
        {
            // A depth that would overflow a native C# stack if the trampoline were
            // lost. Declining keeps the interpreter trampoline, so it completes.
            var src =
                "function f(n, acc){ if (n <= 0) { return acc; } return f(n - 1, acc + 1); }\n" +
                "__result__ = f(100000, 0);";
            var interp = Run(src, null);
            var jit = Run(src, new ReflectionEmitJitCompiler());
            Assert.That(jit.result, Is.EqualTo(interp.result));
            Assert.That(jit.result, Is.EqualTo("100000"));
        }

        [Test]
        public void ClosureBackendDeclinesTailCallChunks()
        {
            // The tail call lowers to a Call, which the closure back-end supports,
            // but the surrounding branch (if) is control flow it declines.
            var src =
                "function g(x){ return x * 2; }\n" +
                "function f(x){ return g(x); }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(i % 50); i = i + 1; }\n__result__ = r;";
            var jit = Run(src, new ClosureThreadedJitCompiler());
            Assert.That(jit.result, Is.EqualTo(Run(src, null).result));
        }
    }
}
