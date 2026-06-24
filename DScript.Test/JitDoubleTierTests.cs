using NUnit.Framework;
using DScript.Vm;
using DScript.Jit;

namespace DScript.Test
{
    /// <summary>
    /// The speculative unboxed-double tier: floating-point pure functions must match
    /// the interpreter, and a non-numeric surprise must deoptimize and still return
    /// the correct value.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class JitDoubleTierTests
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

        private static string Warm(string body, string aExpr, string bExpr) =>
            "function f(a,b){ return " + body + "; }\n" +
            "var r=0; var i=0; while(i<1200){ r = f(" + aExpr + ", " + bExpr + "); i = i + 1; }\n" +
            "__result__ = r;";

        [TestCase("a + b", "i + 0.5", "2.25")]
        [TestCase("a - b", "i + 0.5", "2.25")]
        [TestCase("a * b", "i + 0.5", "2.0")]
        [TestCase("a / b", "i + 0.5", "4.0")]
        [TestCase("a + b", "i", "2.5")]      // mixed int/double
        [TestCase("(a + b) * 2.0 - 1.5", "i + 0.5", "3.0")]
        public void DoubleArithmetic_MatchesInterpreter(string body, string a, string bb)
        {
            var script = Warm(body, a, bb);
            var interp = Run(script, false);
            var jit = Run(script, true);
            Assert.That(jit.result, Is.EqualTo(interp.result));
            Assert.That(jit.f.JitState, Is.EqualTo(Chunk.JitStatus.Compiled));
        }

        [Test]
        public void NonNumericSurprise_DeoptsAndMatchesInterpreter()
        {
            // Warm with doubles (-> double tier), then a string argument: the numeric
            // guard fails and we deopt to the interpreter's string concatenation.
            var script = Warm("a + b", "i + 0.5", "2.25").Replace("__result__ = r;", "")
                       + "r = f('x', 2.25);\n__result__ = r;";
            var interp = Run(script, false);
            var jit = Run(script, true);
            Assert.That(jit.result, Is.EqualTo(interp.result));
            Assert.That(jit.result, Is.EqualTo("x2.25"));
            Assert.That(jit.f.DeoptCount, Is.GreaterThanOrEqualTo(1),
                "a string arg should have deopted the double-speculative function");
        }
    }
}
