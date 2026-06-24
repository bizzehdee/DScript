using NUnit.Framework;
using DScript.Vm;
using DScript.Jit;

namespace DScript.Test
{
    /// <summary>
    /// Deoptimization of the speculative unboxed-int tier: a type surprise must bail
    /// to the interpreter and still produce the correct result, and repeated deopts
    /// must give up on speculation and recompile with the conservative tier.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class JitDeoptTests
    {
        [TearDown] public void Clear() => JitRegistry.Clear();

        private static (string result, Chunk f) Run(string script, bool jit)
        {
            if (jit) JitRegistry.Register(new ReflectionEmitJitCompiler());
            else JitRegistry.Clear();
            var chunk = ScriptEngine.Compile(script);
            var engine = new ScriptEngine();
            engine.Run(chunk);
            return (engine.Root.GetParameter("__result__").String, chunk.Functions[0]);
        }

        // Make f hot and Int-only (-> speculative), then a single non-int call.
        private const string WarmThenSurprise =
            "function f(a,b){ return a + b; }\n" +
            "var r=0; var i=0; while(i<1200){ r = f(i, 3); i = i + 1; }\n";

        [Test]
        public void DoubleArgument_DeoptsAndMatchesInterpreter()
        {
            var script = WarmThenSurprise + "r = f(1.5, 3);\n__result__ = r;";
            var interp = Run(script, false);
            var jit = Run(script, true);
            Assert.That(jit.result, Is.EqualTo(interp.result));
            Assert.That(jit.result, Is.EqualTo("4.5"));
            Assert.That(jit.f.DeoptCount, Is.GreaterThanOrEqualTo(1), "the double arg deopted");
        }

        [Test]
        public void StringArgument_DeoptsAndMatchesInterpreter()
        {
            // a is a string -> not int -> deopt -> interpreter does string concatenation.
            var script = WarmThenSurprise + "r = f('x', 3);\n__result__ = r;";
            var interp = Run(script, false);
            var jit = Run(script, true);
            Assert.That(jit.result, Is.EqualTo(interp.result));
            Assert.That(jit.result, Is.EqualTo("x3"));
        }

        [Test]
        public void RepeatedDeopts_RecompileToConservativeTier()
        {
            // 10 non-int calls: the first DeoptThreshold deopt, then the chunk is
            // recompiled with the conservative tier and stops deopting.
            var script = WarmThenSurprise +
                         "var k=0; while(k<10){ r = f(2.5, 3); k = k + 1; }\n__result__ = r;";
            var interp = Run(script, false);
            var jit = Run(script, true);

            Assert.That(jit.result, Is.EqualTo(interp.result));
            Assert.That(jit.result, Is.EqualTo("5.5"));
            Assert.That(jit.f.DeoptCount, Is.EqualTo(JitThresholds.DeoptThreshold),
                "deopting stops once the conservative tier takes over");
            Assert.That(jit.f.PreferConservativeTier, Is.True);
            Assert.That(jit.f.JitState, Is.EqualTo(Chunk.JitStatus.Compiled),
                "recompiled with the conservative tier");
        }

        [Test]
        public void ConservativeTier_StillCorrectAfterGivingUp()
        {
            // After giving up on speculation, mixed int/double calls must still work.
            var script = WarmThenSurprise +
                         "var k=0; var s=0; while(k<20){ s = s + f(k + 0.5, 1); k = k + 1; }\n__result__ = s;";
            var interp = Run(script, false);
            var jit = Run(script, true);
            Assert.That(jit.result, Is.EqualTo(interp.result));
        }

        [Test]
        public void IntDivision_HandledByConservativeTier()
        {
            // '/' is excluded from the speculative tier, so this compiles conservatively
            // and division-by-zero yields the interpreter's double result (Infinity).
            var script =
                "function f(a,b){ return a / b; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(i, 2); i = i + 1; }\n" +
                "r = f(5, 0);\n__result__ = r;";
            var interp = Run(script, false);
            var jit = Run(script, true);
            Assert.That(jit.result, Is.EqualTo(interp.result));
            Assert.That(jit.f.JitState, Is.EqualTo(Chunk.JitStatus.Compiled));
        }
    }
}
