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
    /// A <b>bimorphic</b> (2-way) inline cache for one JIT-compiled property-read
    /// site. The compiler bakes one instance per <c>GetProp</c> site; it remembers
    /// up to two recently-seen objects, each with its shape version and resolved
    /// link, so a site that alternates between two objects (or two shapes) hits the
    /// cache instead of thrashing a single slot. Each entry is validated by object
    /// identity and <see cref="ScriptVar.ShapeVersion"/>, so adding/removing a
    /// property invalidates that entry. On a miss the new entry takes slot 0 and the
    /// previous slot 0 is demoted to slot 1 (LRU); the older slot 1 is evicted.
    /// </summary>
    public sealed class PropCacheCell
    {
        public ScriptVar Object0 { get; set; }
        public int Version0 { get; set; }
        public ScriptVarLink Link0 { get; set; }

        public ScriptVar Object1 { get; set; }
        public int Version1 { get; set; }
        public ScriptVarLink Link1 { get; set; }

        /// <summary>Return the cached link for <paramref name="obj"/> at its current
        /// shape, or null on a miss. A hit on slot 1 is promoted to slot 0.</summary>
        public ScriptVarLink Lookup(ScriptVar obj)
        {
            if (ReferenceEquals(Object0, obj) && Version0 == obj.ShapeVersion && Link0 != null)
                return Link0;
            if (ReferenceEquals(Object1, obj) && Version1 == obj.ShapeVersion && Link1 != null)
            {
                // promote slot 1 to slot 0
                (Object0, Version0, Link0, Object1, Version1, Link1) =
                    (Object1, Version1, Link1, Object0, Version0, Link0);
                return Link0;
            }
            return null;
        }

        /// <summary>Insert a freshly-resolved entry into slot 0, demoting slot 0 to slot 1.</summary>
        public void Insert(ScriptVar obj, ScriptVarLink link)
        {
            Object1 = Object0; Version1 = Version0; Link1 = Link0;
            Object0 = obj; Version0 = obj.ShapeVersion; Link0 = link;
        }
    }
}
