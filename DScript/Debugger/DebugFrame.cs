using System.Collections.Generic;

namespace DScript.Debugger
{
    /// <summary>
    /// Snapshot of a single call frame at the moment execution paused.
    /// </summary>
    public sealed class DebugFrame
    {
        /// <summary>The function or script name (from <see cref="Vm.Chunk.Name"/>).</summary>
        public string FunctionName { get; }

        /// <summary>The source location within this frame.</summary>
        public DebugLocation Location { get; }

        /// <summary>Variables visible in this frame's immediate scope.</summary>
        public IReadOnlyList<(string Name, ScriptVar Value)> Locals { get; }

        internal DebugFrame(string functionName, DebugLocation location,
                            IReadOnlyList<(string, ScriptVar)> locals)
        {
            FunctionName = functionName;
            Location = location;
            Locals = locals;
        }
    }
}
