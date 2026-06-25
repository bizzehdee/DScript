using DScript;
using NUnit.Framework;

namespace DScript.Test
{
    // ScriptEngine.EnableOptimizer toggles the bytecode optimiser for the engine's
    // own compile paths (Execute / EvalComplex). Results must be identical whether
    // the optimiser runs or not; only the emitted bytecode differs.
    public class EngineOptimizerToggleTests
    {
        [Test]
        public void EnableOptimizer_DefaultsTrue()
        {
            Assert.That(new ScriptEngine().EnableOptimizer, Is.True);
        }

        [Test]
        public void Execute_SameResult_OptimizerOnOrOff()
        {
            const string code =
                "function f(n){ var s = 0; for (var i = 0; i < n; i = i + 1) { s = s + i * 2 - 1; } return s; }" +
                "result = f(50) + (2 + 3 * 4) + (true ? 7 : 9);";

            var on = new ScriptEngine { EnableOptimizer = true };
            on.Execute(code);

            var off = new ScriptEngine { EnableOptimizer = false };
            off.Execute(code);

            Assert.That(off.Root.GetParameter("result").Int,
                Is.EqualTo(on.Root.GetParameter("result").Int));
        }

        [Test]
        public void EvalComplex_SameResult_OptimizerOnOrOff()
        {
            const string expr = "(2 + 3 * 4) + (10 - 2 - 3) + (1 << 4)";

            var on = new ScriptEngine { EnableOptimizer = true };
            var off = new ScriptEngine { EnableOptimizer = false };

            Assert.That(off.EvalComplex(expr).Var.Int,
                Is.EqualTo(on.EvalComplex(expr).Var.Int));
        }

        [Test]
        public void Execute_OptimizerOff_StillRunsCorrectly()
        {
            var engine = new ScriptEngine { EnableOptimizer = false };
            engine.Execute("var a = [1, 2, 3]; result = a[0] + a[1] + a[2];");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(6));
        }
    }
}
