using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DScript.Profiler
{
    /// <summary>
    /// Instrumented CPU profiler. Attach to a <see cref="ScriptEngine"/> via
    /// <c>engine.AttachProfiler(profiler)</c>, run your script, then call
    /// <see cref="GetProfile"/> to retrieve a V8-compatible <c>.cpuprofile</c>
    /// JSON string that can be loaded directly into Chrome DevTools or VS Code's
    /// JavaScript profiler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Because this is an <em>instrumented</em> profiler (not a sampling profiler),
    /// it hooks actual function enter/exit events rather than periodic thread
    /// interrupts. It synthesises a stream of virtual samples spaced
    /// <see cref="SampleIntervalMicros"/> microseconds apart, proportional to each
    /// function's self-time, so that Chrome DevTools' flame chart displays correct
    /// relative widths.
    /// </para>
    /// <para>
    /// Generator and async functions are not profiled at the individual
    /// <c>.next()</c> / resume level — their iterator-factory call is captured
    /// but the body's execution time is attributed to the engine's microtask
    /// infrastructure rather than to the generator function itself.
    /// </para>
    /// </remarks>
    public sealed class CpuProfiler : IProfiler
    {
        /// <summary>
        /// Granularity of synthesised samples in microseconds. Each function whose
        /// self-time exceeds this threshold contributes at least one sample.
        /// Default: 10 µs.
        /// </summary>
        public int SampleIntervalMicros { get; set; } = 10;

        // ── internal data model ───────────────────────────────────────────────

        private sealed class ProfileNode
        {
            public int Id;
            public string FunctionName;
            public string Url;
            public int LineNumber;
            public int ColumnNumber;
            public long SelfTicks;
            public readonly List<int> ChildIds = [];
        }

        private sealed class ActiveFrame
        {
            public int NodeId;
            public long EntryTicks;
            public long ChildTicks;
        }

        private readonly Dictionary<int, ProfileNode> _nodes = [];
        private readonly Dictionary<(int ParentId, string Name, string Url, int Line, int Col), int> _nodeIndex = [];
        private readonly Stack<ActiveFrame> _stack = new();
        private int _nextId = 1;
        private readonly long _startTicks = Stopwatch.GetTimestamp();
        private long _endTicks;

        /// <summary>Initialise the profiler and start the wall-clock timer.</summary>
        public CpuProfiler()
        {
            var root = new ProfileNode
            {
                Id         = _nextId++,
                FunctionName = "(root)",
                Url        = "",
                LineNumber   = -1,
                ColumnNumber = -1,
            };
            _nodes[root.Id] = root;
            _stack.Push(new ActiveFrame { NodeId = root.Id, EntryTicks = _startTicks });
        }

        // ── IProfiler ─────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public void Enter(string functionName, string url, int lineNumber, int columnNumber)
        {
            var name     = string.IsNullOrEmpty(functionName) ? "(anonymous)" : functionName;
            var urlSafe  = url ?? "";
            var parentId = _stack.Peek().NodeId;
            var key      = (parentId, name, urlSafe, lineNumber, columnNumber);

            if (!_nodeIndex.TryGetValue(key, out var nodeId))
            {
                var node = new ProfileNode
                {
                    Id           = _nextId++,
                    FunctionName = name,
                    Url          = urlSafe,
                    LineNumber   = lineNumber,
                    ColumnNumber = columnNumber,
                };
                _nodes[node.Id] = node;
                _nodes[parentId].ChildIds.Add(node.Id);
                _nodeIndex[key] = nodeId = node.Id;
            }

            _stack.Push(new ActiveFrame { NodeId = nodeId, EntryTicks = Stopwatch.GetTimestamp() });
        }

        /// <inheritdoc/>
        public void Exit()
        {
            if (_stack.Count <= 1) return; // root is never explicitly popped

            var frame    = _stack.Pop();
            var exitTick = Stopwatch.GetTimestamp();
            var total    = exitTick - frame.EntryTicks;
            var self     = total - frame.ChildTicks;

            _nodes[frame.NodeId].SelfTicks += self;
            _stack.Peek().ChildTicks       += total;
        }

        // ── profile serialisation ─────────────────────────────────────────────

        /// <summary>
        /// Finalise timing and return the V8 <c>.cpuprofile</c> JSON string.
        /// May be called multiple times; subsequent calls return the same snapshot
        /// taken at the moment of the first call.
        /// </summary>
        public string GetProfile()
        {
            if (_endTicks == 0)
            {
                _endTicks = Stopwatch.GetTimestamp();
                // Close the root frame
                var rootFrame = _stack.Peek();
                var rootTotal = _endTicks - rootFrame.EntryTicks;
                var rootSelf  = rootTotal  - rootFrame.ChildTicks;
                _nodes[rootFrame.NodeId].SelfTicks = rootSelf;
            }

            long TicksToMicros(long t) => t * 1_000_000L / Stopwatch.Frequency;

            var startUs = TicksToMicros(_startTicks);
            var endUs   = TicksToMicros(_endTicks);
            var intervalUs = SampleIntervalMicros;

            // Compute hitCount per node and build the samples + timeDeltas arrays.
            // We do a depth-first walk of the call tree and emit samples for each
            // node proportional to its self-time. This approximates chronological
            // ordering (callers before callees, then caller resumes) while keeping
            // the implementation simple and allocation-lean.
            var samples    = new List<int>();
            var timeDeltas = new List<long>();
            var hitCounts  = new Dictionary<int, int>();

            void Walk(int nodeId)
            {
                var node     = _nodes[nodeId];
                var selfUs   = TicksToMicros(node.SelfTicks);
                var hits     = (int)(selfUs / intervalUs);
                if (hits > 0)
                {
                    hitCounts[nodeId] = hits;
                    for (var i = 0; i < hits; i++)
                    {
                        if (samples.Count == 0)
                            timeDeltas.Add(0);
                        else
                            timeDeltas.Add(intervalUs);
                        samples.Add(nodeId);
                    }
                }
                foreach (var childId in node.ChildIds)
                    Walk(childId);
            }
            Walk(1); // root is always id=1

            // Serialize to JSON manually — avoids any external dependency
            var sb = new StringBuilder(512);
            sb.Append('{');

            // nodes array
            sb.Append("\"nodes\":[");
            var firstNode = true;
            foreach (var node in _nodes.Values)
            {
                if (!firstNode) sb.Append(',');
                firstNode = false;
                sb.Append("{\"id\":").Append(node.Id);
                sb.Append(",\"callFrame\":{");
                sb.Append("\"functionName\":").Append(JsonString(node.FunctionName));
                sb.Append(",\"scriptId\":\"0\"");
                sb.Append(",\"url\":").Append(JsonString(node.Url));
                sb.Append(",\"lineNumber\":").Append(node.LineNumber);
                sb.Append(",\"columnNumber\":").Append(node.ColumnNumber);
                sb.Append('}');
                sb.Append(",\"hitCount\":").Append(hitCounts.GetValueOrDefault(node.Id, 0));
                sb.Append(",\"children\":[");
                for (var i = 0; i < node.ChildIds.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(node.ChildIds[i]);
                }
                sb.Append("]}");
            }
            sb.Append(']');

            sb.Append(",\"startTime\":").Append(startUs);
            sb.Append(",\"endTime\":").Append(endUs);

            sb.Append(",\"samples\":[");
            for (var i = 0; i < samples.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(samples[i]);
            }
            sb.Append(']');

            sb.Append(",\"timeDeltas\":[");
            for (var i = 0; i < timeDeltas.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(timeDeltas[i]);
            }
            sb.Append(']');

            sb.Append('}');
            return sb.ToString();
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static string JsonString(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20)
                            sb.Append($"\\u{(int)c:x4}");
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
