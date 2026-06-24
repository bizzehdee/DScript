using NUnit.Framework;
using DScript.Vm;
using System.Collections.Generic;

namespace DScript.Test
{
    [TestFixture]
    public class BinaryOpProfileTests
    {
        private static (ScriptVar result, Chunk chunk) CompileAndRun(string code)
        {
            var engine = new ScriptEngine();
            var chunk  = ScriptEngine.Compile(code);
            engine.Run(chunk);
            return (engine.Root.GetParameter("__result__"), chunk);
        }

        private static List<(int SiteOffset, Chunk.BinaryOpProfile Profile)> AllBinaryProfiles(Chunk chunk)
        {
            var result = new List<(int, Chunk.BinaryOpProfile)>();
            Collect(chunk, result);
            return result;

            static void Collect(Chunk c, List<(int, Chunk.BinaryOpProfile)> out_)
            {
                foreach (var p in c.GetBinaryOpProfiles()) out_.Add(p);
                foreach (var fn in c.Functions) Collect(fn, out_);
            }
        }

        // ── Int-only operations ──────────────────────────────────────────────

        [Test]
        public void BinaryOp_IntPlusInt_RecordsIntOnBothSides()
        {
            // Use variables — literal `3 + 4` is constant-folded to 7 at compile time.
            var (result, chunk) = CompileAndRun("var a = 3; var b = 4; __result__ = a + b;");
            Assert.That(result.Int, Is.EqualTo(7));

            var profiles = AllBinaryProfiles(chunk);
            Assert.That(profiles.Count, Is.GreaterThan(0), "at least one binary site");
            var anyIntInt = false;
            foreach (var (_, p) in profiles)
                if (p.LeftTypes.HasFlag(Chunk.BinaryTypeFlags.Int) &&
                    p.RightTypes.HasFlag(Chunk.BinaryTypeFlags.Int))
                    anyIntInt = true;
            Assert.That(anyIntInt, Is.True, "3 + 4 → both sides Int");
        }

        [Test]
        public void BinaryOp_IntMinusInt_RecordsInt()
        {
            var (result, chunk) = CompileAndRun("var a = 10; var b = 3; __result__ = a - b;");
            Assert.That(result.Int, Is.EqualTo(7));

            var profiles = AllBinaryProfiles(chunk);
            var anyIntInt = false;
            foreach (var (_, p) in profiles)
                if (p.LeftTypes == Chunk.BinaryTypeFlags.Int &&
                    p.RightTypes == Chunk.BinaryTypeFlags.Int)
                    anyIntInt = true;
            Assert.That(anyIntInt, Is.True, "a - b (both int) → both sides exactly Int");
        }

        // ── String operations ────────────────────────────────────────────────

        [Test]
        public void BinaryOp_StringConcat_RecordsString()
        {
            var (result, chunk) = CompileAndRun(@"var s = ""hello""; __result__ = s + "" world"";");
            Assert.That(result.String, Is.EqualTo("hello world"));

            var profiles = AllBinaryProfiles(chunk);
            var anyStr = false;
            foreach (var (_, p) in profiles)
                if (p.LeftTypes.HasFlag(Chunk.BinaryTypeFlags.String))
                    anyStr = true;
            Assert.That(anyStr, Is.True, "string + literal → LeftTypes includes String");
        }

        // ── Mixed type accumulation ──────────────────────────────────────────

        [Test]
        public void BinaryOp_MixedLoop_AccumulatesBothIntAndDouble()
        {
            // The same binary site (inside the loop) sees int first, then double.
            const string script = @"
var values = [1, 2.5];
var sum = 0;
var i = 0;
while (i < 2) { sum = sum + values[i]; i = i + 1; }
__result__ = sum;
";
            var (_, chunk) = CompileAndRun(script);

            var profiles = AllBinaryProfiles(chunk);
            var bothSeen = false;
            foreach (var (_, p) in profiles)
                if (p.LeftTypes.HasFlag(Chunk.BinaryTypeFlags.Int) &&
                    p.RightTypes.HasFlag(Chunk.BinaryTypeFlags.Double))
                    bothSeen = true;
            Assert.That(bothSeen, Is.True,
                "loop adds int then double → right side accumulates Int|Double");
        }

        // ── BinaryIntConst path ──────────────────────────────────────────────

        [Test]
        public void BinaryOp_IntConstantOperand_RightIsAlwaysInt()
        {
            // i + 1 is likely compiled as BinaryIntConst — right is always Int.
            const string script = @"
var sum = 0;
var i = 0;
while (i < 5) { i = i + 1; }
__result__ = i;
";
            var (result, chunk) = CompileAndRun(script);
            Assert.That(result.Int, Is.EqualTo(5));

            var profiles = AllBinaryProfiles(chunk);
            Assert.That(profiles.Count, Is.GreaterThan(0));
            // At least one site has right=Int (from i + 1 / i < 5 comparisons).
            var anyRightInt = false;
            foreach (var (_, p) in profiles)
                if (p.RightTypes.HasFlag(Chunk.BinaryTypeFlags.Int)) anyRightInt = true;
            Assert.That(anyRightInt, Is.True);
        }

        // ── Correctness guard ────────────────────────────────────────────────

        [Test]
        public void BinaryOp_ProfilingDoesNotAffectResult()
        {
            var (result, _) = CompileAndRun("__result__ = 6 * 7;");
            Assert.That(result.Int, Is.EqualTo(42));
        }

        [Test]
        public void BinaryOp_UnexecutedChunk_ReturnsEmptyProfiles()
        {
            var chunk = ScriptEngine.Compile("function add(a, b) { return a + b; }");
            Assert.That(chunk.GetBinaryOpProfiles().Count, Is.EqualTo(0),
                "un-executed chunk should have no profile entries");
        }

        [Test]
        public void BinaryOp_ProfileOffsetWithinBytecodeRange()
        {
            // Use variables so the additions aren't constant-folded away.
            var (_, chunk) = CompileAndRun("var x = 1; var y = 2; var z = 3; __result__ = x + y + z;");
            var profiles = AllBinaryProfiles(chunk);
            Assert.That(profiles.Count, Is.GreaterThan(0));
            foreach (var (offset, _) in profiles)
                Assert.That(offset, Is.GreaterThan(0).And.LessThan(chunk.CodeBytes.Length));
        }
    }
}
