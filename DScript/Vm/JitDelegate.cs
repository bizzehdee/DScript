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
    /// A compiled entry point for a hot <see cref="Chunk"/>. Invoked by the VM in
    /// place of the interpreter loop once a chunk has been JIT-compiled.
    /// </summary>
    /// <param name="vm">
    /// The runtime the compiled code runs against. Gives the JIT a handle back into
    /// the interpreter for operations it does not emit inline — call dispatch,
    /// deoptimization, OSR re-entry, and inline-cache misses.
    /// </param>
    /// <param name="args">The positional arguments for this invocation.</param>
    /// <param name="scope">
    /// The scope object the body executes against. Parameters and locals already
    /// bound by the caller are reachable as children of this object.
    /// </param>
    /// <returns>The function's return value.</returns>
    public delegate ScriptVar JitDelegate(VirtualMachine vm, ScriptVar[] args, ScriptVar scope);

    /// <summary>
    /// Contract between the VM and a pluggable JIT back-end. Implementations turn a
    /// hot <see cref="Chunk"/> into an executable <see cref="JitDelegate"/>. The VM
    /// has no compile-time dependency on any concrete compiler, so JIT support is
    /// entirely opt-in (see <see cref="JitRegistry"/>).
    /// </summary>
    public interface IJitCompiler
    {
        /// <summary>
        /// Compile a chunk to a <see cref="JitDelegate"/>, or return <c>null</c> if
        /// this chunk cannot be compiled (the VM then keeps interpreting it).
        /// </summary>
        JitDelegate Compile(Chunk chunk);
    }
}
