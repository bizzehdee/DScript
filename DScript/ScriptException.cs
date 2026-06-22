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

using System;
using System.Collections.Generic;
using System.Text;

namespace DScript
{
    /// <summary>
    /// Thrown by the VM for runtime errors (type mismatches, missing variables,
    /// etc.). Carries a script-level stack trace built up as the exception
    /// unwinds through VM call frames.
    /// </summary>
    public class ScriptException : Exception
    {
        private List<(string Source, int Line)> _frames;

        public ScriptException(string msg) : base(msg) { }

        public ScriptException(string msg, Exception innerException) : base(msg, innerException) { }

        /// <summary>
        /// Script-level call stack at the point the error was raised.
        /// Index 0 is the innermost frame; subsequent entries are callers.
        /// </summary>
        public IReadOnlyList<(string Source, int Line)> ScriptStackTrace =>
            (IReadOnlyList<(string, int)>)_frames ?? Array.Empty<(string, int)>();

        internal void PushFrame(string source, int line)
        {
            _frames ??= [];
            _frames.Add((source, line));
        }

        /// <summary>
        /// Returns a human-readable description with the script stack trace
        /// (not the C# implementation stack).
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(nameof(ScriptException)).Append(": ").Append(Message);
            JITException.AppendFrames(sb, _frames);
            return sb.ToString();
        }
    }
}
