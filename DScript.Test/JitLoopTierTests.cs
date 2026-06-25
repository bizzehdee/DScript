using NUnit.Framework;
using DScript.Vm;
using DScript.Jit;

namespace DScript.Test
{
    [TestFixture]
    [NonParallelizable]
    public class JitLoopTierTests
    {
        [TearDown] public void Clear() => JitRegistry.Clear();

        private static (string result, Chunk f) Run(string fn, string call, IJitCompiler c)
        {
            if (c != null) JitRegistry.Register(c); else JitRegistry.Clear();
            var s = fn + "\nvar r=0; var i=0; while(i<1200){ r = " + call + "; i = i + 1; }\n__result__ = r;";
            var chunk = ScriptEngine.Compile(s);
            var engine = new ScriptEngine();
            engine.Run(chunk);
            return (engine.Root.GetParameter("__result__").String, chunk.Functions[0]);
        }

        [TestCase("function f(n){ var s=0; var i=0; while(i<n){ s=s+i; i=i+1; } return s; }", "f(i % 50)")]
        [TestCase("function f(n){ var s=0; for(var i=0;i<n;i=i+1){ s=s+i*i; } return s; }", "f(i % 30)")]
        [TestCase("function f(n){ var s=1; var i=1; while(i<=n){ s=s*i; i=i+1; } return s; }", "f(i % 10)")]
        [TestCase("function f(n){ var c=0; var a=0; while(a<n){ var b=0; while(b<n){ c=c+1; b=b+1; } a=a+1; } return c; }", "f(i % 12)")]
        [TestCase("function f(n){ var s=0; var i=0; while(i<n){ if(i<10){s=s+1;}else{s=s+2;} i=i+1; } return s; }", "f(i % 40)")]
        public void IntLoopMatches(string fn, string call)
        {
            var interp = Run(fn, call, null);
            var jit = Run(fn, call, new ReflectionEmitJitCompiler());
            Assert.That(jit.f.JitState, Is.EqualTo(Chunk.JitStatus.Compiled));
            Assert.That(jit.result, Is.EqualTo(interp.result));
        }

        [Test]
        public void DeoptOnNonIntParamProvesUnboxedTier()
        {
            // Warm with int args (unboxed loop tier compiles), then a double arg: the
            // entry param guard fails and we deopt — proving the unboxed tier (not the
            // conservative tier, which never deopts) was selected.
            var r = RunWithSurprise(new ReflectionEmitJitCompiler());
            Assert.That(r.deopt, Is.GreaterThanOrEqualTo(1), "non-int param should deopt the unboxed loop tier");
            Assert.That(r.result, Is.EqualTo(r.interp));
        }

        private static (string result, string interp, int deopt) RunWithSurprise(IJitCompiler c)
        {
            var fn = "function f(n){ var s=0; var i=0; while(i<n){ s=s+i; i=i+1; } return s; }\n";
            var src = fn + "var r=0; var i=0; while(i<1200){ r=f(i % 20); i=i+1; }\n r=f(3.5);\n__result__=r;";
            JitRegistry.Clear();
            var ie = new ScriptEngine(); ie.Run(ScriptEngine.Compile(src));
            var interp = ie.Root.GetParameter("__result__").String;
            JitRegistry.Register(c);
            var chunk = ScriptEngine.Compile(src);
            var e = new ScriptEngine(); e.Run(chunk);
            JitRegistry.Clear();
            return (e.Root.GetParameter("__result__").String, interp, chunk.Functions[0].DeoptCount);
        }
    }
}
