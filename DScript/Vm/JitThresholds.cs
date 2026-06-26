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

namespace DScript.Vm
{
    /// <summary>
    /// Tunable thresholds that decide when a <see cref="Chunk"/> is "hot" enough
    /// to be handed to a JIT compiler. These are the standard tier-up triggers in
    /// production JIT engines: a function called many times, or a loop whose
    /// back-edges have been crossed many times, is a good compilation candidate.
    /// </summary>
    public static class JitThresholds
    {
        /// <summary>
        /// Number of invocations after which a function chunk is considered hot.
        /// Compared against <see cref="Chunk.InvocationCount"/>.
        /// </summary>
        public const int InvocationThreshold = 1000;

        /// <summary>
        /// Number of back-edges (loop iterations) after which a chunk is considered
        /// hot. Compared against <see cref="Chunk.BackEdgeCount"/>.
        /// </summary>
        public const int BackEdgeThreshold = 10000;

        /// <summary>
        /// Number of back-edges crossed within a single still-running interpreter
        /// frame after which the VM attempts on-stack replacement (OSR): it compiles
        /// the chunk and transfers control into the compiled code at the loop header,
        /// so a long-running loop in a function that is only entered once (and would
        /// therefore never tier up on invocation count) still gets compiled.
        /// </summary>
        public const int OsrBackEdgeThreshold = 10000;

        /// <summary>
        /// Number of deoptimizations after which a chunk's speculative compilation is
        /// abandoned: it is recompiled with the conservative (never-deopting) tier
        /// instead. Compared against <see cref="Chunk.DeoptCount"/>.
        /// </summary>
        public const int DeoptThreshold = 5;
    }
}
