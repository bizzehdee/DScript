namespace DScript.Debugger
{
    /// <summary>
    /// Receives debug events from the VM. Implement this interface and attach it
    /// via <see cref="ScriptEngine.AttachDebugger"/> to drive step-by-step
    /// execution, set breakpoints, and inspect state.
    /// </summary>
    public interface IDebugger
    {
        /// <summary>
        /// Called synchronously by the VM whenever execution pauses (at a step
        /// boundary or a registered breakpoint). The returned <see cref="DebugAction"/>
        /// controls how execution resumes. This method may block — for example,
        /// waiting for interactive user input.
        /// </summary>
        DebugAction OnPause(DebugEvent ev);
    }
}
