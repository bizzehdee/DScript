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
using System.Threading;

namespace DScript.Vm
{
    /// <summary>
    /// A hidden class (shape) for plain JS objects. Every object whose properties
    /// are added in the same order shares one Shape instance. The Shape maps each
    /// property name to a slot index so the VM can read/write properties via a
    /// direct array offset instead of a linked-list scan.
    ///
    /// Shapes form a transition tree: starting from <see cref="Empty"/>, each
    /// call to <see cref="Transition"/> either returns an existing child shape
    /// (cache hit) or creates a new one (cache miss). Two objects that receive
    /// properties in the same order always converge on the same leaf Shape, so a
    /// single inline-cache entry keyed on <see cref="Id"/> serves all of them.
    ///
    /// Shapes are immutable once created and globally shared; they are never
    /// removed from the transition tree (shapes are cheap — typically one per
    /// unique property-addition order in the whole program).
    ///
    /// Thread safety: reads (Transition hot path) are lock-free via a volatile
    /// dictionary reference. Writes (new transitions) take a per-shape lock.
    /// Scripts run single-threaded, so the lock is essentially always uncontended.
    /// </summary>
    internal sealed class Shape
    {
        // IDs start at 1; 0 is reserved as the "identity path / uninitialized"
        // sentinel in PropCacheEntry so a zero-initialized struct is always a miss.
        private static int _nextId;

        /// <summary>Root shape — no properties. Every plain object starts here.</summary>
        public static readonly Shape Empty = new Shape(null, null);

        /// <summary>Globally unique identifier. Stable for the lifetime of the process.</summary>
        public int Id { get; }

        /// <summary>
        /// Maps property name → slot index in the owning object's <c>_slots</c> array.
        /// Slot indices are assigned in insertion order (0, 1, 2, …).
        /// </summary>
        public Dictionary<string, int> Slots { get; }

        // Copy-on-write transitions dictionary. Written only under _transLock;
        // the volatile ensures readers always see the latest published version
        // without acquiring the lock.
        private volatile Dictionary<string, Shape> _transitions;
        private readonly object _transLock = new();

        private Shape(Shape parent, string addedName)
        {
            Id = Interlocked.Increment(ref _nextId);
            if (parent == null)
            {
                Slots = new Dictionary<string, int>();
            }
            else
            {
                Slots = new Dictionary<string, int>(parent.Slots);
                if (addedName != null)
                    Slots[addedName] = parent.Slots.Count;
            }
        }

        /// <summary>
        /// Return the child shape reached by adding <paramref name="name"/> to this shape.
        /// Lock-free on the hot (warm-cache) read path; acquires a per-shape lock
        /// only when a new transition must be created.
        /// </summary>
        public Shape Transition(string name)
        {
            // Hot path: lock-free read of the current transitions snapshot.
            var t = _transitions;
            if (t != null && t.TryGetValue(name, out var child))
                return child;

            // Slow path: create a new child shape under the per-shape lock.
            lock (_transLock)
            {
                // Re-read after acquiring the lock (another thread may have raced).
                t = _transitions;
                if (t != null && t.TryGetValue(name, out child))
                    return child;

                child = new Shape(this, name);
                // Publish a new copy of the dictionary atomically so readers
                // outside the lock always see a consistent (if possibly stale) snapshot.
                var next = t == null
                    ? new Dictionary<string, Shape> { [name] = child }
                    : new Dictionary<string, Shape>(t) { [name] = child };
                _transitions = next; // volatile write
                return child;
            }
        }
    }
}
