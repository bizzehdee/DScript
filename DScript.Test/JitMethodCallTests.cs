using NUnit.Framework;
using DScript.Vm;
using DScript.Jit;

namespace DScript.Test
{
    /// <summary>
    /// Phase 2: method calls (GetPropMethod/GetPropCall0/CallMethod) compile on the
    /// Reflection.Emit conservative tier and match the interpreter — including the
    /// receiver/`this` binding, zero-arg fast path, prototype (class) methods, and
    /// chaining. The closure back-end also compiles method calls (a pending-method stack
    /// evaluates the peeked receiver exactly once), so both back-ends have parity.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class JitMethodCallTests
    {
        [TearDown] public void Clear() => JitRegistry.Clear();

        private static (string result, Chunk f) Run(string src, IJitCompiler c)
        {
            if (c != null) JitRegistry.Register(c); else JitRegistry.Clear();
            var chunk = ScriptEngine.Compile(src);
            var engine = new ScriptEngine();
            engine.Run(chunk);
            // f is the last top-level function declared in each script.
            Chunk f = null;
            foreach (var fn in chunk.Functions) if (fn.Name == "f") { f = fn; break; }
            return (engine.Root.GetParameter("__result__").String, f);
        }

        private static void Matches(string src, bool expectCompiled = true)
        {
            var interp = Run(src, null);
            var jit = Run(src, new ReflectionEmitJitCompiler());
            Assert.That(jit.result, Is.EqualTo(interp.result));
            if (expectCompiled && jit.f != null)
                Assert.That(jit.f.JitState, Is.EqualTo(Chunk.JitStatus.Compiled));
        }

        [Test]
        public void MethodCallWithArgsAndThis()
        {
            Matches(
                "var o = { x: 10, add: function(a, b){ return this.x + a + b; } };\n" +
                "function f(o){ return o.add(1, 2) + 0; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(o); i = i + 1; }\n__result__ = r;");
        }

        [Test]
        public void ZeroArgMethodCall()
        {
            Matches(
                "var o = { v: 5, get: function(){ return this.v * 2; } };\n" +
                "function f(o){ return o.get() + 0; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(o); i = i + 1; }\n__result__ = r;");
        }

        [Test]
        public void ClassInstanceMethod()
        {
            // Method resolved through the prototype chain (class instance).
            Matches(
                "class C { constructor(n){ this.n = n; } twice(){ return this.n * 2; } }\n" +
                "function f(c){ return c.twice() + 0; }\n" +
                "var obj = new C(7);\n" +
                "var r=0; var i=0; while(i<1200){ r = f(obj); i = i + 1; }\n__result__ = r;");
        }

        [Test]
        public void ChainedMethodCalls()
        {
            Matches(
                "var o = { n: 3, step: function(){ return { n: this.n + 1, val: function(){ return this.n; } }; } };\n" +
                "function f(o){ return o.step().val() + 0; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(o); i = i + 1; }\n__result__ = r;");
        }

        [Test]
        public void MethodCallWithComputedArgs()
        {
            Matches(
                "var o = { base: 100, calc: function(a, b){ return this.base + a * b; } };\n" +
                "function f(o, i){ return o.calc(i, i + 1) + 0; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(o, i % 20); i = i + 1; }\n__result__ = r;");
        }

        [Test]
        public void ClosureBackendCompilesMethodCalls()
        {
            // The closure back-end now compiles method calls (it used to decline them),
            // matching the interpreter and the Reflection.Emit back-end.
            var src =
                "var o = { v: 5, get: function(){ return this.v; } };\n" +
                "function f(o){ return o.get() + 0; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(o); i = i + 1; }\n__result__ = r;";
            var jit = Run(src, new ClosureThreadedJitCompiler());
            Assert.That(jit.f.JitState, Is.EqualTo(Chunk.JitStatus.Compiled), "closure now compiles method calls");
            Assert.That(jit.result, Is.EqualTo(Run(src, null).result));
        }
    }
}
