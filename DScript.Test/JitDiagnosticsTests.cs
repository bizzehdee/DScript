using NUnit.Framework;
using DScript.Vm;
using DScript.Jit;

namespace DScript.Test
{
    /// <summary>
    /// JIT observability: <see cref="JitDiagnostics"/> reports a chunk's state, hotness
    /// counters, deopt count, and decline reason.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class JitDiagnosticsTests
    {
        [TearDown] public void Clear() => JitRegistry.Clear();

        private static Chunk RunAndGetFunction(string script, int fnIndex, IJitCompiler c)
        {
            if (c != null) JitRegistry.Register(c); else JitRegistry.Clear();
            var chunk = ScriptEngine.Compile(script);
            new ScriptEngine().Run(chunk);
            return chunk.Functions[fnIndex];
        }

        [Test]
        public void CompiledChunkReportsCompiledNoDeclineReason()
        {
            var f = RunAndGetFunction(
                "function f(a,b){ return a + b; }\nvar r=0; var i=0; while(i<1200){ r=f(i,3); i=i+1; }\n__result__=r;",
                0, new ReflectionEmitJitCompiler());
            var report = JitDiagnostics.Describe(f);
            Assert.That(report.State, Is.EqualTo(Chunk.JitStatus.Compiled));
            Assert.That(report.IsCompiled, Is.True);
            Assert.That(report.DeclineReason, Is.Null);
            Assert.That(report.InvocationCount, Is.GreaterThanOrEqualTo(JitThresholds.InvocationThreshold));
        }

        [Test]
        public void TryCatchReportsUnsupportedOpcode()
        {
            var chunk = ScriptEngine.Compile("function f(x){ try { return x + 1; } catch (e) { return 0; } }");
            var report = JitDiagnostics.Describe(chunk.Functions[0]);
            Assert.That(report.DeclineReason, Does.StartWith("unsupported opcode:"));
            Assert.That(report.DeclineReason, Does.Contain("Try"));
        }

        [Test]
        public void GeneratorReportsGeneratorReason()
        {
            var chunk = ScriptEngine.Compile("function* g(){ yield 1; yield 2; }");
            var report = JitDiagnostics.Describe(chunk.Functions[0]);
            Assert.That(report.DeclineReason, Is.EqualTo("generator or async function"));
        }

        [Test]
        public void CompilableButColdHasNoDeclineReason()
        {
            // Never run, so Cold — but it is structurally compilable, so no reason.
            var chunk = ScriptEngine.Compile("function f(a,b){ return a + b; } __result__ = 1;");
            var report = JitDiagnostics.Describe(chunk.Functions[0]);
            Assert.That(report.State, Is.EqualTo(Chunk.JitStatus.Cold));
            Assert.That(report.DeclineReason, Is.Null);
            Assert.That(report.IsCompiled, Is.False);
        }

        [Test]
        public void DeoptCountReported()
        {
            var f = RunAndGetFunction(
                "function f(a,b){ return a + b; }\n" +
                "var r=0; var i=0; while(i<1200){ r=f(i,3); i=i+1; }\n" +
                "r = f(1.5, 3);\n__result__=r;",
                0, new ReflectionEmitJitCompiler());
            var report = JitDiagnostics.Describe(f);
            Assert.That(report.DeoptCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void DescribeAllCoversNestedFunctions()
        {
            var chunk = ScriptEngine.Compile(
                "function outer(){ function inner(x){ return x; } return inner; } __result__ = 1;");
            var reports = JitDiagnostics.DescribeAll(chunk);
            // root + outer + inner
            Assert.That(reports.Count, Is.GreaterThanOrEqualTo(3));
        }
    }
}
