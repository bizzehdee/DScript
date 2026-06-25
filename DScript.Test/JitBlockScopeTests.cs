using NUnit.Framework;
using DScript.Vm;
using DScript.Jit;

namespace DScript.Test
{
    /// <summary>
    /// Phase 4: let/const block scopes (EnterBlock/LeaveBlock) compile on the
    /// Reflection.Emit conservative tier and match the interpreter. The compiler
    /// tracks a "current environment" IL local that every variable op resolves
    /// against; EnterBlock pushes a child env, LeaveBlock restores the parent.
    /// The speculative int/double/int-loop tiers and the closure back-end all
    /// decline blocks (they fall through to the conservative tier).
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class JitBlockScopeTests
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
            var jit = Run(src, new ReflectionEmitJitCompiler());
            Assert.That(jit.result, Is.EqualTo(interp.result));
            if (expectCompiled && jit.f != null)
                Assert.That(jit.f.JitState, Is.EqualTo(Chunk.JitStatus.Compiled));
        }

        [Test]
        public void LetInsideLoopBody()
        {
            Matches(
                "function f(n){ var s = 0; var i = 0; while(i < n){ let step = i * 2; s = s + step; i = i + 1; } return s; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(10); i = i + 1; }\n__result__ = r;");
        }

        [Test]
        public void BareBlockWithLet()
        {
            Matches(
                "function f(x){ let r = x; { let r = x + 100; } return r; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(7); i = i + 1; }\n__result__ = r;");
        }

        [Test]
        public void ConstInBlock()
        {
            Matches(
                "function f(x){ let total = 0; { const k = 5; total = x * k; } return total; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(4); i = i + 1; }\n__result__ = r;");
        }

        [Test]
        public void ShadowingAcrossBlocks()
        {
            Matches(
                "function f(x){ let v = x; { let v = x + 1; { let v = x + 2; } } return v; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(3); i = i + 1; }\n__result__ = r;");
        }

        [Test]
        public void LetInIfBranch()
        {
            Matches(
                "function f(x){ let out = 0; if(x > 0){ let d = x * 3; out = d; } else { let d = x - 1; out = d; } return out; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(5); i = i + 1; }\n__result__ = r;");
        }

        [Test]
        public void NestedBlocksInLoop()
        {
            Matches(
                "function f(n){ var s = 0; var i = 0; while(i < n){ { let a = i; { let b = a + 1; s = s + b; } } i = i + 1; } return s; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(8); i = i + 1; }\n__result__ = r;");
        }

        [Test]
        public void ClosureBackendDeclinesBlocks()
        {
            var src =
                "function f(x){ let r = x; { let r = x + 100; } return r; }\n" +
                "var r=0; var i=0; while(i<1200){ r = f(7); i = i + 1; }\n__result__ = r;";
            var jit = Run(src, new ClosureThreadedJitCompiler());
            Assert.That(jit.f.JitState, Is.EqualTo(Chunk.JitStatus.Failed), "closure declines block scopes");
            Assert.That(jit.result, Is.EqualTo(Run(src, null).result));
        }
    }
}
