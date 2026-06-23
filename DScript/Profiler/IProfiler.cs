namespace DScript.Profiler
{
    /// <summary>
    /// Receives function entry and exit events from the VM. Implement this interface
    /// and attach it via <see cref="ScriptEngine.AttachProfiler"/> to collect
    /// timing data. <see cref="CpuProfiler"/> is the built-in implementation that
    /// produces V8-compatible <c>.cpuprofile</c> JSON.
    /// </summary>
    public interface IProfiler
    {
        /// <summary>
        /// Called synchronously by the VM when a function is entered.
        /// </summary>
        /// <param name="functionName">Declared function name, or <c>"(anonymous)"</c>.</param>
        /// <param name="url">Source name (chunk name) for the function's containing script.</param>
        /// <param name="lineNumber">1-based line number of the first line of the function body; 0 if unknown.</param>
        /// <param name="columnNumber">1-based column number; 0 if unknown.</param>
        void Enter(string functionName, string url, int lineNumber, int columnNumber);

        /// <summary>
        /// Called synchronously by the VM when a function returns (normally or via exception).
        /// Every successful <see cref="Enter"/> call is paired with exactly one <see cref="Leave"/> call.
        /// </summary>
        void Leave();
    }
}
