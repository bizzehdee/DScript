using DScript.Jit;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    /// <summary>
    /// The closure long-register loop tier under positional local slots
    /// (EnableLocalSlots, as used by NativeAOT builds). The main test suite runs with
    /// slots off, so these exercise the slot path: GetLocal/SetLocal registers loaded
    /// and guarded from the frame at the OSR resume, int-leaf call inlining with slot
    /// parameters, comparison branches, and the callee-identity / non-integer fallbacks.
    /// Each result must match the interpreter. NonParallelizable: JitRegistry and the
    /// EnableLocalSlots flag are process-global.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class JitLongLoopSlotTests
    {
        [SetUp]    public void On()  => ScriptEngine.EnableLocalSlots = true;
        [TearDown] public void Off() { JitRegistry.Clear(); ScriptEngine.EnableLocalSlots = false; }

        private static string Interp(string s)
        {
            JitRegistry.Clear();
            var e = new ScriptEngine();
            e.Run(ScriptEngine.Compile(s));
            return e.Root.GetParameter("__result__").GetParsableString();
        }

        private static string Closure(string s)
        {
            JitRegistry.Clear();
            JitRegistry.Register(new ClosureThreadedJitCompiler());
            var e = new ScriptEngine();
            e.Run(ScriptEngine.Compile(s));
            return e.Root.GetParameter("__result__").GetParsableString();
        }

        private static void Matches(string s) => Assert.That(Closure(s), Is.EqualTo(Interp(s)));

        [Test]
        public void SingleBigLoop()
            => Matches("function big(n){ var s=0; var i=0; while(i<n){ s=s+i; i=i+1; } return s; }\n" +
                       "__result__ = big(2000000);");

        [Test]
        public void IntLeafCallInlinedInLoop()
            => Matches("function f(a,b,c){ return a+b+c; }\n" +
                       "function bench(n){ var s=0; var i=0; while(i<n){ s = s + f(i,1,2); i=i+1; } return s; }\n" +
                       "__result__ = bench(2000000);");

        [Test]
        public void LoopWithComparisonBranch()
            => Matches("function f(n){ var s=0; var i=0; while(i<n){ if(i<1000){ s=s+1; } else { s=s+2; } i=i+1; } return s; }\n" +
                       "__result__ = f(2000000);");

        [Test]
        public void MultiplyAccumulateBeyondInt32()
            => Matches("function f(n){ var s=0; var i=0; while(i<n){ s = s + i*i; i=i+1; } return s; }\n" +
                       "__result__ = f(200000);");

        [Test]
        public void NonIntegerArgFallsBack()
            // Loop bound is a non-integer: the entry guard must fail and the boxed path run.
            => Matches("function f(n){ var s=0; var i=0; while(i<n){ s=s+i; i=i+1; } return s; }\n" +
                       "__result__ = f(2000000.5);");

        [Test]
        public void CalleeReassignedBetweenCallsFallsBack()
            => Matches("function a(x){ return x+1; }\nfunction b(x){ return x+1000; }\nvar fn=a;\n" +
                       "function g(n){ var s=0; var i=0; while(i<n){ s = s + fn(i); i=i+1; } return s; }\n" +
                       "var r=0; var c=0; while(c<3000){ if(c==2000){ fn=b; } r=g(50); c=c+1; }\n__result__ = r;");
    }
}
