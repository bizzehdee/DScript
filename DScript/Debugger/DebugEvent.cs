using System.Collections.Generic;

namespace DScript.Debugger
{
    /// <summary>
    /// Passed to <see cref="IDebugger.OnPause"/> whenever the VM pauses.
    /// </summary>
    public sealed class DebugEvent
    {
        /// <summary>The location where execution paused.</summary>
        public DebugLocation Location { get; }

        /// <summary>
        /// Full call stack at the point of the pause. Index 0 is the innermost
        /// (current) frame; the last entry is the outermost script frame.
        /// </summary>
        public IReadOnlyList<DebugFrame> CallStack { get; }

        internal DebugEvent(DebugLocation location, IReadOnlyList<DebugFrame> callStack)
        {
            Location = location;
            CallStack = callStack;
        }
    }
}
