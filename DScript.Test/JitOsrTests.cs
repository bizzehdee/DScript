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
        // JIT registered. Returns both results, how many OSR entries were compiled on
        // the top-level chunk, and how many of those used the fast unboxed-long loop
        // tier (vs the conservative fallback).
        private static (string interp, string jit, int osrEntries, long longTier) RunBoth(string script)
        {
            JitRegistry.Clear();
            var interpChunk = ScriptEngine.Compile(script);
            var interpEngine = new ScriptEngine();
            interpEngine.Run(interpChunk);
            var interp = interpEngine.Root.GetParameter("__result__").GetParsableString();

            var longBefore = ReflectionEmitJitCompiler.OsrLongLoopCompilations;
            JitRegistry.Register(new ReflectionEmitJitCompiler());
            var jitChunk = ScriptEngine.Compile(script);
            var jitEngine = new ScriptEngine();
            jitEngine.Run(jitChunk);
            var jit = jitEngine.Root.GetParameter("__result__").GetParsableString();
            JitRegistry.Clear();

            return (interp, jit, jitChunk.OsrEntries.Count,
                    ReflectionEmitJitCompiler.OsrLongLoopCompilations - longBefore);
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

        // ── unboxed-long loop tier (the sub-1s path) ─────────────────────────────

        [Test]
        public void LongTierEngagesForScalarLoop()
        {
            // Brace-free single-statement body keeps the loop region block-free so the
            // long tier (which declines block scopes) can engage.
            var r = RunBoth("var s=0; for(var i=0;i<50000;i++) s = s + i; __result__ = s;");
            Assert.That(r.jit, Is.EqualTo(r.interp));
            Assert.That(r.longTier, Is.GreaterThan(0), "the unboxed-long loop tier should engage");
        }

        [Test]
        public void LongTierInlinesIntLeafCall()
        {
            // The Functions-benchmark shape: a hot loop calling a pure int-leaf helper,
            // accumulating past int32. The long tier must inline the call and carry the
            // 64-bit sum exactly.
            var r = RunBoth(
                "function f(a,b,c){ return a+b+c; }\n" +
                "var s=0; for(var i=0;i<3000000;i++) s = s + f(i,1,2); __result__ = s;");
            Assert.That(r.jit, Is.EqualTo(r.interp));
            Assert.That(r.longTier, Is.GreaterThan(0));
        }

        [Test]
        public void LongTierAcceptsIntegerValuedDoubleBound()
        {
            // `1e7` is a double literal; the tier admits integer-valued double constants.
            var r = RunBoth("var s=0; for(var i=0;i<5e4;i++) s = s + i; __result__ = s;");
            Assert.That(r.jit, Is.EqualTo(r.interp));
            Assert.That(r.longTier, Is.GreaterThan(0));
        }

        [Test]
        public void LongTierWritesPromotedGlobalsBack()
        {
            // `s` is a global read after the loop via __result__; the tier must write the
            // register back to the environment before returning.
            var r = RunBoth("var s=0; var i=0; for(i=0;i<40000;i++) s = s + 2; __result__ = s + i;");
            Assert.That(r.jit, Is.EqualTo(r.interp));
            Assert.That(r.longTier, Is.GreaterThan(0));
        }

        [Test]
        public void LongTierFallsBackOnFractionalDouble()
        {
            // A fractional double constant in the loop can't flow as long, so the long
            // tier declines and the conservative OSR entry runs — still correct.
            var r = RunBoth("var s=0; for(var i=0;i<40000;i++) s = s + 1.5; __result__ = s;");
            Assert.That(r.jit, Is.EqualTo(r.interp));
            Assert.That(r.osrEntries, Is.GreaterThan(0), "conservative OSR still applies");
            Assert.That(r.longTier, Is.EqualTo(0), "long tier declines a fractional double");
        }

        [Test]
        public void LongTierAccumulatorAcrossInt32MatchesInterpreter()
        {
            // The register stays a 64-bit integer as it crosses int32 — no truncation,
            // no overflow deopt.
            var r = RunBoth("var s=0; for(var i=0;i<3000000;i++) s = s + i; __result__ = s;");
            Assert.That(r.jit, Is.EqualTo(r.interp));
            Assert.That(r.longTier, Is.GreaterThan(0));
        }
    }
}
