namespace DScript.Debugger
{
    /// <summary>A specific point in source and bytecode.</summary>
    public sealed class DebugLocation
    {
        /// <summary>The chunk (script or function) name.</summary>
        public string Source { get; }

        /// <summary>1-based source line number; 0 if unavailable.</summary>
        public int Line { get; }

        /// <summary>Bytecode offset of the instruction being paused at.</summary>
        public int BytecodeOffset { get; }

        internal DebugLocation(string source, int line, int offset)
        {
            Source = source;
            Line = line;
            BytecodeOffset = offset;
        }

        public override string ToString() => $"{Source}:{Line}";
    }
}
