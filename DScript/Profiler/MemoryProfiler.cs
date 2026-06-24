/*
Copyright (c) 2014 - 2020 Darren Horrocks

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System.Collections.Generic;
using System.Text;

namespace DScript.Profiler
{
    /// <summary>
    /// Allocation-counting memory profiler. Attach to a <see cref="ScriptEngine"/>
    /// via <see cref="AttachTo"/>, run your script, then call <see cref="GetProfile"/>
    /// to retrieve a V8-compatible <c>.heapprofile</c> JSON string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The output can be loaded directly into Chrome DevTools (Memory ▸ Load profile)
    /// or VS Code's JavaScript profiler extension. It is the V8 <em>heap sampling
    /// profile</em> format — a call-tree where each node carries the number of bytes
    /// allocated directly in that function — not a full heap snapshot.
    /// </para>
    /// <para>
    /// Allocation sizes are estimates based on the <see cref="ScriptVar"/> type; they
    /// are proportionally correct but do not reflect actual CLR managed-heap sizes.
    /// </para>
    /// <para>
    /// <see cref="ScriptVar.SetAllocationHook"/> is a static global. Only one
    /// <see cref="MemoryProfiler"/> (or other hook consumer) may be active at a time;
    /// calling <see cref="AttachTo"/> overwrites any previously registered hook.
    /// </para>
    /// </remarks>
    public sealed class MemoryProfiler : IProfiler
    {
        // ── internal data model ───────────────────────────────────────────────

        private sealed class AllocNode
        {
            public int Id;
            public string FunctionName;
            public string Url;
            public int LineNumber;
            public int ColumnNumber;
            public long SelfBytes;
            public long SelfCount;
            public readonly List<int> ChildIds = [];
        }

        private sealed class TypeStats
        {
            public long Count;
            public long Bytes;
        }

        // ── fields ────────────────────────────────────────────────────────────

        private readonly Dictionary<int, AllocNode> _nodes = [];
        private readonly Dictionary<(int ParentId, string Name, string Url, int Line, int Col), int> _nodeIndex = [];
        private readonly Stack<int> _stack = new();   // node IDs
        private int _nextId = 1;
        private long _nextOrdinal;

        private readonly List<(long Size, int NodeId, long Ordinal)> _samples = [];
        private readonly Dictionary<string, TypeStats> _typeStats = [];

        private string _cachedProfile;

        // ── configuration ─────────────────────────────────────────────────────

        /// <summary>
        /// Maximum number of individual allocation samples recorded.
        /// Once the cap is reached, per-node byte totals still accumulate but
        /// individual sample entries stop being appended to keep the output file
        /// a manageable size.  Default: 100 000.
        /// </summary>
        public int MaxSamples { get; set; } = 100_000;

        // ── constructor ───────────────────────────────────────────────────────

        /// <summary>Creates a new MemoryProfiler ready to be attached.</summary>
        public MemoryProfiler()
        {
            var root = new AllocNode
            {
                Id           = _nextId++,
                FunctionName = "(root)",
                Url          = "",
                LineNumber   = -1,
                ColumnNumber = -1,
            };
            _nodes[root.Id] = root;
            _stack.Push(root.Id);
        }

        // ── attach / detach ───────────────────────────────────────────────────

        /// <summary>
        /// Starts allocation tracking and attaches call-stack instrumentation to
        /// <paramref name="engine"/>.  Call before <c>engine.Run()</c>.
        /// Overwrites any existing <see cref="ScriptVar.SetAllocationHook"/>.
        /// </summary>
        public void AttachTo(ScriptEngine engine)
        {
            engine.AttachProfiler(this);
            ScriptVar.SetAllocationHook(OnAllocate);
        }

        /// <summary>
        /// Stops allocation tracking and detaches from <paramref name="engine"/>.
        /// Call after <c>engine.Run()</c> and before <see cref="GetProfile"/>.
        /// </summary>
        // CA1822: intentionally an instance method — symmetry with AttachTo() and future
        // per-instance cleanup (e.g. flushing pooled state when pooling is added).
#pragma warning disable CA1822
        public void DetachFrom(ScriptEngine engine)
        {
            ScriptVar.SetAllocationHook(null);
            engine.DetachProfiler();
        }
#pragma warning restore CA1822

        /// <summary>
        /// Sets <see cref="ScriptVar.SetAllocationHook"/> without touching engine
        /// call-stack instrumentation.  Use when you want allocation counts only,
        /// without per-function attribution.
        /// </summary>
        public void Attach() => ScriptVar.SetAllocationHook(OnAllocate);

        /// <summary>Clears the allocation hook set by <see cref="Attach"/>.</summary>
        // CA1822: intentionally an instance method — symmetry with Attach() and to
        // prevent callers from writing MemoryProfiler.Detach() which could silently
        // clear a different profiler's hook than the one they think they own.
#pragma warning disable CA1822
        public void Detach() => ScriptVar.SetAllocationHook(null);
#pragma warning restore CA1822

        // ── IProfiler — call-stack tracking ───────────────────────────────────

        /// <inheritdoc/>
        public void Enter(string functionName, string url, int lineNumber, int columnNumber)
        {
            var name     = string.IsNullOrEmpty(functionName) ? "(anonymous)" : functionName;
            var urlSafe  = url ?? "";
            var parentId = _stack.Peek();
            var key      = (parentId, name, urlSafe, lineNumber, columnNumber);

            if (!_nodeIndex.TryGetValue(key, out var nodeId))
            {
                var node = new AllocNode
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

            _stack.Push(nodeId);
        }

        /// <inheritdoc/>
        public void Leave()
        {
            if (_stack.Count > 1) _stack.Pop();
        }

        // ── allocation hook ───────────────────────────────────────────────────

        private void OnAllocate(ScriptVar v)
        {
            var typeName = TypeNameOf(v);
            var size     = EstimateSize(v);
            var nodeId   = _stack.Peek();

            _nodes[nodeId].SelfBytes += size;
            _nodes[nodeId].SelfCount++;

            if (!_typeStats.TryGetValue(typeName, out var stats))
                _typeStats[typeName] = stats = new TypeStats();
            stats.Count++;
            stats.Bytes += size;

            if (_samples.Count < MaxSamples)
                _samples.Add((size, nodeId, _nextOrdinal));
            _nextOrdinal++;
        }

        // ── profile serialisation ─────────────────────────────────────────────

        /// <summary>
        /// Returns the V8 <c>.heapprofile</c> JSON string. Idempotent — subsequent
        /// calls return the same snapshot taken at the moment of the first call.
        /// Save the result to a <c>.heapprofile</c> file and open it in Chrome
        /// DevTools (Memory ▸ Load profile) or VS Code's JavaScript profiler.
        /// </summary>
        public string GetProfile()
        {
            if (_cachedProfile != null) return _cachedProfile;

            var sb = new StringBuilder(4096);
            sb.Append('{');

            // head — recursive call tree; each node carries selfSize (bytes allocated
            // directly in that function) so the flame chart shows allocation weight.
            sb.Append("\"head\":");
            WriteNode(sb, _nodes[1]);

            // samples — individual allocation events (up to MaxSamples)
            sb.Append(",\"samples\":[");
            for (var i = 0; i < _samples.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var (size, nodeId, ordinal) = _samples[i];
                sb.Append("{\"size\":").Append(size)
                  .Append(",\"nodeId\":").Append(nodeId)
                  .Append(",\"ordinal\":").Append(ordinal)
                  .Append('}');
            }
            sb.Append(']');

            // _summary — DScript extension; ignored by V8 tooling but useful for
            // quick textual analysis without parsing the full call tree.
            long totalBytes = 0;
            foreach (var n in _nodes.Values) totalBytes += n.SelfBytes;

            sb.Append(",\"_summary\":{");
            sb.Append("\"totalAllocations\":").Append(_nextOrdinal);
            sb.Append(",\"totalBytes\":").Append(totalBytes);
            sb.Append(",\"samplesRecorded\":").Append(_samples.Count);
            sb.Append(",\"byType\":{");
            var firstType = true;
            foreach (var kv in _typeStats)
            {
                if (!firstType) sb.Append(',');
                firstType = false;
                sb.Append(JsonString(kv.Key))
                  .Append(":{\"count\":").Append(kv.Value.Count)
                  .Append(",\"bytes\":").Append(kv.Value.Bytes)
                  .Append('}');
            }
            sb.Append("}}");

            sb.Append('}');
            return _cachedProfile = sb.ToString();
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private void WriteNode(StringBuilder sb, AllocNode node)
        {
            sb.Append("{\"callFrame\":{");
            sb.Append("\"functionName\":").Append(JsonString(node.FunctionName));
            sb.Append(",\"scriptId\":\"0\"");
            sb.Append(",\"url\":").Append(JsonString(node.Url));
            sb.Append(",\"lineNumber\":").Append(node.LineNumber);
            sb.Append(",\"columnNumber\":").Append(node.ColumnNumber);
            sb.Append('}');
            sb.Append(",\"selfSize\":").Append(node.SelfBytes);
            sb.Append(",\"id\":").Append(node.Id);
            sb.Append(",\"children\":[");
            var first = true;
            foreach (var childId in node.ChildIds)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteNode(sb, _nodes[childId]);
            }
            sb.Append("]}");
        }

        /// <summary>
        /// Returns the canonical type label used in the per-type summary.
        /// Matches the type labels shown in Chrome DevTools' heap analysis views.
        /// </summary>
        public static string TypeNameOf(ScriptVar v)
        {
            if (v.IsUndefined)                return "undefined";
            if (v.IsNull)                     return "null";
            if (v.IsInt)                      return "number(int)";
            if (v.IsDouble)                   return "number(double)";
            if (v.IsString)                   return "string";
            if (v.IsArray)                    return "array";
            if (v.IsNative && v.IsFunction)   return "function(native)";
            if (v.IsFunction)                 return "function";
            if (v.IsObject)                   return "object";
            if (v.IsRegexp)                   return "regexp";
            if (v.IsSymbol)                   return "symbol";
            if (v.IsBigInt)                   return "bigint";
            if (v.IsProxy)                    return "proxy";
            return "other";
        }

        /// <summary>
        /// Returns a proportionally-correct estimated byte size for a ScriptVar.
        /// The estimates are based on typical .NET 64-bit managed object sizes for
        /// each type; they are not exact CLR measurements.
        /// </summary>
        public static long EstimateSize(ScriptVar v)
        {
            if (v.IsUndefined || v.IsNull) return 24;  // header + flags only
            if (v.IsInt)                   return 32;  // + intData
            if (v.IsDouble)                return 40;  // + doubleData
            if (v.IsString)                return 64;  // fixed estimate; string body is a separate heap object
            if (v.IsArray  ||
                v.IsObject ||
                v.IsFunction)              return 80;  // + child-list linkage
            return 48;                                 // Symbol, BigInt, Regexp, Proxy, …
        }

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
                        if (c < 0x20) sb.Append($"\\u{(int)c:x4}");
                        else          sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
