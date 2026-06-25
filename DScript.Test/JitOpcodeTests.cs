using NUnit.Framework;
using DScript.Vm;
using DScript.Jit;

namespace DScript.Test
{
    /// <summary>
    /// Phase 12 extended opcode coverage: indexed get/set, unary negate/bitnot/typeof,
    /// and shifts compile (conservative tier) and match the interpreter on both
    /// back-ends.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class JitOpcodeTests
    {
        [TearDown] public void Clear() => JitRegistry.Clear();

        private static string Run(string fn, string call, IJitCompiler c)
        {
            if (c != null) JitRegistry.Register(c); else JitRegistry.Clear();
            var s = fn + "\nvar r=0; var i=0; while(i<1200){ r = " + call + "; i = i + 1; }\n__result__ = r;";
            var chunk = ScriptEngine.Compile(s);
            var engine = new ScriptEngine();
            engine.Run(chunk);
            return engine.Root.GetParameter("__result__").String;
        }

        private void BothBackendsMatch(string fn, string call)
        {
            var interp = Run(fn, call, null);
            Assert.That(Run(fn, call, new ReflectionEmitJitCompiler()), Is.EqualTo(interp), "reflemit");
            Assert.That(Run(fn, call, new ClosureThreadedJitCompiler()), Is.EqualTo(interp), "closure");
        }

        [TestCase("function f(a, k){ return a[k]; }", "f([10,20,30], i % 3)")]
        [TestCase("function f(a, k){ return a[k] + a[0]; }", "f([1,2,3,4], i % 4)")]
        [TestCase("function f(x){ return -x; }", "f(i - 600)")]
        [TestCase("function f(x){ return -x; }", "f(i + 0.5)")]   // negate double
        [TestCase("function f(x){ return ~x; }", "f(i)")]
        [TestCase("function f(x){ return typeof x; }", "f(i)")]
        [TestCase("function f(x){ return typeof x; }", "f('s')")]
        [TestCase("function f(x){ return x << 2; }", "f(i % 16)")]
        [TestCase("function f(x){ return x >> 1; }", "f(i)")]
        [TestCase("function f(x){ return x >>> 1; }", "f(0 - i)")]
        public void ExtendedOpcodesMatch(string fn, string call)
        {
            BothBackendsMatch(fn, call);
        }

        [Test]
        public void IndexedWrite()
        {
            // a is a fresh array per call (literal in the caller); f reads/writes a[0].
            BothBackendsMatch("function f(a){ a[0] = a[0] + 1; return a[0]; }", "f([5])");
        }

        [Test]
        public void IndexedWriteWithComputedKey()
        {
            BothBackendsMatch("function f(a, k){ a[k] = a[k] * 2; return a[k]; }", "f([10,20,30], i % 3)");
        }
    }
}
