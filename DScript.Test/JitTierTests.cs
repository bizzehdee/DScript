using NUnit.Framework;
using DScript.Vm;

namespace DScript.Test
{
    /// <summary>
    /// Exercises the VM tier-selection logic at the top of Execute(). The fixture is
    /// <see cref="NonParallelizableAttribute"/> because <see cref="JitRegistry"/> is
    /// process-global: a stub compiler registered here would otherwise leak into
    /// scripts run by other fixtures.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class JitTierTests
    {
        // A compiler whose delegate does nothing but record that it ran.
        private sealed class CountingCompiler : IJitCompiler
        {
            public int CompileCount;
            public int InvokeCount;

            public JitDelegate Compile(Chunk chunk)
            {
                CompileCount++;
                return (args, scope) => { InvokeCount++; return ScriptVar.CreateUndefined(); };
            }
        }

        // A compiler that always blows up during compilation.
        private sealed class ThrowingCompiler : IJitCompiler
        {
            public int CompileCount;

            public JitDelegate Compile(Chunk chunk)
            {
                CompileCount++;
                throw new System.InvalidOperationException("boom");
            }
        }

        // A compiler that declines every chunk by returning null.
        private sealed class DecliningCompiler : IJitCompiler
        {
            public int CompileCount;

            public JitDelegate Compile(Chunk chunk)
            {
                CompileCount++;
                return null;
            }
        }

        [TearDown]
        public void ClearRegistry() => JitRegistry.Clear();

        // Run the same chunk on the same engine until it crosses the invocation
        // threshold (each Execute entry bumps InvocationCount by one).
        private static void RunUntilHot(ScriptEngine engine, Chunk chunk)
        {
            for (var i = 0; i < JitThresholds.InvocationThreshold; i++)
                engine.Run(chunk);
        }

        [Test]
        public void NoCompilerRegistered_InterpreterRunsNormally()
        {
            JitRegistry.Clear();
            var chunk  = ScriptEngine.Compile("__result__ = 6 * 7;");
            var engine = new ScriptEngine();

            // Cross the threshold; with no compiler the chunk must stay Cold and the
            // interpreter must keep producing the correct result.
            RunUntilHot(engine, chunk);

            Assert.That(chunk.JitState, Is.EqualTo(Chunk.JitStatus.Cold));
            Assert.That(chunk.CompiledDelegate, Is.Null);
            Assert.That(engine.Root.GetParameter("__result__").Int, Is.EqualTo(42));
        }

        [Test]
        public void CompilerRegistered_DelegateInvokedAfterThreshold()
        {
            var stub = new CountingCompiler();
            JitRegistry.Register(stub);

            var chunk  = ScriptEngine.Compile("__result__ = 1;");
            var engine = new ScriptEngine();
            RunUntilHot(engine, chunk);

            Assert.That(chunk.JitState, Is.EqualTo(Chunk.JitStatus.Compiled));
            Assert.That(chunk.CompiledDelegate, Is.Not.Null);
            Assert.That(stub.CompileCount, Is.EqualTo(1), "compiled exactly once");
            Assert.That(stub.InvokeCount, Is.GreaterThanOrEqualTo(1),
                "the compiled delegate ran in place of the interpreter");
        }

        [Test]
        public void CompiledDelegate_ReusedWithoutRecompiling()
        {
            var stub = new CountingCompiler();
            JitRegistry.Register(stub);

            var chunk  = ScriptEngine.Compile("__result__ = 1;");
            var engine = new ScriptEngine();
            RunUntilHot(engine, chunk);   // crosses threshold → compiles + invokes once
            engine.Run(chunk);            // already Compiled → straight to delegate
            engine.Run(chunk);

            Assert.That(stub.CompileCount, Is.EqualTo(1), "never recompiled");
            Assert.That(stub.InvokeCount, Is.EqualTo(3),
                "delegate ran on the hot call and on both subsequent calls");
        }

        [Test]
        public void CompileException_SetsFailedAndFallsBackToInterpreter()
        {
            var stub = new ThrowingCompiler();
            JitRegistry.Register(stub);

            var chunk  = ScriptEngine.Compile("__result__ = 5 + 5;");
            var engine = new ScriptEngine();
            RunUntilHot(engine, chunk);

            Assert.That(chunk.JitState, Is.EqualTo(Chunk.JitStatus.Failed));
            Assert.That(chunk.CompiledDelegate, Is.Null);
            // The interpreter still ran on the call where compilation threw.
            Assert.That(engine.Root.GetParameter("__result__").Int, Is.EqualTo(10));
        }

        [Test]
        public void FailedChunk_NeverRecompiles()
        {
            var stub = new ThrowingCompiler();
            JitRegistry.Register(stub);

            var chunk  = ScriptEngine.Compile("__result__ = 1;");
            var engine = new ScriptEngine();
            RunUntilHot(engine, chunk);   // one failed compile attempt
            engine.Run(chunk);
            engine.Run(chunk);

            Assert.That(chunk.JitState, Is.EqualTo(Chunk.JitStatus.Failed));
            Assert.That(stub.CompileCount, Is.EqualTo(1),
                "a failed chunk is not retried on later invocations");
        }

        [Test]
        public void DecliningCompiler_MarksFailedAndInterprets()
        {
            var stub = new DecliningCompiler();
            JitRegistry.Register(stub);

            var chunk  = ScriptEngine.Compile("__result__ = 8;");
            var engine = new ScriptEngine();
            RunUntilHot(engine, chunk);

            Assert.That(chunk.JitState, Is.EqualTo(Chunk.JitStatus.Failed),
                "a compiler returning null is treated as a permanent decline");
            Assert.That(chunk.CompiledDelegate, Is.Null);
            Assert.That(engine.Root.GetParameter("__result__").Int, Is.EqualTo(8));
        }
    }
}
