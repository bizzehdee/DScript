using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using DScript.Vm;
using DScript.Jit;

namespace DScript.Test
{
    /// <summary>
    /// Opt-in background compilation: hot chunks compile on a worker thread while the
    /// interpreter keeps running. Results must stay correct, compilation must
    /// eventually publish a delegate, and a chunk must not be compiled twice.
    /// NonParallelizable because JitRegistry (and its worker) are process-global.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class JitBackgroundTests
    {
        [TearDown]
        public void Reset()
        {
            JitRegistry.BackgroundCompilation = false;
            JitRegistry.Clear();
        }

        private sealed class CountingCompiler : IJitCompiler
        {
            public int CompileCount;
            private readonly IJitCompiler inner = new ReflectionEmitJitCompiler();
            public JitDelegate Compile(Chunk chunk)
            {
                Interlocked.Increment(ref CompileCount);
                return inner.Compile(chunk);
            }
        }

        private static bool WaitUntil(Func<bool> cond, int ms)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (cond()) return true;
                Thread.Sleep(5);
            }
            return cond();
        }

        private const string HotAdd =
            "function f(a,b){ return a + b; }\n" +
            "var r=0; var i=0; while(i<3000){ r = f(i, 3); i = i + 1; }\n__result__ = r;";

        private static (string result, Chunk f) Run(IJitCompiler c, bool background)
        {
            JitRegistry.BackgroundCompilation = background;
            if (c != null) JitRegistry.Register(c); else JitRegistry.Clear();
            var chunk = ScriptEngine.Compile(HotAdd);
            var engine = new ScriptEngine();
            engine.Run(chunk);
            return (engine.Root.GetParameter("__result__").String, chunk.Functions[0]);
        }

        [Test]
        public void BackgroundCompile_ResultMatchesInterpreter()
        {
            var interp = Run(null, background: false);
            var jit = Run(new ReflectionEmitJitCompiler(), background: true);

            Assert.That(WaitUntil(() => jit.f.JitState == Chunk.JitStatus.Compiled, 5000), Is.True,
                "the worker should eventually compile f");
            Assert.That(jit.result, Is.EqualTo(interp.result),
                "result is correct whether f ran interpreted or compiled");
        }

        [Test]
        public void BackgroundCompile_CompilesExactlyOnce()
        {
            var stub = new CountingCompiler();
            var (_, f) = Run(stub, background: true);

            Assert.That(WaitUntil(() => f.JitState == Chunk.JitStatus.Compiled, 5000), Is.True);
            Assert.That(stub.CompileCount, Is.EqualTo(1), "a chunk is enqueued for compilation once");

            // Running again uses the published delegate; no recompile.
            JitRegistry.Register(stub);
            var chunk2 = ScriptEngine.Compile(HotAdd);
            new ScriptEngine().Run(chunk2);
            WaitUntil(() => chunk2.Functions[0].JitState == Chunk.JitStatus.Compiled, 5000);
            Assert.That(stub.CompileCount, Is.EqualTo(2), "the second chunk compiles once too (distinct chunk)");
        }

        [Test]
        public void BackgroundCompile_CompiledDelegateRunsAfterPublish()
        {
            var jit = Run(new ReflectionEmitJitCompiler(), background: true);
            Assert.That(WaitUntil(() => jit.f.JitState == Chunk.JitStatus.Compiled, 5000), Is.True);
            Assert.That(jit.f.CompiledDelegate, Is.Not.Null, "delegate is published after background compile");
        }
    }
}
