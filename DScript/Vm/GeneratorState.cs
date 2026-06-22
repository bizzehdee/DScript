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
    /// State for a stackless generator. Holds the saved instruction pointer,
    /// the latest yielded value, and lifecycle flags so that successive .next()
    /// calls can resume the generator body without a background thread.
    /// </summary>
    internal sealed class GeneratorState
    {
        /// <summary>Instruction pointer to resume from on the next .next() call.</summary>
        public int SavedIp;

        /// <summary>Set to true once the first .next() has been called.</summary>
        public bool Started;

        /// <summary>Set to true when the generator body has completed (Return or Halt).</summary>
        public bool Done;

        /// <summary>The value produced by the most recent yield expression.</summary>
        public ScriptVar YieldedValue;

        /// <summary>
        /// Value to push onto the stack at the start of the next Execute call
        /// (the input argument passed to .next(v)). Consumed and cleared by Execute.
        /// </summary>
        public ScriptVar ResumeValue;
    }
}
