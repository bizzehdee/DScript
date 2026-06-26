using NUnit.Framework;
using DScript.Vm;
using DScript.Jit;

namespace DScript.Test
{
    /// <summary>
    /// On-stack replacement (OSR): a long-running loop in a function that is only
    /// entered once would never tier up on invocation count, so the VM compiles the
    /// chunk mid-flight and resumes execution in JIT code at the loop header. These
    /// tests assert the OSR result is identical to the interpreter and that OSR
    /// actually fired (an OSR entry was compiled and cached on the chunk).
    /// NonParallelizable because JitRegistry is process-global.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class JitOsrTests
    {
        [TearDown] public void Clear() => JitRegistry.Clear();

        // Run a top-level script once, interpreted and then with the Reflection.Emit
        // JIT registered. Returns both results plus how many OSR entries were compiled
        // on the top-level chunk during the JIT run.
        private static (string interp, string jit, int osrEntries) RunBoth(string script)
        {
            JitRegistry.Clear();
            var interpChunk = ScriptEngine.Compile(script);
            var interpEngine = new ScriptEngine();
            interpEngine.Run(interpChunk);
            var interp = interpEngine.Root.GetParameter("__result__").GetParsableString();

            JitRegistry.Register(new ReflectionEmitJitCompiler());
            var jitChunk = ScriptEngine.Compile(script);
            var jitEngine = new ScriptEngine();
            jitEngine.Run(jitChunk);
            var jit = jitEngine.Root.GetParameter("__result__").GetParsableString();
            JitRegistry.Clear();

            return (interp, jit, jitChunk.OsrEntries.Count);
        }

        [Test]
        public void OsrCompilesHotLoopEnteredOnce()
        {
            // The top-level frame is entered exactly once; only OSR can compile it.
            var r = RunBoth("var s=0; for(var i=0;i<20000;i++){ s = s + i; } __result__ = s;");
            Assert.That(r.jit, Is.EqualTo(r.interp), "OSR result must match interpreter");
            Assert.That(r.osrEntries, Is.GreaterThan(0), "OSR entry should have been compiled");
        }

        [Test]
        public void OsrWithLetLoopVariable()
        {
            var r = RunBoth("let s=0; for(let i=0;i<20000;i++){ s = s + i*2; } __result__ = s;");
            Assert.That(r.jit, Is.EqualTo(r.interp));
            Assert.That(r.osrEntries, Is.GreaterThan(0));
        }

        [Test]
        public void OsrAccumulatorBeyondInt32IsCorrect()
        {
            // The sum (~5e13) overflows int32, so the conservative tier's 64-bit
            // integer path must carry it without truncation under OSR.
            var r = RunBoth(
                "function f(a,b,c){ return a+b+c; }\n" +
                "var s=0; for(var i=0;i<2000000;i++){ s = s + f(i,1,2); }\n__result__ = s;");
            Assert.That(r.jit, Is.EqualTo(r.interp));
            Assert.That(r.osrEntries, Is.GreaterThan(0));
        }

        [Test]
        public void OsrInFunctionDeclaringNestedFunction()
        {
            // The loop body calls a nested (MakeClosure) function; OSR must compile a
            // chunk that contains MakeClosure in its (skipped) prologue.
            var r = RunBoth(
                "function f(x){ return x * 3; }\n" +
                "var s=0; for(var i=0;i<20000;i++){ s = s + f(i); } __result__ = s;");
            Assert.That(r.jit, Is.EqualTo(r.interp));
            Assert.That(r.osrEntries, Is.GreaterThan(0));
        }

        [Test]
        public void OsrWhileLoopMatchesInterpreter()
        {
            var r = RunBoth("var s=0; var i=0; while(i<20000){ s = s + (i % 7); i = i + 1; } __result__ = s;");
            Assert.That(r.jit, Is.EqualTo(r.interp));
            Assert.That(r.osrEntries, Is.GreaterThan(0));
        }

        [Test]
        public void OsrDoesNotFireBelowThreshold()
        {
            // A short loop (well under the OSR back-edge threshold) stays interpreted.
            var r = RunBoth("var s=0; for(var i=0;i<100;i++){ s = s + i; } __result__ = s;");
            Assert.That(r.jit, Is.EqualTo(r.interp));
            Assert.That(r.osrEntries, Is.EqualTo(0), "no OSR below the back-edge threshold");
        }

        [Test]
        public void OsrNestedLoopMatchesInterpreter()
        {
            var r = RunBoth(
                "var s=0; for(var i=0;i<200;i++){ for(var j=0;j<200;j++){ s = s + 1; } } __result__ = s;");
            Assert.That(r.jit, Is.EqualTo(r.interp));
            Assert.That(r.osrEntries, Is.GreaterThan(0));
        }
    }
}
