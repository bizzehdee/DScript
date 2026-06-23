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
        public HashSet<ScriptVar> Data { get; } = new HashSet<ScriptVar>(ReferenceEqualityComparer.Instance);

        /// <inheritdoc/>
        public int GetSize() => Data.Count;

        /// <summary>
        /// Returns true if any element in the set is value-equal to
        /// <paramref name="val"/> (mirrors the JS <c>===</c> semantics).
        /// </summary>
        public bool Contains(ScriptVar val)
        {
            foreach (var item in Data)
                if (item.Equal(val)) return true;
            return false;
        }

        /// <summary>
        /// Adds <paramref name="val"/> only if no value-equal element is present.
        /// Returns true if the element was added.
        /// </summary>
        public bool TryAdd(ScriptVar val)
        {
            if (Contains(val)) return false;
            Data.Add(val.DeepCopy());
            return true;
        }

        public ScriptVar ToScriptVar()
        {
            var sv = new ScriptVar(ScriptVar.Flags.Object);
            sv.SetData(this);
            return sv;
        }
    }
}
