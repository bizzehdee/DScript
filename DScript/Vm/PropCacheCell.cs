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
    /// site. Maintains two slots so a site that alternates between two object shapes
    /// hits the cache instead of thrashing a single entry.
    ///
    /// <b>Shape-keyed path (preferred):</b> When the receiver is a shaped object
    /// (a <see cref="ShapedScriptVar"/> whose <c>_shape</c> is non-null), the cache
    /// stores <c>(ShapeId, SlotIndex)</c>. A hit requires only an integer comparison
    /// against the shape ID — no object-identity check — so all instances that share
    /// the same hidden class hit the same entry. The shaped var's <c>_shapeRoot</c> is
    /// walked <c>SlotIndex</c> steps to reach the target link without a per-instance array.
    ///
    /// <b>Identity-keyed path (fallback):</b> For non-shaped objects (arrays, proxies,
    /// host objects) the old behaviour is preserved: entries are keyed on object
    /// identity and <see cref="ScriptVar.ShapeVersion"/>. Each entry stores the
    /// resolved <see cref="ScriptVarLink"/> directly.
    ///
    /// On a slot-1 hit, that entry is promoted to slot 0 (LRU). On a miss the new
    /// entry takes slot 0 and the previous slot 0 is demoted to slot 1; slot 1 is
    /// evicted.
    /// </summary>
    public sealed class PropCacheCell
    {
        // Shape-keyed slot 0 (ShapeId == 0 means empty / use identity path)
        public int ShapeId0 { get; set; }
        public int SlotIndex0 { get; set; }
        // Shape-keyed slot 1
        public int ShapeId1 { get; set; }
        public int SlotIndex1 { get; set; }

        // Identity-keyed slot 0
        public ScriptVar Object0 { get; set; }
        public int Version0 { get; set; }
        public ScriptVarLink Link0 { get; set; }
        // Identity-keyed slot 1
        public ScriptVar Object1 { get; set; }
        public int Version1 { get; set; }
        public ScriptVarLink Link1 { get; set; }

        /// <summary>
        /// Return the cached link for <paramref name="obj"/> at its current shape,
        /// or null on a miss. Shape-keyed entries are checked first; a slot-1 hit
        /// is promoted to slot 0.
        /// </summary>
        public ScriptVarLink Lookup(ScriptVar obj)
        {
            // Shape-keyed path: one integer comparison, no object identity check.
            var shapeHit = LookupShapeOwn(obj);
            if (shapeHit != null) return shapeHit;

            // Identity-keyed path (non-shaped objects or shape mismatch).
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

        /// <summary>
        /// Shape-keyed lookup only. Returns the cached link when <paramref name="obj"/>
        /// is a shaped object whose current shape matches a cached entry, or null
        /// otherwise. Entries on this path are only ever inserted for <b>own data
        /// properties</b> (see <see cref="Insert"/>), so the result is always an own,
        /// non-accessor property of <paramref name="obj"/> — safe to overwrite in place
        /// for a property assignment without risking a write through the prototype chain.
        /// </summary>
        public ScriptVarLink LookupShapeOwn(ScriptVar obj)
        {
            // Only ShapedScriptVar instances carry shape state; the single isinst here
            // replaces the old per-instance _shape field load.
            if (obj is ShapedScriptVar shaped)
            {
                var shape = shaped._shape;
                if (shape != null)
                {
                    if (ShapeId0 == shape.Id && ShapeId0 > 0)
                        return GetSlot(shaped, SlotIndex0);
                    if (ShapeId1 == shape.Id && ShapeId1 > 0)
                    {
                        // promote slot 1 → slot 0
                        (ShapeId0, SlotIndex0, ShapeId1, SlotIndex1) =
                            (ShapeId1, SlotIndex1, ShapeId0, SlotIndex0);
                        return GetSlot(shaped, SlotIndex0);
                    }
                }
            }
            return null;
        }

        // Slot load: flat array for indices ≥ 2 (O(1)); _shapeRoot walk for indices
        // 0–1 (≤ 1 hop — not worth a heap allocation).
        private static ScriptVarLink GetSlot(ShapedScriptVar shaped, int slotIdx)
        {
            var slots = shaped._slots;
            if (slots != null && (uint)slotIdx < (uint)slots.Length)
                return slots[slotIdx];
            // Flat array not populated for this slot — walk from _shapeRoot (fast for small indices).
            var link = shaped._shapeRoot;
            for (int i = 0; i < slotIdx; i++) link = link?.Next;
            return link;
        }

        /// <summary>
        /// Insert a freshly-resolved entry for <paramref name="obj"/> /
        /// <paramref name="link"/> into slot 0, demoting slot 0 to slot 1.
        /// Prefers the shape-keyed path when the link is an own data property of a
        /// shaped object; falls back to identity-keyed otherwise.
        /// </summary>
        public void Insert(ScriptVar obj, ScriptVarLink link)
        {
            if (link != null && obj is ShapedScriptVar shaped)
            {
                var shape = shaped._shape;
                // Use shape path only for own data properties (no accessors, owned by obj).
                if (shape != null && link.Getter == null && ReferenceEquals(link.Owner, obj)
                    && shape.Slots.TryGetValue(link.Name, out var slotIdx))
                {
                    ShapeId1 = ShapeId0; SlotIndex1 = SlotIndex0;
                    ShapeId0 = shape.Id; SlotIndex0 = slotIdx;
                    return;
                }
            }
            // Identity-keyed path.
            Object1 = Object0; Version1 = Version0; Link1 = Link0;
            Object0 = obj; Version0 = obj.ShapeVersion; Link0 = link;
        }
    }
}
