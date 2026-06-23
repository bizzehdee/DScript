using System.Text.Json;
using DScript;
using DScript.Compiler;
using DScript.Profiler;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class ProfilerTests
    {
        private static string RunAndProfile(string source, out CpuProfiler profiler)
        {
            var engine = new ScriptEngine();
            profiler = new CpuProfiler();
            engine.AttachProfiler(profiler);
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            // Note: attaching directly to VM (bypassing ScriptEngine.Run) means
            // we call AttachProfiler on the VM here for the integration path test.
            return profiler.GetProfile();
        }

        // Helper that goes through ScriptEngine.Run so the wiring is exercised.
        private static string RunViaEngine(string source, out CpuProfiler profiler)
        {
            profiler = new CpuProfiler();
            var engine = new ScriptEngine();
            engine.AttachProfiler(profiler);
            engine.Run(ScriptEngine.Compile(source));
            return profiler.GetProfile();
        }

        // ── CpuProfiler unit tests ────────────────────────────────────────────

        [Test]
        public void CpuProfiler_GetProfile_ReturnsValidJson()
        {
            var profiler = new CpuProfiler();
            profiler.Enter("foo", "test.js", 1, 0);
            profiler.Exit();
            var json = profiler.GetProfile();
            Assert.DoesNotThrow(() => JsonDocument.Parse(json));
        }

        [Test]
        public void CpuProfiler_GetProfile_ContainsRequiredTopLevelKeys()
        {
            var profiler = new CpuProfiler();
            profiler.Enter("bar", "test.js", 2, 0);
            profiler.Exit();
            using var doc = JsonDocument.Parse(profiler.GetProfile());
            var root = doc.RootElement;
            Assert.That(root.TryGetProperty("nodes",      out _), Is.True);
            Assert.That(root.TryGetProperty("startTime",  out _), Is.True);
            Assert.That(root.TryGetProperty("endTime",    out _), Is.True);
            Assert.That(root.TryGetProperty("samples",    out _), Is.True);
            Assert.That(root.TryGetProperty("timeDeltas", out _), Is.True);
        }

        [Test]
        public void CpuProfiler_RootNodeAlwaysPresent()
        {
            var profiler = new CpuProfiler();
            using var doc = JsonDocument.Parse(profiler.GetProfile());
            var nodes = doc.RootElement.GetProperty("nodes");
            Assert.That(nodes.GetArrayLength(), Is.GreaterThanOrEqualTo(1));
            var root = nodes[0];
            Assert.That(root.GetProperty("callFrame").GetProperty("functionName").GetString(),
                        Is.EqualTo("(root)"));
        }

        [Test]
        public void CpuProfiler_CalledFunctionAppearsAsChildOfRoot()
        {
            var profiler = new CpuProfiler();
            profiler.Enter("myFunc", "script.js", 10, 0);
            profiler.Exit();
            using var doc = JsonDocument.Parse(profiler.GetProfile());
            var nodes = doc.RootElement.GetProperty("nodes");
            string foundName = null;
            foreach (var node in nodes.EnumerateArray())
            {
                var name = node.GetProperty("callFrame").GetProperty("functionName").GetString();
                if (name == "myFunc") { foundName = name; break; }
            }
            Assert.That(foundName, Is.EqualTo("myFunc"));
        }

        [Test]
        public void CpuProfiler_NestedCalls_BuildCallTree()
        {
            var profiler = new CpuProfiler();
            profiler.Enter("outer", "", 1, 0);
            profiler.Enter("inner", "", 5, 0);
            profiler.Exit(); // inner
            profiler.Exit(); // outer
            using var doc = JsonDocument.Parse(profiler.GetProfile());
            var nodes = doc.RootElement.GetProperty("nodes");
            int outerChildren = -1;
            foreach (var node in nodes.EnumerateArray())
            {
                if (node.GetProperty("callFrame").GetProperty("functionName").GetString() == "outer")
                    outerChildren = node.GetProperty("children").GetArrayLength();
            }
            Assert.That(outerChildren, Is.EqualTo(1), "outer should have inner as a child");
        }

        [Test]
        public void CpuProfiler_GetProfile_IsIdempotent()
        {
            var profiler = new CpuProfiler();
            profiler.Enter("f", "", 1, 0);
            profiler.Exit();
            var first  = profiler.GetProfile();
            var second = profiler.GetProfile();
            Assert.That(first, Is.EqualTo(second));
        }

        [Test]
        public void CpuProfiler_StartTimeLessThanEndTime()
        {
            var profiler = new CpuProfiler();
            profiler.Enter("work", "", 1, 0);
            profiler.Exit();
            using var doc = JsonDocument.Parse(profiler.GetProfile());
            var start = doc.RootElement.GetProperty("startTime").GetInt64();
            var end   = doc.RootElement.GetProperty("endTime").GetInt64();
            Assert.That(end, Is.GreaterThanOrEqualTo(start));
        }

        [Test]
        public void CpuProfiler_SamplesAndTimeDeltasSameLength()
        {
            var profiler = new CpuProfiler();
            profiler.SampleIntervalMicros = 1; // 1µs so short calls still emit samples
            profiler.Enter("a", "", 1, 0);
            System.Threading.Thread.Sleep(1); // ensure non-zero self-time
            profiler.Exit();
            using var doc = JsonDocument.Parse(profiler.GetProfile());
            var samples    = doc.RootElement.GetProperty("samples").GetArrayLength();
            var timeDeltas = doc.RootElement.GetProperty("timeDeltas").GetArrayLength();
            Assert.That(samples, Is.EqualTo(timeDeltas));
        }

        [Test]
        public void CpuProfiler_AnonymousFunctionName_NormalisedToAnonymous()
        {
            var profiler = new CpuProfiler();
            profiler.Enter(null, "", 1, 0);
            profiler.Exit();
            using var doc = JsonDocument.Parse(profiler.GetProfile());
            var nodes = doc.RootElement.GetProperty("nodes");
            bool found = false;
            foreach (var node in nodes.EnumerateArray())
            {
                if (node.GetProperty("callFrame").GetProperty("functionName").GetString() == "(anonymous)")
                    found = true;
            }
            Assert.That(found, Is.True);
        }

        // ── ScriptEngine integration ──────────────────────────────────────────

        [Test]
        public void ScriptEngine_AttachProfiler_CapturesFunctionCall()
        {
            var json = RunViaEngine(@"
                function add(a, b) { return a + b; }
                var r = add(1, 2);
            ", out _);
            using var doc = JsonDocument.Parse(json);
            var nodes = doc.RootElement.GetProperty("nodes");
            bool foundAdd = false;
            foreach (var node in nodes.EnumerateArray())
            {
                if (node.GetProperty("callFrame").GetProperty("functionName").GetString() == "add")
                    foundAdd = true;
            }
            Assert.That(foundAdd, Is.True, "profile should contain an 'add' node");
        }

        [Test]
        public void ScriptEngine_AttachProfiler_DetachStopsCollection()
        {
            var profiler = new CpuProfiler();
            var engine = new ScriptEngine();
            engine.AttachProfiler(profiler);
            engine.Run(ScriptEngine.Compile("function f() {} f();"));
            engine.DetachProfiler();
            // After detach, running more code must not affect the already-taken snapshot.
            engine.Run(ScriptEngine.Compile("function g() {} g();"));
            var json = profiler.GetProfile();
            using var doc = JsonDocument.Parse(json);
            var nodes = doc.RootElement.GetProperty("nodes");
            bool foundG = false;
            foreach (var node in nodes.EnumerateArray())
            {
                if (node.GetProperty("callFrame").GetProperty("functionName").GetString() == "g")
                    foundG = true;
            }
            Assert.That(foundG, Is.False, "g() ran after detach and must not appear in the profile");
        }

        [Test]
        public void ScriptEngine_RecursiveFunction_ProfiledCorrectly()
        {
            var json = RunViaEngine(@"
                function fib(n) { return n <= 1 ? n : fib(n-1) + fib(n-2); }
                fib(10);
            ", out _);
            using var doc = JsonDocument.Parse(json);
            var nodes = doc.RootElement.GetProperty("nodes");
            bool foundFib = false;
            foreach (var node in nodes.EnumerateArray())
            {
                if (node.GetProperty("callFrame").GetProperty("functionName").GetString() == "fib")
                    foundFib = true;
            }
            Assert.That(foundFib, Is.True);
        }

        [Test]
        public void ScriptEngine_NativeFunctionCall_AppearsInProfile()
        {
            // Register a named native callback and call it from script.
            var profiler = new CpuProfiler();
            var engine = new ScriptEngine();
            engine.AttachProfiler(profiler);

            var nativeFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            nativeFn.SetCallback((scope, _) => { }, null);
            // Give it a 'name' child so the profiler can read it
            nativeFn.AddChild("name", new ScriptVar("myNative"));
            engine.Root.AddChild("myNative", nativeFn);

            engine.Run(ScriptEngine.Compile("myNative();"));
            var json = profiler.GetProfile();
            using var doc = JsonDocument.Parse(json);
            var nodes = doc.RootElement.GetProperty("nodes");
            bool found = false;
            foreach (var node in nodes.EnumerateArray())
            {
                var name = node.GetProperty("callFrame").GetProperty("functionName").GetString();
                if (name == "myNative") { found = true; break; }
            }
            Assert.That(found, Is.True, "native function with 'name' child should appear in profile");
        }
    }
}
