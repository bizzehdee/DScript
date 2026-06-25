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
    /// Global, opt-in registration point for the active JIT compiler. The VM reads
    /// <see cref="Current"/> when deciding whether to compile a hot chunk; when it
    /// is <c>null</c> (the default) the VM runs purely interpreted with no JIT
    /// behaviour at all. A host enables JIT by calling <see cref="Register"/> once
    /// at startup and disables it again with <see cref="Clear"/>.
    /// </summary>
    public static class JitRegistry
    {
        private static readonly object Gate = new();
        private static IJitCompiler current;

        /// <summary>
        /// The currently-registered compiler, or <c>null</c> if JIT is disabled.
        /// Reads are lock-free; only registration mutates the field.
        /// </summary>
        public static IJitCompiler Current => current;

        /// <summary>Install <paramref name="compiler"/> as the active JIT back-end.</summary>
        public static void Register(IJitCompiler compiler)
        {
            lock (Gate)
            {
                current = compiler;
            }
        }

        /// <summary>Remove any registered compiler, returning the VM to pure interpretation.</summary>
        public static void Clear()
        {
            lock (Gate)
            {
                current = null;
            }
        }

        // ── background compilation (opt-in) ──────────────────────────────────────

        /// <summary>
        /// When true, hot chunks are compiled on a background worker thread instead of
        /// synchronously on the interpreter thread, so the compile pause does not stall
        /// execution — the chunk keeps running interpreted until the worker publishes
        /// its <see cref="Chunk.CompiledDelegate"/>. Off by default (synchronous).
        ///
        /// Note: the worker reads the chunk's profiling arrays while the interpreter may
        /// still be mutating them. That race is benign — it only influences which
        /// (guarded) specialization tier is chosen, never correctness; a wrong guess is
        /// caught by a runtime guard/deopt.
        /// </summary>
        public static bool BackgroundCompilation { get; set; }

        private static readonly System.Collections.Concurrent.BlockingCollection<Chunk> CompileQueue = new();
        private static System.Threading.Thread worker;
        private static readonly object WorkerGate = new();

        /// <summary>
        /// Queue a hot chunk for background compilation (lazily starting the worker).
        /// The caller has already transitioned the chunk to <c>Compiling</c>.
        /// </summary>
        public static void EnqueueForCompilation(Chunk chunk)
        {
            EnsureWorker();
            CompileQueue.Add(chunk);
        }

        private static void EnsureWorker()
        {
            if (worker != null) return;
            lock (WorkerGate)
            {
                if (worker != null) return;
                worker = new System.Threading.Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "DScript-JIT",
                };
                worker.Start();
            }
        }

        private static void WorkerLoop()
        {
            foreach (var chunk in CompileQueue.GetConsumingEnumerable())
            {
                var compiler = current;
                if (compiler == null) { chunk.JitState = Chunk.JitStatus.Failed; continue; }
                try
                {
                    var compiled = compiler.Compile(chunk);
                    if (compiled != null)
                    {
                        chunk.CompiledDelegate = compiled;          // volatile release
                        chunk.JitState = Chunk.JitStatus.Compiled;
                    }
                    else
                    {
                        chunk.JitState = Chunk.JitStatus.Failed;
                    }
                }
                catch
                {
                    chunk.JitState = Chunk.JitStatus.Failed;
                }
            }
        }
    }
}
