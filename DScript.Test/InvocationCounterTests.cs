using NUnit.Framework;
using DScript.Vm;

namespace DScript.Test
{
    [TestFixture]
    public class InvocationCounterTests
    {
        private static (ScriptVar result, Chunk chunk) CompileAndRun(string code)
        {
            var engine = new ScriptEngine();
            var chunk  = ScriptEngine.Compile(code);
            engine.Run(chunk);
            return (engine.Root.GetParameter("__result__"), chunk);
        }

        // ── InvocationCount ──────────────────────────────────────────────────

        [Test]
        public void InvocationCount_TopLevelChunkCountedOnce()
        {
            var (_, chunk) = CompileAndRun("__result__ = 1;");
            Assert.That(chunk.InvocationCount, Is.EqualTo(1),
                "top-level chunk is entered exactly once per engine.Run");
        }

        [Test]
        public void InvocationCount_FunctionCalledNTimes()
        {
            const string script = @"
function work() { return 1; }
work(); work(); work(); work(); work();
__result__ = 42;
";
            var (_, chunk) = CompileAndRun(script);

            Assert.That(chunk.Functions.Count, Is.GreaterThan(0));
            var workChunk = chunk.Functions[0];
            Assert.That(workChunk.InvocationCount, Is.EqualTo(5),
                "work() called 5 times → InvocationCount == 5");
        }

        [Test]
        public void InvocationCount_UnCalledFunction_IsZero()
        {
            var chunk = ScriptEngine.Compile("function noop() {} __result__ = 1;");
            var engine = new ScriptEngine();
            engine.Run(chunk);

            Assert.That(chunk.Functions.Count, Is.GreaterThan(0));
            Assert.That(chunk.Functions[0].InvocationCount, Is.EqualTo(0),
                "a never-called function has InvocationCount == 0");
        }

        [Test]
        public void InvocationCount_RecursiveFunction_CountsEachEntry()
        {
            // countdown(5) calls itself 5 more times — 6 total entries.
            const string script = @"
function countdown(n) { if (n > 0) countdown(n - 1); }
countdown(5);
__result__ = 42;
";
            var (_, chunk) = CompileAndRun(script);
            var countdownChunk = chunk.Functions[0];
            Assert.That(countdownChunk.InvocationCount, Is.EqualTo(6),
                "countdown(5) → 6 recursive entries total");
        }

        // ── BackEdgeCount ────────────────────────────────────────────────────

        [Test]
        public void BackEdgeCount_WhileLoop_CountsIterations()
        {
            const string script = @"
var i = 0;
while (i < 5) { i = i + 1; }
__result__ = i;
";
            var (result, chunk) = CompileAndRun(script);
            Assert.That(result.Int, Is.EqualTo(5));
            // The while loop takes the backward jump 5 times (once per iteration).
            Assert.That(chunk.BackEdgeCount, Is.EqualTo(5),
                "5-iteration while loop → 5 back-edges");
        }

        [Test]
        public void BackEdgeCount_NestedLoops_Cumulative()
        {
            const string script = @"
var count = 0;
var i = 0;
while (i < 3) {
    var j = 0;
    while (j < 4) { count = count + 1; j = j + 1; }
    i = i + 1;
}
__result__ = count;
";
            var (result, chunk) = CompileAndRun(script);
            Assert.That(result.Int, Is.EqualTo(12));
            // Outer loop: 3 back-edges. Inner loop: 4 back-edges × 3 outer iterations = 12.
            // Total = 15.
            Assert.That(chunk.BackEdgeCount, Is.EqualTo(15));
        }

        [Test]
        public void BackEdgeCount_NoLoop_IsZero()
        {
            var (_, chunk) = CompileAndRun("var x = 1; var y = 2; __result__ = x + y;");
            Assert.That(chunk.BackEdgeCount, Is.EqualTo(0),
                "straight-line code has no back-edges");
        }

        [Test]
        public void BackEdgeCount_UnexecutedChunk_IsZero()
        {
            var chunk = ScriptEngine.Compile("function looper() { var i=0; while(i<10){i=i+1;} }");
            Assert.That(chunk.Functions[0].BackEdgeCount, Is.EqualTo(0),
                "never-executed function has no back-edges");
        }
    }
}
