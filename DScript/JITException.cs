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
    /// Thrown when a DScript <c>throw</c> statement executes and the exception
    /// is not caught by a script-level <c>try/catch</c>. Carries the thrown
    /// value and a script-level stack trace built up as the exception unwinds
    /// through VM call frames.
    /// </summary>
    public class JITException : Exception
    {
        private List<(string Source, int Line, int Col)> _frames;

        public JITException() { }

        public JITException(string message) : base(message) { }

        public JITException(ScriptVar varObj)
        {
            VarObj = varObj;
        }

        /// <summary>The DScript value that was thrown, or <c>null</c> for a bare throw.</summary>
        public ScriptVar VarObj { get; }

        /// <summary>
        /// Script-level call stack at the point the exception was thrown.
        /// Index 0 is the innermost frame (where the <c>throw</c> occurred);
        /// subsequent entries are caller frames in order.
        /// </summary>
        public IReadOnlyList<(string Source, int Line, int Col)> ScriptStackTrace =>
            (IReadOnlyList<(string, int, int)>)_frames ?? Array.Empty<(string, int, int)>();

        internal void PushFrame(string source, int line, int col = 0)
        {
            _frames ??= [];
            _frames.Add((source, line, col));
        }

        /// <summary>
        /// Returns a human-readable description with the script stack trace
        /// (not the C# implementation stack).
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(nameof(JITException)).Append(": ");
            sb.Append(VarObj != null ? VarObj.String : Message);
            AppendFrames(sb, _frames);
            return sb.ToString();
        }

        internal static void AppendFrames(StringBuilder sb, List<(string Source, int Line, int Col)> frames)
        {
            if (frames == null) return;
            foreach (var (source, line, col) in frames)
            {
                sb.AppendLine();
                sb.Append("    at ").Append(source);
                if (line > 0)
                {
                    sb.Append(" (line ").Append(line);
                    if (col > 0) sb.Append(", col ").Append(col);
                    sb.Append(')');
                }
            }
        }
    }
}
