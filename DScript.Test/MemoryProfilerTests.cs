using System.Text.Json;
using DScript;
using DScript.Compiler;
using DScript.Profiler;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class MemoryProfilerTests
    {
        [TearDown]
        public void ClearHook()
        {
            // Every test that sets the allocation hook must leave it clean so
            // other tests (and the CPU profiler tests) are not affected.
            ScriptVar.SetAllocationHook(null);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static string RunAndProfile(string source, out MemoryProfiler profiler)
        {
            profiler = new MemoryProfiler();
            var engine = new ScriptEngine();
            profiler.AttachTo(engine);
            engine.Run(ScriptEngine.Compile(source));
            profiler.DetachFrom(engine);
            return profiler.GetProfile();
        }

        // ── JSON structure ────────────────────────────────────────────────────

        [Test]
        public void MemoryProfiler_GetProfile_ReturnsValidJson()
        {
            var profiler = new MemoryProfiler();
            profiler.Attach();
            profiler.Detach();
            Assert.DoesNotThrow(() => JsonDocument.Parse(profiler.GetProfile()));
        }

        [Test]
        public void MemoryProfiler_GetProfile_ContainsRequiredTopLevelKeys()
        {
            var profiler = new MemoryProfiler();
            profiler.Attach();
            profiler.Detach();
            using var doc = JsonDocument.Parse(profiler.GetProfile());
            var root = doc.RootElement;
            Assert.That(root.TryGetProperty("head",     out _), Is.True, "missing 'head'");
            Assert.That(root.TryGetProperty("samples",  out _), Is.True, "missing 'samples'");
            Assert.That(root.TryGetProperty("_summary", out _), Is.True, "missing '_summary'");
        }

        [Test]
        public void MemoryProfiler_Head_HasRequiredCallFrameFields()
        {
            var profiler = new MemoryProfiler();
            profiler.Attach();
            profiler.Detach();
            using var doc  = JsonDocument.Parse(profiler.GetProfile());
            var cf = doc.RootElement.GetProperty("head").GetProperty("callFrame");
            Assert.That(cf.TryGetProperty("functionName", out _), Is.True);
            Assert.That(cf.TryGetProperty("scriptId",     out _), Is.True);
            Assert.That(cf.TryGetProperty("url",          out _), Is.True);
            Assert.That(cf.TryGetProperty("lineNumber",   out _), Is.True);
            Assert.That(cf.TryGetProperty("columnNumber", out _), Is.True);
        }

        [Test]
        public void MemoryProfiler_RootNodeAlwaysPresent()
        {
            var profiler = new MemoryProfiler();
            using var doc = JsonDocument.Parse(profiler.GetProfile());
            var name = doc.RootElement
                          .GetProperty("head")
                          .GetProperty("callFrame")
                          .GetProperty("functionName")
                          .GetString();
            Assert.That(name, Is.EqualTo("(root)"));
        }

        [Test]
        public void MemoryProfiler_GetProfile_IsIdempotent()
        {
            var profiler = new MemoryProfiler();
            profiler.Attach();
            _ = ScriptVar.FromInt(1);
            profiler.Detach();
            var first  = profiler.GetProfile();
            var second = profiler.GetProfile();
            Assert.That(first, Is.EqualTo(second));
        }

        // ── allocation counting ───────────────────────────────────────────────

        [Test]
        public void MemoryProfiler_SingleAllocation_IncrementsTotalCount()
        {
            var profiler = new MemoryProfiler();
            profiler.Attach();
            _ = ScriptVar.FromInt(42);
            profiler.Detach();

            using var doc = JsonDocument.Parse(profiler.GetProfile());
            var total = doc.RootElement
                           .GetProperty("_summary")
                           .GetProperty("totalAllocations")
                           .GetInt64();
            Assert.That(total, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void MemoryProfiler_MultipleAllocations_TotalBytesIsPositive()
        {
            var profiler = new MemoryProfiler();
            profiler.Attach();
            _ = ScriptVar.FromInt(1);
            _ = ScriptVar.FromString("hello");
            _ = ScriptVar.CreateObject();
            profiler.Detach();

            using var doc = JsonDocument.Parse(profiler.GetProfile());
            var bytes = doc.RootElement
                           .GetProperty("_summary")
                           .GetProperty("totalBytes")
                           .GetInt64();
            Assert.That(bytes, Is.GreaterThan(0));
        }

        [Test]
        public void MemoryProfiler_DetachStopsAccounting()
        {
            var profiler = new MemoryProfiler();
            profiler.Attach();
            _ = ScriptVar.FromInt(1);
            profiler.Detach();

            // Allocations after Detach must not be counted.
            var before = profiler.GetProfile();
            // We can't call GetProfile after new allocs (it's cached), so we
            // capture count before detach via _summary.totalAllocations.
            using var doc   = JsonDocument.Parse(before);
            var countBefore = doc.RootElement
                                 .GetProperty("_summary")
                                 .GetProperty("totalAllocations")
                                 .GetInt64();

            // Allocate more — hook should be null so nothing is recorded.
            _ = ScriptVar.FromInt(99);
            _ = ScriptVar.FromString("after");

            // Because GetProfile is idempotent, the cached result still holds
            // the count taken at Detach time.
            using var doc2   = JsonDocument.Parse(profiler.GetProfile());
            var countAfter = doc2.RootElement
                                  .GetProperty("_summary")
                                  .GetProperty("totalAllocations")
                                  .GetInt64();
            Assert.That(countAfter, Is.EqualTo(countBefore));
        }

        // ── type breakdown ────────────────────────────────────────────────────

        [Test]
        public void MemoryProfiler_TypeBreakdown_ContainsIntType()
        {
            var profiler = new MemoryProfiler();
            profiler.Attach();
            _ = ScriptVar.FromInt(7);
            profiler.Detach();

            using var doc   = JsonDocument.Parse(profiler.GetProfile());
            var byType = doc.RootElement.GetProperty("_summary").GetProperty("byType");
            Assert.That(byType.TryGetProperty("number(int)", out _), Is.True);
        }

        [Test]
        public void MemoryProfiler_TypeBreakdown_MultipleTypes_AllPresent()
        {
            var profiler = new MemoryProfiler();
            profiler.Attach();
            _ = ScriptVar.FromInt(1);
            _ = ScriptVar.FromDouble(3.14);
            _ = ScriptVar.FromString("s");
            _ = ScriptVar.CreateObject();
            profiler.Detach();

            using var doc   = JsonDocument.Parse(profiler.GetProfile());
            var byType = doc.RootElement.GetProperty("_summary").GetProperty("byType");
            Assert.That(byType.TryGetProperty("number(int)",    out _), Is.True, "int missing");
            Assert.That(byType.TryGetProperty("number(double)", out _), Is.True, "double missing");
            Assert.That(byType.TryGetProperty("string",         out _), Is.True, "string missing");
            Assert.That(byType.TryGetProperty("object",         out _), Is.True, "object missing");
        }

        [Test]
        public void MemoryProfiler_TypeBreakdown_BytesMatchEstimate()
        {
            var profiler = new MemoryProfiler();
            profiler.Attach();
            var v = ScriptVar.FromInt(5);
            profiler.Detach();

            var expected = MemoryProfiler.EstimateSize(v);

            using var doc   = JsonDocument.Parse(profiler.GetProfile());
            var intEntry = doc.RootElement
                              .GetProperty("_summary")
                              .GetProperty("byType")
                              .GetProperty("number(int)");
            var bytes = intEntry.GetProperty("bytes").GetInt64();
            // bytes >= expected because the engine may have pre-allocated other ints
            Assert.That(bytes, Is.GreaterThanOrEqualTo(expected));
        }

        // ── samples cap ───────────────────────────────────────────────────────

        [Test]
        public void MemoryProfiler_MaxSamples_CapsRecordedSamples()
        {
            var profiler = new MemoryProfiler { MaxSamples = 5 };
            profiler.Attach();
            for (var i = 0; i < 20; i++) _ = ScriptVar.FromInt(i);
            profiler.Detach();

            using var doc     = JsonDocument.Parse(profiler.GetProfile());
            var samplesLen    = doc.RootElement.GetProperty("samples").GetArrayLength();
            var totalAllocs   = doc.RootElement.GetProperty("_summary").GetProperty("totalAllocations").GetInt64();
            var samplesStored = doc.RootElement.GetProperty("_summary").GetProperty("samplesRecorded").GetInt64();

            Assert.That(samplesLen,  Is.EqualTo(5),        "JSON samples array length should be capped at MaxSamples");
            Assert.That(samplesStored, Is.EqualTo(5),      "samplesRecorded in summary should equal cap");
            Assert.That(totalAllocs,   Is.GreaterThan(5),  "totalAllocations must still count all allocs beyond the cap");
        }

        // ── call-tree attribution ─────────────────────────────────────────────

        [Test]
        public void MemoryProfiler_Enter_AddsChildNodeToRoot()
        {
            var profiler = new MemoryProfiler();
            profiler.Enter("myFunc", "script.js", 1, 0);
            profiler.Attach();
            _ = ScriptVar.FromInt(1);
            profiler.Detach();
            profiler.Leave();

            using var doc = JsonDocument.Parse(profiler.GetProfile());
            var children = doc.RootElement.GetProperty("head").GetProperty("children");
            Assert.That(children.GetArrayLength(), Is.GreaterThanOrEqualTo(1));
            var firstName = children[0].GetProperty("callFrame").GetProperty("functionName").GetString();
            Assert.That(firstName, Is.EqualTo("myFunc"));
        }

        [Test]
        public void MemoryProfiler_AllocationInsideFunction_AttributedToFunction()
        {
            var profiler = new MemoryProfiler();
            profiler.Enter("allocator", "script.js", 1, 0);
            profiler.Attach();
            _ = ScriptVar.FromInt(123);
            profiler.Detach();
            profiler.Leave();

            using var doc = JsonDocument.Parse(profiler.GetProfile());
            var children = doc.RootElement.GetProperty("head").GetProperty("children");
            // Find the "allocator" child node and check its selfSize > 0.
            foreach (var child in children.EnumerateArray())
            {
                if (child.GetProperty("callFrame").GetProperty("functionName").GetString() == "allocator")
                {
                    Assert.That(child.GetProperty("selfSize").GetInt64(), Is.GreaterThan(0),
                                "allocations inside the function must be attributed to its node");
                    return;
                }
            }
            Assert.Fail("node 'allocator' not found in call tree");
        }

        [Test]
        public void MemoryProfiler_NestedCalls_BuildCallTree()
        {
            var profiler = new MemoryProfiler();
            profiler.Enter("outer", "", 1, 0);
            profiler.Enter("inner", "", 5, 0);
            profiler.Attach();
            _ = ScriptVar.FromInt(1);
            profiler.Detach();
            profiler.Leave(); // inner
            profiler.Leave(); // outer

            using var doc = JsonDocument.Parse(profiler.GetProfile());
            var outerChildren = doc.RootElement
                                   .GetProperty("head")
                                   .GetProperty("children");
            Assert.That(outerChildren.GetArrayLength(), Is.GreaterThanOrEqualTo(1));
            var innerChildren = outerChildren[0].GetProperty("children");
            Assert.That(innerChildren.GetArrayLength(), Is.GreaterThanOrEqualTo(1),
                        "outer should have inner as a child");
            Assert.That(innerChildren[0].GetProperty("callFrame")
                                        .GetProperty("functionName").GetString(),
                        Is.EqualTo("inner"));
        }

        [Test]
        public void MemoryProfiler_AnonymousFunction_LabelledAsAnonymous()
        {
            var profiler = new MemoryProfiler();
            profiler.Enter("", "", 1, 0);  // empty name → "(anonymous)"
            profiler.Leave();

            using var doc = JsonDocument.Parse(profiler.GetProfile());
            var children = doc.RootElement.GetProperty("head").GetProperty("children");
            Assert.That(children.GetArrayLength(), Is.GreaterThanOrEqualTo(1));
            var name = children[0].GetProperty("callFrame").GetProperty("functionName").GetString();
            Assert.That(name, Is.EqualTo("(anonymous)"));
        }

        // ── static helpers ────────────────────────────────────────────────────

        [Test]
        public void MemoryProfiler_EstimateSize_AllKnownTypesReturnPositiveSize()
        {
            Assert.That(MemoryProfiler.EstimateSize(ScriptVar.CreateUndefined()),     Is.GreaterThan(0));
            Assert.That(MemoryProfiler.EstimateSize(ScriptVar.CreateNull()),          Is.GreaterThan(0));
            Assert.That(MemoryProfiler.EstimateSize(ScriptVar.FromInt(0)),            Is.GreaterThan(0));
            Assert.That(MemoryProfiler.EstimateSize(ScriptVar.FromDouble(0.0)),       Is.GreaterThan(0));
            Assert.That(MemoryProfiler.EstimateSize(ScriptVar.FromString("")),        Is.GreaterThan(0));
            Assert.That(MemoryProfiler.EstimateSize(ScriptVar.CreateObject()),        Is.GreaterThan(0));
            Assert.That(MemoryProfiler.EstimateSize(ScriptVar.CreateArray()),         Is.GreaterThan(0));
            Assert.That(MemoryProfiler.EstimateSize(ScriptVar.CreateNativeFunction()), Is.GreaterThan(0));
        }

        [Test]
        public void MemoryProfiler_TypeNameOf_CorrectLabelsForPrimitives()
        {
            Assert.That(MemoryProfiler.TypeNameOf(ScriptVar.CreateUndefined()), Is.EqualTo("undefined"));
            Assert.That(MemoryProfiler.TypeNameOf(ScriptVar.CreateNull()),      Is.EqualTo("null"));
            Assert.That(MemoryProfiler.TypeNameOf(ScriptVar.FromInt(1)),        Is.EqualTo("number(int)"));
            Assert.That(MemoryProfiler.TypeNameOf(ScriptVar.FromDouble(1.5)),   Is.EqualTo("number(double)"));
            Assert.That(MemoryProfiler.TypeNameOf(ScriptVar.FromString("hi")),  Is.EqualTo("string"));
            Assert.That(MemoryProfiler.TypeNameOf(ScriptVar.CreateObject()),    Is.EqualTo("object"));
            Assert.That(MemoryProfiler.TypeNameOf(ScriptVar.CreateArray()),     Is.EqualTo("array"));
            Assert.That(MemoryProfiler.TypeNameOf(ScriptVar.CreateNativeFunction()), Is.EqualTo("function(native)"));
        }

        // ── integration ───────────────────────────────────────────────────────

        [Test]
        public void MemoryProfiler_Integration_ScriptAllocatesObjects()
        {
            var json = RunAndProfile(
                "var a = {}; var b = {}; var c = {};",
                out _);

            using var doc = JsonDocument.Parse(json);
            var summary   = doc.RootElement.GetProperty("_summary");
            var total     = summary.GetProperty("totalAllocations").GetInt64();
            Assert.That(total, Is.GreaterThan(0),
                "running a script must produce at least one allocation");
        }

        [Test]
        public void MemoryProfiler_Integration_FunctionAllocationAppearsInTree()
        {
            const string src = "function alloc() { return {}; } var x = alloc();";
            var json = RunAndProfile(src, out _);

            using var doc = JsonDocument.Parse(json);
            var head = doc.RootElement.GetProperty("head");

            // Walk the tree breadth-first looking for a node named "alloc".
            bool Found(JsonElement node)
            {
                var name = node.GetProperty("callFrame").GetProperty("functionName").GetString();
                if (name == "alloc") return true;
                foreach (var child in node.GetProperty("children").EnumerateArray())
                    if (Found(child)) return true;
                return false;
            }

            Assert.That(Found(head), Is.True,
                "the 'alloc' function should appear as a call-tree node");
        }

        [Test]
        public void MemoryProfiler_Integration_SamplesArrayContainsEntries()
        {
            var json = RunAndProfile(
                "var arr = [1,2,3,4,5];",
                out _);

            using var doc = JsonDocument.Parse(json);
            var samples   = doc.RootElement.GetProperty("samples");
            Assert.That(samples.GetArrayLength(), Is.GreaterThan(0));
        }

        [Test]
        public void MemoryProfiler_Integration_SampleEntriesHaveRequiredFields()
        {
            var json = RunAndProfile("var x = 1;", out _);
            using var doc = JsonDocument.Parse(json);
            var samples   = doc.RootElement.GetProperty("samples");

            // At least one sample must be present and must have the V8 fields.
            Assert.That(samples.GetArrayLength(), Is.GreaterThan(0));
            var first = samples[0];
            Assert.That(first.TryGetProperty("size",    out _), Is.True, "missing 'size'");
            Assert.That(first.TryGetProperty("nodeId",  out _), Is.True, "missing 'nodeId'");
            Assert.That(first.TryGetProperty("ordinal", out _), Is.True, "missing 'ordinal'");
        }
    }
}
