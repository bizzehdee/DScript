using NUnit.Framework;
using DScript.Vm;
using DScript.Jit;

namespace DScript.Test
{
    /// <summary>
    /// Phase 1: short-circuit operators (&amp;&amp;, ||, ??, ?.) and the ternary ?:
    /// compile (conservative tier, Reflection.Emit) and match the interpreter. These
    /// lower to conditional-pop jumps with branch/fall-through stack-effect differences.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class JitShortCircuitTests
    {
        [TearDown] public void Clear() => JitRegistry.Clear();

        private static (string result, Chunk.JitStatus st) Run(string fn, string call, IJitCompiler c)
        {
            if (c != null) JitRegistry.Register(c); else JitRegistry.Clear();
            var s = fn + "\nvar r=0; var i=0; while(i<1200){ r = " + call + "; i = i + 1; }\n__result__ = r;";
            var chunk = ScriptEngine.Compile(s);
            var engine = new ScriptEngine();
            engine.Run(chunk);
            return (engine.Root.GetParameter("__result__").String, chunk.Functions[0].JitState);
        }

        [TestCase("function f(x){ return x < 0 ? 0 - x : x; }", "f(i - 600)")]                  // ternary
        [TestCase("function f(n){ return n < 10 ? 'lo' : n < 100 ? 'mid' : 'hi'; }", "f(i % 150)")] // nested ternary
        [TestCase("function f(a,b){ return a > 0 && b > 0; }", "f(i - 600, 5)")]                // &&
        [TestCase("function f(a,b){ return a > 0 || b > 0; }", "f(i - 600, 0 - 5)")]            // ||
        [TestCase("function f(a,b){ return (a > 0 && b > 0) ? 1 : 0; }", "f(i % 3 - 1, 2)")]    // && in condition
        public void ShortCircuitMatches(string fn, string call)
        {
            var interp = Run(fn, call, null);
            var jit = Run(fn, call, new ReflectionEmitJitCompiler());
            Assert.That(jit.st, Is.EqualTo(Chunk.JitStatus.Compiled), "should compile");
            Assert.That(jit.result, Is.EqualTo(interp.result));
        }

        [Test]
        public void NullishCoalescing()
        {
            var fn = "function f(a, b){ return a ?? b; }";
            // a is sometimes null, sometimes a value.
            var interp = Run(fn, "f(i % 2 == 0 ? null : i, 99)", null);
            var jit = Run(fn, "f(i % 2 == 0 ? null : i, 99)", new ReflectionEmitJitCompiler());
            Assert.That(jit.result, Is.EqualTo(interp.result));
        }

        [Test]
        public void OptionalChaining()
        {
            var fn = "function f(o){ return o?.x; }";
            var interp = Run(fn, "f(i % 2 == 0 ? null : { x: i })", null);
            var jit = Run(fn, "f(i % 2 == 0 ? null : { x: i })", new ReflectionEmitJitCompiler());
            Assert.That(jit.result, Is.EqualTo(interp.result));
        }
    }
}
