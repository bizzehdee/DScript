using NUnit.Framework;
using DScript.Vm;
using DScript.Jit;

namespace DScript.Test
{
    /// <summary>
    /// Phase 3: object &amp; array literals (NewObject/NewArray/InitProp/InitElem).
    /// These are straight-line, so BOTH back-ends compile them and match the
    /// interpreter — including nested literals and returning a constructed object.
    /// Computed keys, spread, and getters/setters remain declined.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class JitLiteralTests
    {
        [TearDown] public void Clear() => JitRegistry.Clear();

        private static (string result, Chunk f) Run(string src, IJitCompiler c)
        {
            if (c != null) JitRegistry.Register(c); else JitRegistry.Clear();
            var chunk = ScriptEngine.Compile(src);
            var engine = new ScriptEngine();
            engine.Run(chunk);
            Chunk f = null;
            foreach (var fn in chunk.Functions) if (fn.Name == "f") { f = fn; break; }
            return (engine.Root.GetParameter("__result__").String, f);
        }

        private static void Matches(string src, bool expectCompiled = true)
        {
            var interp = Run(src, null);
            foreach (var c in new IJitCompiler[] { new ReflectionEmitJitCompiler(), new ClosureThreadedJitCompiler() })
            {
                var jit = Run(src, c);
                Assert.That(jit.result, Is.EqualTo(interp.result), c.GetType().Name);
                if (expectCompiled && jit.f != null)
                    Assert.That(jit.f.JitState, Is.EqualTo(Chunk.JitStatus.Compiled), c.GetType().Name);
            }
        }

        [Test]
        public void ObjectLiteral()
        {
            Matches(
                "function f(x){ var o = { a: x, b: x + 1 }; return o.a + o.b; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(i % 50); i = i + 1; }\n__result__ = r;");
        }

        [Test]
        public void ArrayLiteral()
        {
            Matches(
                "function f(x){ var a = [x, x * 2, x * 3]; return a[0] + a[1] + a[2]; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(i % 50); i = i + 1; }\n__result__ = r;");
        }

        [Test]
        public void NestedLiteral()
        {
            Matches(
                "function f(x){ var o = { p: { q: x }, arr: [x, x + 1] }; return o.p.q + o.arr[1]; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(i % 50); i = i + 1; }\n__result__ = r;");
        }

        [Test]
        public void ReturnsBuiltObject()
        {
            Matches(
                "function f(x){ return { a: x, b: x * 10 }; }\n" +
                "var o; var i=0; while(i<1200){ o = f(7); i = i + 1; }\n__result__ = o.a + o.b;");
        }

        [Test]
        public void EmptyLiterals()
        {
            Matches(
                "function f(){ var o = {}; var a = []; return a.length; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(); i = i + 1; }\n__result__ = r;");
        }

        [Test]
        public void ArrayOfObjects()
        {
            Matches(
                "function f(x){ var a = [{ v: x }, { v: x + 1 }]; return a[0].v + a[1].v; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(i % 50); i = i + 1; }\n__result__ = r;");
        }
    }
}
