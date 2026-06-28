using DScript.Extras;
using DScript.Jit;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    /// <summary>
    /// JIT property inline-cache cases (Lever 2a/2b) that need DScript.Extras built-ins
    /// (e.g. <c>Object.freeze</c>), which the bare-engine <see cref="JitCodeGenTestsBase"/>
    /// harness does not register. Each case runs a full Extras-enabled engine with a hot
    /// loop so the function tiers up through the JIT back-end under test.
    /// NonParallelizable: JitRegistry is process-global.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class JitPropertyCacheExtrasTests
    {
        [TearDown] public void Clear() => JitRegistry.Clear();

        private static string Run(string code, IJitCompiler c)
        {
            JitRegistry.Clear();
            if (c != null) JitRegistry.Register(c);
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__").String;
        }

        // Writing a frozen (non-writable) property must be ignored in non-strict code:
        // the SetProp fast path's Writable guard must route the write to the full
        // SetMember path, never overwriting the slot in place.
        private const string FrozenWrite =
            "var obj = Object.freeze({ a: 5 });\n" +
            "function f(o){ o.a = 99; return o.a; }\n" +
            "var r = 0; var i = 0; while (i < 50000) { r = f(obj); i = i + 1; }\n" +
            "__result__ = r;";

        [Test]
        public void FrozenPropertyWriteIgnored_Interpreter()
            => Assert.That(Run(FrozenWrite, null), Is.EqualTo("5"));

        [Test]
        public void FrozenPropertyWriteIgnored_ReflectionEmit()
            => Assert.That(Run(FrozenWrite, new ReflectionEmitJitCompiler()), Is.EqualTo("5"));

        [Test]
        public void FrozenPropertyWriteIgnored_Closure()
            => Assert.That(Run(FrozenWrite, new ClosureThreadedJitCompiler()), Is.EqualTo("5"));
    }
}
