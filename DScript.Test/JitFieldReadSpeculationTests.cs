using DScript.Jit;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    /// <summary>
    /// Lever 2c: a pure numeric function that reads object fields should compile through
    /// the unboxed speculative-int tier (Reflection.Emit back-end) — prefetching each
    /// <c>receiver.field</c> as a guarded raw int in the prologue — rather than falling to
    /// the conservative boxed tier. The closure back-end has no unboxed numeric tiers, so
    /// these cases are Reflection.Emit-only. NonParallelizable: JitRegistry is global.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class JitFieldReadSpeculationTests
    {
        [TearDown]
        public void Clear()
        {
            JitRegistry.Clear();
            ReflectionEmitJitCompiler.DisableFieldReadSpeculation = false;
        }

        private static string RunInterp(string s)
        {
            JitRegistry.Clear();
            var e = new ScriptEngine();
            e.Run(ScriptEngine.Compile(s));
            return e.Root.GetParameter("__result__").GetParsableString();
        }

        private static (string result, long fieldCompiles) RunJit(string s)
        {
            JitRegistry.Clear();
            JitRegistry.Register(new ReflectionEmitJitCompiler());
            var before = ReflectionEmitJitCompiler.SpeculativeFieldReadCompilations;
            var e = new ScriptEngine();
            e.Run(ScriptEngine.Compile(s));
            return (e.Root.GetParameter("__result__").GetParsableString(),
                    ReflectionEmitJitCompiler.SpeculativeFieldReadCompilations - before);
        }

        [Test]
        public void IntFieldArithmeticCompilesViaSpeculativeTier()
        {
            const string src =
                "function dist2(p){ return p.x*p.x + p.y*p.y; }\n" +
                "var p = { x:3, y:4 }; var r=0; var i=0;\n" +
                "while(i<200000){ r = dist2(p); i=i+1; }\n__result__ = r;";
            var jit = RunJit(src);
            Assert.That(jit.result, Is.EqualTo(RunInterp(src)), "result must match interpreter");
            Assert.That(jit.result, Is.EqualTo("25"));
            Assert.That(jit.fieldCompiles, Is.GreaterThan(0),
                "dist2 should compile via the unboxed-int field-read tier");
        }

        [Test]
        public void IntFieldArithmeticInLoopCompilesViaSpeculativeTier()
        {
            // A loop function that reads a field every iteration, called enough times to
            // tier up via invocation count into the unboxed int-loop tier (Lever 2c).
            // Uses `var` (block scopes go to the conservative tier) and a small inner loop
            // (a large per-call loop would trip OSR before invocation tier-up).
            const string src =
                "function sumField(o, n){ var s=0; var i=0; while(i<n){ s = s + o.k; i = i+1; } return s; }\n" +
                "var r=0; var c=0; while(c<1500){ r = sumField({k:7}, c % 40); c=c+1; }\n__result__ = r;";
            var jit = RunJit(src);
            Assert.That(jit.result, Is.EqualTo(RunInterp(src)), "result must match interpreter");
            Assert.That(jit.fieldCompiles, Is.GreaterThan(0),
                "loop field read should compile via the unboxed int-loop tier");
        }

        [Test]
        public void OsrFieldLoopCompilesViaLongTier()
        {
            // Single call with a large loop -> OSR path. The OSR long-loop tier should
            // prefetch the field and run unboxed (Lever 2c).
            const string src =
                "function sumField(o, n){ var s=0; var i=0; while(i<n){ s = s + o.k; i = i+1; } return s; }\n" +
                "__result__ = sumField({k:7}, 50000);";
            var jit = RunJit(src);
            Assert.That(jit.result, Is.EqualTo(RunInterp(src)), "result must match interpreter");
            Assert.That(jit.result, Is.EqualTo("350000"));
            Assert.That(jit.fieldCompiles, Is.GreaterThan(0),
                "OSR long-loop tier should compile the field read");
        }

        [Test]
        public void MixedObjectReceiverAndNumericArg()
        {
            const string src =
                "function fma(o,k){ return o.a*k + o.b; }\n" +
                "var o = { a:6, b:7 }; var r=0; var i=0;\n" +
                "while(i<200000){ r = fma(o,3); i=i+1; }\n__result__ = r;";
            var jit = RunJit(src);
            Assert.That(jit.result, Is.EqualTo(RunInterp(src)));
            Assert.That(jit.result, Is.EqualTo("25"));
            Assert.That(jit.fieldCompiles, Is.GreaterThan(0));
        }

        [Test]
        public void DeoptsCleanlyWhenFieldBecomesNonInt()
        {
            // Compile with int fields, then make a field a double: the prologue int guard
            // must deopt and the interpreter must produce the correct double result.
            const string src =
                "function dist2(p){ return p.x*p.x + p.y*p.y; }\n" +
                "var p = { x:3, y:4 }; var r=0; var i=0;\n" +
                "while(i<200000){ r = dist2(p); i=i+1; }\n" +
                "p.x = 1.5;\n" +              // 1.5*1.5 + 4*4 = 18.25
                "__result__ = dist2(p);";
            Assert.That(RunJit(src).result, Is.EqualTo(RunInterp(src)));
        }

        [Test]
        public void DisableFlagForcesConservativeTier()
        {
            ReflectionEmitJitCompiler.DisableFieldReadSpeculation = true;
            const string src =
                "function dist2(p){ return p.x*p.x + p.y*p.y; }\n" +
                "var p = { x:3, y:4 }; var r=0; var i=0;\n" +
                "while(i<200000){ r = dist2(p); i=i+1; }\n__result__ = r;";
            var jit = RunJit(src);
            Assert.That(jit.result, Is.EqualTo("25"), "still correct via the conservative tier");
            Assert.That(jit.fieldCompiles, Is.EqualTo(0), "field speculation must be disabled");
        }
    }
}
