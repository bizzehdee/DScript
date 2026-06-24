using NUnit.Framework;
using DScript.Vm;
using System.Collections.Generic;

namespace DScript.Test
{
    [TestFixture]
    public class CallSiteProfileTests
    {
        // Compile a script, run it through a fresh engine, and return both the
        // compiled top-level chunk and the __result__ variable.
        private static (ScriptVar result, Chunk chunk) CompileAndRun(string code)
        {
            var engine = new ScriptEngine();
            var chunk  = ScriptEngine.Compile(code);
            engine.Run(chunk);
            var result = engine.Root.GetParameter("__result__");
            return (result, chunk);
        }

        // Collect call-site profiles from a chunk and all its nested function bodies.
        private static List<(int SiteOffset, Chunk.CallSiteProfile Profile)> AllCallSiteProfiles(Chunk chunk)
        {
            var result = new List<(int, Chunk.CallSiteProfile)>();
            CollectProfiles(chunk, result);
            return result;

            static void CollectProfiles(Chunk c, List<(int, Chunk.CallSiteProfile)> out_)
            {
                foreach (var p in c.GetCallSiteProfiles())
                    out_.Add(p);
                foreach (var fn in c.Functions)
                    CollectProfiles(fn, out_);
            }
        }

        // ── Morphism transitions ───────────────────────────────────────────────
        // All tests use a top-level call-site (in the compiled chunk, not a
        // nested function body) so AllCallSiteProfiles finds them directly.

        [Test]
        public void CallSite_SameCallee_StaysMonomorphic()
        {
            // The same function is called 5 times → the call site stays Monomorphic.
            const string script = @"
function add(a, b) { return a + b; }
var r = 0;
r = add(r, 1);
r = add(r, 1);
r = add(r, 1);
r = add(r, 1);
r = add(r, 1);
__result__ = r;
";
            var (result, chunk) = CompileAndRun(script);
            Assert.That(result.Int, Is.EqualTo(5));

            var profiles = AllCallSiteProfiles(chunk);
            Assert.That(profiles.Count, Is.GreaterThan(0), "at least one call site should be profiled");

            var anyMono = false;
            foreach (var (_, p) in profiles)
                if (p.State == Chunk.CallSiteMorphism.Monomorphic) anyMono = true;
            Assert.That(anyMono, Is.True, "repeated calls to same function → Monomorphic");
        }

        [Test]
        public void CallSite_TwoDistinctCallees_BecomesBimorphic()
        {
            // A single call-site instruction must fire with two distinct callees.
            // A loop ensures both iterations hit the same bytecode Call instruction.
            const string script = @"
function add(a, b) { return a + b; }
function sub(a, b) { return a - b; }
var fns = [add, sub];
var r = 0;
var i = 0;
while (i < 2) { r = fns[i](3, 1); i = i + 1; }
__result__ = r;
";
            var (result, chunk) = CompileAndRun(script);
            Assert.That(result.Int, Is.EqualTo(2), "sub(3,1) == 2");

            var profiles = AllCallSiteProfiles(chunk);
            var anyBiOrMega = false;
            foreach (var (_, p) in profiles)
                if (p.State is Chunk.CallSiteMorphism.Bimorphic or Chunk.CallSiteMorphism.Megamorphic)
                    anyBiOrMega = true;
            Assert.That(anyBiOrMega, Is.True, "two distinct callees → Bimorphic (or Mega)");
        }

        [Test]
        public void CallSite_ThreeDistinctCallees_BecomesMegamorphic()
        {
            // Three different functions through the same call site → Megamorphic.
            // A loop ensures all iterations hit the same bytecode Call instruction.
            const string script = @"
function a() { return 1; }
function b() { return 2; }
function c() { return 3; }
var fns = [a, b, c, c];
var r = 0;
var i = 0;
while (i < 4) { r = fns[i](); i = i + 1; }
__result__ = r;
";
            var (result, chunk) = CompileAndRun(script);
            Assert.That(result.Int, Is.EqualTo(3));

            var profiles = AllCallSiteProfiles(chunk);
            var anyMega = false;
            foreach (var (_, p) in profiles)
                if (p.State == Chunk.CallSiteMorphism.Megamorphic) anyMega = true;
            Assert.That(anyMega, Is.True, "three distinct callees → Megamorphic");
        }

        [Test]
        public void CallSite_MegamorphicStaysMegamorphic()
        {
            // After reaching Mega, returning to earlier callees must NOT demote state.
            // All calls go through the same loop call-site instruction.
            const string script = @"
function a() { return 1; }
function b() { return 2; }
function c() { return 3; }
var fns = [a, b, c, a, a];
var i = 0;
while (i < 5) { fns[i](); i = i + 1; }
__result__ = 42;
";
            var (result, chunk) = CompileAndRun(script);
            Assert.That(result.Int, Is.EqualTo(42));

            var profiles = AllCallSiteProfiles(chunk);
            var anyMega = false;
            foreach (var (_, p) in profiles)
                if (p.State == Chunk.CallSiteMorphism.Megamorphic) anyMega = true;
            Assert.That(anyMega, Is.True, "site stays Megamorphic after revisiting earlier callee");
        }

        // ── Correctness under profiling ────────────────────────────────────────

        [Test]
        public void CallSite_ProfilingDoesNotAffectReturnValue()
        {
            var (result, _) = CompileAndRun(@"
function double(x) { return x * 2; }
__result__ = double(21);
");
            Assert.That(result.Int, Is.EqualTo(42));
        }

        [Test]
        public void CallSite_NativeCallee_IsProfiledWithoutError()
        {
            // A native C# callback called through a script call site must not crash.
            var engine = new ScriptEngine();
            engine.AddNative("function nativeAdd(a, b)", (scope, _) =>
            {
                var a = scope.GetParameter("a").Int;
                var b = scope.GetParameter("b").Int;
                scope.FindChildOrCreate(ScriptVar.ReturnVarName)
                     .ReplaceWith(ScriptVar.FromInt(a + b));
            }, null);
            var chunk = ScriptEngine.Compile("__result__ = nativeAdd(3, 4);");
            engine.Run(chunk);
            Assert.That(engine.Root.GetParameter("__result__").Int, Is.EqualTo(7));
        }

        [Test]
        public void CallSite_Uninitialised_BeforeFirstCall()
        {
            // A compiled-but-never-executed chunk has no profile entries at all.
            var chunk = ScriptEngine.Compile(@"
function noop() {}
function caller() { noop(); }
");
            Assert.That(chunk.GetCallSiteProfiles().Count, Is.EqualTo(0),
                "un-executed chunk should have no profile entries");
        }

        [Test]
        public void CallSite_ProfileIndexMatchesBytecodeOffset()
        {
            const string script = @"
function id(x) { return x; }
__result__ = id(10) + id(20);
";
            var (result, chunk) = CompileAndRun(script);
            Assert.That(result.Int, Is.EqualTo(30));

            var profiles = AllCallSiteProfiles(chunk);
            Assert.That(profiles.Count, Is.GreaterThan(0));
            foreach (var (siteOffset, _) in profiles)
                Assert.That(siteOffset, Is.GreaterThan(0).And.LessThan(chunk.CodeBytes.Length),
                    "site offset must be within the top-level bytecode range");
        }
    }
}
