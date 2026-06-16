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
    /// The runtime payload of a compiled (non-native) function value: its
    /// bytecode body plus the environment it was defined in. Carrying the
    /// defining environment is what gives DScript true lexical closures — the
    /// function resolves free variables against where it was created, not the
    /// call-time stack. Stored in a function <see cref="ScriptVar"/>'s data slot.
    /// </summary>
    public sealed class VmFunction
    {
        public Chunk Body { get; }
        public Environment Captured { get; }

        /// <summary>The function's original source text (for stringify / eval).</summary>
        public string Source => Body.Source;

        public VmFunction(Chunk body, Environment captured)
        {
            Body = body;
            Captured = captured;
        }
    }
}
