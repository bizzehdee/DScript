using DScript.Jit;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    /// <summary>
    /// Lever 2d: monomorphic method calls (<c>obj.m(args)</c>) compiled by the
    /// Reflection.Emit conservative tier should guard on the resolved method's identity
    /// and splice its body inline with <c>this</c> = receiver. The closure back-end
    /// declines method calls outright (no receiver threading), so this is RE-only. These
    /// tests assert the JIT result matches the interpreter and that the calling function
    /// tiers up to Compiled.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class JitMethodInlineTests
    {
        [TearDown] public void Clear() => JitRegistry.Clear();

        private static string RunInterp(string s)
        {
            JitRegistry.Clear();
            var e = new ScriptEngine();
            e.Run(ScriptEngine.Compile(s));
            return e.Root.GetParameter("__result__").GetParsableString();
        }

        private static (string result, Chunk.JitStatus state) RunJit(string s, string fn)
        {
            JitRegistry.Clear();
            JitRegistry.Register(new ReflectionEmitJitCompiler());
            var chunk = ScriptEngine.Compile(s);
            var e = new ScriptEngine();
            e.Run(chunk);
            return (e.Root.GetParameter("__result__").GetParsableString(), FindState(chunk, fn));
        }

        private static Chunk.JitStatus FindState(Chunk chunk, string fn)
        {
            foreach (var f in chunk.Functions)
            {
                if (f.Name == fn) return f.JitState;
                var nested = FindState(f, fn);
                if (nested != Chunk.JitStatus.Cold) return nested;
            }
            return Chunk.JitStatus.Cold;
        }

        private void AssertMatches(string src, string fn)
        {
            var interp = RunInterp(src);
            var jit = RunJit(src, fn);
            Assert.That(jit.result, Is.EqualTo(interp), "JIT result must match interpreter");
            Assert.That(jit.state, Is.EqualTo(Chunk.JitStatus.Compiled), $"{fn} should be compiled");
        }

        [Test]
        public void MonomorphicMethodReadingThisField()
        {
            AssertMatches(
                "class P { constructor(x){ this.x = x; } twice(){ return this.x + this.x; } }\n" +
                "var p = new P(5);\n" +
                "function f(o){ return o.twice(); }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(p); i = i + 1; }\n__result__ = r;",
                "f");
        }

        [Test]
        public void MonomorphicMethodWithParameters()
        {
            AssertMatches(
                "class P { constructor(x){ this.x = x; } addBoth(a, b){ return this.x + a + b; } }\n" +
                "var p = new P(10);\n" +
                "function f(o){ return o.addBoth(3, 4); }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(p); i = i + 1; }\n__result__ = r;",
                "f");
        }

        [Test]
        public void MethodAcrossDistinctInstancesSharesGuard()
        {
            // Two instances of the same class share the prototype method, so the identity
            // guard hits for both — the inline path must stay correct as the receiver
            // alternates between instances with different field values.
            AssertMatches(
                "class P { constructor(x){ this.x = x; } get(){ return this.x; } }\n" +
                "var a = new P(2); var b = new P(9);\n" +
                "function f(o){ return o.get(); }\n" +
                "var r=0; var i=0; while(i<1200){ r = r + f((i % 2 === 0) ? a : b); i = i + 1; }\n__result__ = r;",
                "f");
        }

        [Test]
        public void PolymorphicMethodUsesGeneralDispatch()
        {
            // Two different method functions at one site -> not monomorphic -> the general
            // array + InvokeCallable path; result must still match the interpreter.
            AssertMatches(
                "class A { m(){ return 1; } }\n" +
                "class B { m(){ return 2; } }\n" +
                "var a = new A(); var b = new B();\n" +
                "function f(o){ return o.m(); }\n" +
                "var r=0; var i=0; while(i<1500){ r = r + f((i % 2 === 0) ? a : b); i = i + 1; }\n__result__ = r;",
                "f");
        }
    }
}
