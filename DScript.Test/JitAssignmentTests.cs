using NUnit.Framework;
using DScript.Vm;
using DScript.Jit;

namespace DScript.Test
{
    /// <summary>
    /// Local variables, assignments, and the loops they enable. Loop cases use the
    /// Reflection.Emit tier (the closure back-end declines control flow); straight-line
    /// assignment is checked on both back-ends. All compared against the interpreter.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class JitAssignmentTests
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

        private void ReflEmitMatches(string fn, string call)
        {
            var interp = Run(fn, call, null);
            var jit = Run(fn, call, new ReflectionEmitJitCompiler());
            Assert.That(jit.st, Is.EqualTo(Chunk.JitStatus.Compiled), "should compile");
            Assert.That(jit.result, Is.EqualTo(interp.result));
        }

        [TestCase("function f(n){ var s = 0; var i = 0; while (i < n) { s = s + i; i = i + 1; } return s; }", "f(i % 50)")]
        [TestCase("function f(n){ var s = 0; for (var i = 0; i < n; i = i + 1) { s = s + i * i; } return s; }", "f(i % 30)")]
        [TestCase("function f(n){ var c = 0; var a = 0; while (a < n) { var b = 0; while (b < n) { c = c + 1; b = b + 1; } a = a + 1; } return c; }", "f(i % 12)")]
        [TestCase("function f(n){ var s = 0; var i = 0; while (i < n) { s += i; i++; } return s; }", "f(i % 40)")]
        [TestCase("function f(n){ var s = 1; var i = 1; while (i <= n) { s = s * i; i = i + 1; } return s; }", "f(i % 10)")]
        public void LoopsAndCompoundAssignment(string fn, string call)
        {
            ReflEmitMatches(fn, call);
        }

        [Test]
        public void PropertyMutationInLoop()
        {
            // The object is a parameter (its literal lives in the caller); f only
            // reads/writes o.c, so it compiles. An object literal *inside* f would use
            // NewObject/InitProp, which are not yet supported and would decline.
            ReflEmitMatches(
                "function f(o, n){ var i = 0; while (i < n) { o.c = o.c + i; i = i + 1; } return o.c; }",
                "f({ c: 0 }, i % 25)");
        }

        // Straight-line assignment (no control flow) compiles on BOTH back-ends.
        [TestCase("function f(){ var a = 1; a = a + 4; return a; }", "f()")]
        [TestCase("function f(o){ o.x = o.x + 1; return o.x; }", "f({ x: 0 })")]
        public void StraightLineAssignment(string fn, string call)
        {
            var interp = Run(fn, call, null);
            Assert.That(Run(fn, call, new ReflectionEmitJitCompiler()).result, Is.EqualTo(interp.result), "reflemit");
            Assert.That(Run(fn, call, new ClosureThreadedJitCompiler()).result, Is.EqualTo(interp.result), "closure");
        }

        [Test]
        public void BlockScopedLetIsDeclined()
        {
            // `let`/`const` introduce block scopes (EnterBlock/LeaveBlock), which are
            // not supported — the function is declined but still runs (interpreted)
            // with the correct result.
            var fn = "function f(n){ let s = 0; let i = 0; while (i < n) { let step = 2; s = s + step; i = i + 1; } return s; }";
            var interp = Run(fn, "f(i % 20)", null);
            var jit = Run(fn, "f(i % 20)", new ReflectionEmitJitCompiler());
            Assert.That(jit.st, Is.EqualTo(Chunk.JitStatus.Failed), "block scopes are unsupported");
            Assert.That(jit.result, Is.EqualTo(interp.result));
        }
    }
}
