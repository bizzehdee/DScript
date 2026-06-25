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

using System.Collections.Generic;

namespace DScript.Extras.FunctionProviders
{
    public sealed class SetObject : INativeContainer
    {
        // Keyed by SameValueZero (primitives by value, objects by reference) so
        // add/has/delete are O(1) instead of O(n) linear scans.
        public HashSet<ScriptVar> Data { get; } = new HashSet<ScriptVar>(ScriptVarKeyComparer.Instance);

        /// <inheritdoc/>
        public int GetSize() => Data.Count;

        /// <summary>
        /// Returns true if a value-equal element is present (JS SameValueZero).
        /// </summary>
        public bool Contains(ScriptVar val) => Data.Contains(val);

        /// <summary>
        /// Adds <paramref name="val"/> (by reference) unless a value-equal element is
        /// already present. Returns true if it was added.
        /// </summary>
        public bool TryAdd(ScriptVar val) => Data.Add(val);

        public ScriptVar ToScriptVar()
        {
            var sv = ScriptVar.CreateObject();
            sv.SetData(this);
            return sv;
        }
    }
}
