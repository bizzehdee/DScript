using NUnit.Framework;
using DScript.Vm;
using DScript.Jit;

namespace DScript.Test
{
    /// <summary>
    /// Control-flow compilation (Reflection.Emit conservative tier): branching
    /// functions must match the interpreter. The closure back-end declines control
    /// flow. Loops with mutable accumulators arrive with assignment support (Phase 8).
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class JitControlFlowTests
    {
        [TearDown] public void Clear() => JitRegistry.Clear();

        private static (string result, Chunk.JitStatus st) Run(string fn, string call, IJitCompiler c)
        {
            if (c != null) JitRegistry.Register(c); else JitRegistry.Clear();
            var s = fn + "\nvar r=0; var i=0; while(i<1200){ r = " + call + "; i = i + 1; }\n__result__ = r;";
            var chunk = ScriptEngine.Compile(s);
            var engine = new ScriptEngine();
            engine.Run(chunk);
            return (engine.Root.GetParameter("__result__").String, chunk.Functions[0].JitState);
        }

        [TestCase("function f(n){ if(n<0){return 0;} if(n<10){return 1;} if(n<100){return 2;} return 3; }", "f(i % 150)")]
        [TestCase("function f(x){ if (x < 0) return 0 - x; return x; }", "f(i - 600)")]
        [TestCase("function f(a,b){ if (a > b) { return a; } else { return b; } }", "f(i, 600)")]
        [TestCase("function f(n){ if (n < 5) { if (n < 2) { return 0; } return 1; } return 2; }", "f(i % 8)")]
        [TestCase("function f(n){ if (n < 0) return n + 100; if (n < 50) return n * 2; return n - 1; }", "f(i % 120 - 10)")]
        public void BranchingMatchesInterpreter(string fn, string call)
        {
            var interp = Run(fn, call, null);
            var jit = Run(fn, call, new ReflectionEmitJitCompiler());
            Assert.That(jit.st, Is.EqualTo(Chunk.JitStatus.Compiled), "conservative tier compiles control flow");
            Assert.That(jit.result, Is.EqualTo(interp.result));
        }

        [Test]
        public void BranchWithCallMatchesInterpreter()
        {
            // A branch whose arms call another function.
            var fn = "function dbl(x){ return x + x; } function f(n){ if (n < 0) return dbl(0 - n); return dbl(n); }";
            var interp = Run(fn, "f(i - 600)", null);
            var jit = Run(fn, "f(i - 600)", new ReflectionEmitJitCompiler());
            // Functions[1] is f (the branching one).
            Assert.That(jit.result, Is.EqualTo(interp.result));
        }

        [Test]
        public void ClosureBackendCompilesControlFlow()
        {
            // The closure back-end now lowers structured control flow to basic blocks
            // driven by a program counter (no Reflection.Emit), so branching functions
            // compile and must match the interpreter.
            var fn = "function f(n){ if (n < 0) return 0; return 1; }";
            var jit = Run(fn, "f(i - 600)", new ClosureThreadedJitCompiler());
            Assert.That(jit.st, Is.EqualTo(Chunk.JitStatus.Compiled),
                "the closure back-end compiles structured branches via basic blocks");
            var interp = Run(fn, "f(i - 600)", null);
            Assert.That(jit.result, Is.EqualTo(interp.result));
        }

        [TestCase("function f(n){ if(n<0){return 0;} if(n<10){return 1;} if(n<100){return 2;} return 3; }", "f(i % 150)")]
        [TestCase("function f(x){ if (x < 0) return 0 - x; return x; }", "f(i - 600)")]
        [TestCase("function f(a,b){ if (a > b) { return a; } else { return b; } }", "f(i, 600)")]
        [TestCase("function f(n){ if (n < 5) { if (n < 2) { return 0; } return 1; } return 2; }", "f(i % 8)")]
        public void ClosureBranchingMatchesInterpreter(string fn, string call)
        {
            var interp = Run(fn, call, null);
            var jit = Run(fn, call, new ClosureThreadedJitCompiler());
            Assert.That(jit.st, Is.EqualTo(Chunk.JitStatus.Compiled), "closure compiles control flow");
            Assert.That(jit.result, Is.EqualTo(interp.result));
        }

        [Test]
        public void ClosureLoopMatchesInterpreter()
        {
            // A loop with a mutable accumulator (back-edges) inside the JIT-compiled
            // function body — exercises the closure back-end's block driver.
            var fn = "function f(n){ var s = 0; for (var k = 0; k < n; k = k + 1) { s = s + k; } return s; }";
            var interp = Run(fn, "f(i % 50)", null);
            var jit = Run(fn, "f(i % 50)", new ClosureThreadedJitCompiler());
            Assert.That(jit.st, Is.EqualTo(Chunk.JitStatus.Compiled), "closure compiles loops");
            Assert.That(jit.result, Is.EqualTo(interp.result));
        }
    }
}
