using NUnit.Framework;
using DScript.Vm;
using DScript.Jit;

namespace DScript.Test
{
    /// <summary>
    /// Inline-cache promotion for JIT-compiled property reads: a warmed site must
    /// return the correct value, and a shape change (added property) or value change
    /// must invalidate/track correctly — verified against the interpreter on both
    /// back-ends.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class JitIcPromotionTests
    {
        [TearDown] public void Clear() => JitRegistry.Clear();

        private static string Run(string script, IJitCompiler c)
        {
            if (c != null) JitRegistry.Register(c); else JitRegistry.Clear();
            var chunk = ScriptEngine.Compile(script);
            var engine = new ScriptEngine();
            engine.Run(chunk);
            return engine.Root.GetParameter("__result__").String;
        }

        private void AssertBothBackendsMatchInterpreter(string script)
        {
            var interp = Run(script, null);
            Assert.That(Run(script, new ReflectionEmitJitCompiler()), Is.EqualTo(interp), "reflection backend");
            Assert.That(Run(script, new ClosureThreadedJitCompiler()), Is.EqualTo(interp), "closure backend");
        }

        [Test]
        public void WarmIcReturnsCorrectValue()
        {
            AssertBothBackendsMatchInterpreter(
                "function get(o){ return o.x; }\n" +
                "var o={}; o.x=7;\n" +
                "var r=0; var i=0; while(i<1200){ r = get(o); i = i + 1; }\n__result__ = r;");
        }

        [Test]
        public void ShapeChangeInvalidatesIc()
        {
            // Warm the cache on o, then add a new property (bumps ShapeVersion); the
            // next read of o.x must re-resolve and still be correct.
            AssertBothBackendsMatchInterpreter(
                "function get(o){ return o.x; }\n" +
                "var o={}; o.x=1;\n" +
                "var r=0; var i=0; while(i<1200){ r = get(o); i = i + 1; }\n" +
                "o.y = 99;\n" +
                "r = get(o);\n__result__ = r;");
        }

        [Test]
        public void ValueChangeTrackedThroughIc()
        {
            // Changing an existing property's value (no shape change) must be reflected.
            AssertBothBackendsMatchInterpreter(
                "function get(o){ return o.x; }\n" +
                "var o={}; o.x=1;\n" +
                "var r=0; var i=0; while(i<1200){ r = get(o); i = i + 1; }\n" +
                "o.x = 42;\n" +
                "r = get(o);\n__result__ = r;");
        }

        [Test]
        public void DifferentObjectsSameSite()
        {
            // A site read against two different objects must return each one's value
            // (cache thrashes but stays correct).
            AssertBothBackendsMatchInterpreter(
                "function get(o){ return o.x; }\n" +
                "var a={}; a.x=10; var b={}; b.x=20;\n" +
                "var r=0; var i=0; while(i<1200){ r = get(a) + get(b); i = i + 1; }\n__result__ = r;");
        }

        [Test]
        public void GetterProperty()
        {
            // A property backed by a getter must invoke it through the cache.
            AssertBothBackendsMatchInterpreter(
                "var o = { get x() { return 5; } };\n" +
                "function get(p){ return p.x; }\n" +
                "var r=0; var i=0; while(i<1200){ r = get(o); i = i + 1; }\n__result__ = r;");
        }
    }
}
