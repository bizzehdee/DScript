namespace DScript.Debugger
{
    /// <summary>
    /// Controls what the VM does after <see cref="IDebugger.OnPause"/> returns.
    /// </summary>
    public enum DebugAction
    {
        /// <summary>Run until the next breakpoint or the program exits.</summary>
        Continue,

        /// <summary>
        /// Pause at the next source line, stepping INTO called functions.
        /// </summary>
        StepIn,

        /// <summary>
        /// Pause at the next source line in the current call frame, skipping
        /// over function calls.
        /// </summary>
        StepOver,

        /// <summary>
        /// Run until the current function returns, then pause in the caller.
        /// </summary>
        StepOut,
    }
}
