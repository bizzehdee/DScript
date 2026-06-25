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
using DScript.Vm;

namespace DScript.Jit
{
    /// <summary>A read-only snapshot of one chunk's JIT status, for tuning and tooling.</summary>
    public sealed class JitChunkReport
    {
        public string Name { get; init; }
        public Chunk.JitStatus State { get; init; }
        public int InvocationCount { get; init; }
        public int BackEdgeCount { get; init; }
        public int DeoptCount { get; init; }
        public bool IsCompiled { get; init; }
        public bool IsHot { get; init; }

        /// <summary>
        /// Why this chunk is not JIT-compilable, or <c>null</c> if it is compilable (it
        /// may simply be below the hotness threshold). Examples: "unsupported opcode:
        /// EnterTry", "generator or async function".
        /// </summary>
        public string DeclineReason { get; init; }

        public override string ToString() =>
            $"{Name}: {State} (inv={InvocationCount}, backedge={BackEdgeCount}, deopt={DeoptCount})" +
            (DeclineReason != null ? $" — declined: {DeclineReason}" : IsCompiled ? " — compiled" : "");
    }

    /// <summary>
    /// Read-only observability for the JIT: report a chunk's compilation state, hotness
    /// counters, deopt count, and — when it will not compile — the reason why. Useful
    /// for tuning thresholds and discovering why an expected chunk was never compiled.
    /// </summary>
    public static class JitDiagnostics
    {
        /// <summary>Describe a single chunk's JIT status.</summary>
        public static JitChunkReport Describe(Chunk chunk)
        {
            // A chunk that already failed to compile, or hasn't compiled, may have a
            // structural reason it can't be JIT'd; surface it. (A compilable chunk
            // returns null here even if it just hasn't crossed the threshold yet.)
            JitDecoder.Decode(chunk, out var declineReason);

            return new JitChunkReport
            {
                Name = chunk.Name,
                State = chunk.JitState,
                InvocationCount = chunk.InvocationCount,
                BackEdgeCount = chunk.BackEdgeCount,
                DeoptCount = chunk.DeoptCount,
                IsCompiled = chunk.CompiledDelegate != null,
                IsHot = chunk.IsHot(),
                DeclineReason = declineReason,
            };
        }

        /// <summary>Describe a chunk and all of its nested function chunks (depth-first).</summary>
        public static IReadOnlyList<JitChunkReport> DescribeAll(Chunk root)
        {
            var reports = new List<JitChunkReport>();
            Collect(root, reports);
            return reports;
        }

        private static void Collect(Chunk chunk, List<JitChunkReport> into)
        {
            into.Add(Describe(chunk));
            foreach (var fn in chunk.Functions)
                Collect(fn, into);
        }
    }
}
