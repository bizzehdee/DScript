using DScript;
using NUnit.Framework;

namespace DScript.Test
{
    // Deep or infinite recursion must throw a catchable "Maximum call stack size
    // exceeded" error (reported to the host) rather than overflowing the native
    // .NET stack — which is uncatchable and crashes the process. Execution runs on
    // a large stack and the call-depth guard (ScriptEngine.MaxCallStackDepth) bounds
    // it well short of overflow.
    public class RecursionLimitTests
    {
        [Test]
        public void InfiniteRecursion_ThrowsCatchableError_NotProcessCrash()
        {
            var engine = new ScriptEngine();
            var ex = Assert.Throws<ScriptException>(() =>
                engine.Run(ScriptEngine.Compile("function r(n){ return 1 + r(n + 1); } r(0);")));
            Assert.That(ex.Message, Does.Contain("Maximum call stack size exceeded"));
        }

        [Test]
        public void DeepButBoundedRecursion_CompletesNormally()
        {
            // 5000 deep is comfortably under the default limit (10000) and well within
            // the large execution stack.
            var engine = new ScriptEngine();
            engine.Execute("function d(n){ return n <= 0 ? 0 : 1 + d(n - 1); } result = d(5000);");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(5000));
        }

        [Test]
        public void MaxCallStackDepth_DefaultsTo10000()
        {
            Assert.That(new ScriptEngine().MaxCallStackDepth, Is.EqualTo(10000));
        }

        [Test]
        public void MaxCallStackDepth_IsConfigurable_LowerLimitTripsSooner()
        {
            var engine = new ScriptEngine { MaxCallStackDepth = 100 };
            var ex = Assert.Throws<ScriptException>(() =>
                engine.Run(ScriptEngine.Compile("function d(n){ return n <= 0 ? 0 : 1 + d(n - 1); } d(5000);")));
            Assert.That(ex.Message, Does.Contain("Maximum call stack size exceeded"));
        }

        [Test]
        public void RecursionJustUnderConfiguredLimit_Succeeds()
        {
            var engine = new ScriptEngine { MaxCallStackDepth = 300 };
            engine.Execute("function d(n){ return n <= 0 ? 0 : 1 + d(n - 1); } result = d(250);");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(250));
        }

        [Test]
        public void TailRecursion_IsNotDepthLimited()
        {
            // Tail calls are trampolined, so they don't grow the call depth and a deep
            // tail-recursive accumulator runs past the limit without error.
            var engine = new ScriptEngine { MaxCallStackDepth = 500 };
            engine.Execute("function sum(n, a){ return n <= 0 ? a : sum(n - 1, a + 1); } result = sum(20000, 0);");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(20000));
        }
    }
}
