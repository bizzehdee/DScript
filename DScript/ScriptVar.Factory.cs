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
using System.Runtime.CompilerServices;

namespace DScript
{
    // Integer intern table — range and storage.
    // Range -1..255 covers loop-index 0, boolean 0/1, common small constants,
    // and all return values of recursive numeric functions whose results stay
    // within that window (e.g. fib base cases, status codes, array indices).
    // The table is built once at type-load time and never mutated.
    file static class InternRange
    {
        internal const int Min   = -1;
        internal const int Max   = 255;
        internal const int Count = Max - Min + 1;   // 257
    }

    /// <summary>
    /// Factory methods for creating <see cref="ScriptVar"/> instances.
    /// All code — including the VM core — must create ScriptVar values through
    /// these methods so that three future capabilities can be plugged in at a
    /// single point without touching call sites:
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>Memory profiling</b>: set <see cref="SetAllocationHook"/> to observe
    ///     every allocation with zero overhead when the hook is null.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Value interning</b>: replace the <c>new ScriptVar(...)</c> calls inside
    ///     each factory method with table lookups for common values (small integers,
    ///     empty string, …) to eliminate allocations on hot arithmetic paths.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Object pooling</b>: replace the <c>new ScriptVar(...)</c> calls for
    ///     Object/Array/Function with pool borrow/return to reduce GC pressure.
    ///     Add a paired <c>ScriptVar.Return(v)</c> call wherever the VM currently
    ///     drops an object on the floor.
    ///   </description></item>
    /// </list>
    /// </summary>
    public sealed partial class ScriptVar
    {
        // ── Integer intern table ──────────────────────────────────────────────────
        // Pre-allocated ScriptVar instances for integers in [-1, 255].
        // All entries are marked Flags.Interned so Dispose() is a no-op on them.
        private static readonly ScriptVar[] _internedInts = BuildInternTable();

        private static ScriptVar[] BuildInternTable()
        {
            var table = new ScriptVar[InternRange.Count];
            for (var i = 0; i < InternRange.Count; i++)
            {
                var v = new ScriptVar(InternRange.Min + i);
                v.flags |= Flags.Interned;
                table[i] = v;
            }
            return table;
        }

        // ── Allocation hook ───────────────────────────────────────────────────────
        // Set to a non-null delegate to observe every ScriptVar allocation.
        // When null (the default), Track() inlines to a single well-predicted branch
        // that adds no measurable overhead on the hot dispatch loop.
        //
        // Memory profiler seam: ScriptVar.SetAllocationHook(v => myProfiler.Record(v))
        private static Action<ScriptVar> _allocationHook;

        /// <summary>
        /// Registers a callback invoked for every ScriptVar created through the
        /// factory.  Pass <c>null</c> to stop profiling.
        /// Not thread-safe with respect to concurrent factory calls.
        /// </summary>
        public static void SetAllocationHook(Action<ScriptVar> hook) => _allocationHook = hook;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ScriptVar Track(ScriptVar v)
        {
            _allocationHook?.Invoke(v);
            return v;
        }

        // ── Undefined / Null ──────────────────────────────────────────────────────

        /// <summary>Returns a new Undefined ScriptVar.</summary>
        // Interning seam: return a shared singleton (or a pool of undefineds) once
        // the factory owns lifetime management.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ScriptVar CreateUndefined() => Track(new ScriptVar());

        /// <summary>Returns a new Null ScriptVar.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ScriptVar CreateNull() => Track(new ScriptVar(Flags.Null));

        // ── Primitives ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a ScriptVar for the given integer value.
        /// For values in [-1, 255] a pre-allocated interned instance is returned;
        /// no heap allocation occurs and the instance is never disposed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ScriptVar FromInt(int value)
        {
            if ((uint)(value - InternRange.Min) <= (uint)(InternRange.Max - InternRange.Min))
            {
                // Use an unsigned comparison so the branch is one compare + one array
                // bounds-check, with no branch for negative values.
                var interned = _internedInts[value - InternRange.Min];
                _allocationHook?.Invoke(interned);
                return interned;
            }
            return Track(new ScriptVar(value));
        }

        /// <summary>
        /// Returns a ScriptVar for the given int64 value.
        /// Delegates to <see cref="FromInt"/> when the value fits in int32 (intern-friendly);
        /// allocates a LargeInt ScriptVar only for wider values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ScriptVar FromLong(long value)
        {
            if (value >= int.MinValue && value <= int.MaxValue)
                return FromInt((int)value);
            return Track(new ScriptVar(value));
        }

        /// <summary>Returns a new Double ScriptVar.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ScriptVar FromDouble(double value) => Track(new ScriptVar(value));

        /// <summary>Returns a new String ScriptVar.</summary>
        // Interning seam: short identifier strings could be interned to reduce
        // allocation in property-access-heavy scripts.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ScriptVar FromString(string value) => Track(new ScriptVar(value));

        /// <summary>
        /// Returns a ScriptVar for the given boolean value (0 = false, 1 = true).
        /// Delegates to <see cref="FromInt"/> so that the interned 0 and 1 instances
        /// are reused — every boolean result in a script is allocation-free.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ScriptVar FromBool(bool value) => FromInt(value ? 1 : 0);

        // ── Objects / containers ──────────────────────────────────────────────────

        /// <summary>Returns a new empty plain Object ScriptVar.</summary>
        // Pooling seam: ScriptVar v = _objectPool.TryRent() ?? new ScriptVar(Flags.Object);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ScriptVar CreateObject() => Track(new ScriptVar(Flags.Object));

        /// <summary>Returns a new empty Array ScriptVar.</summary>
        // Pooling seam: same pattern as CreateObject().
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ScriptVar CreateArray() => Track(new ScriptVar(Flags.Array));

        // ── Functions ─────────────────────────────────────────────────────────────

        /// <summary>Returns a new native (C#-backed) Function ScriptVar.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ScriptVar CreateNativeFunction() => Track(new ScriptVar(Flags.Function | Flags.Native));

        /// <summary>Returns a new compiled (VM-executed) Function ScriptVar.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ScriptVar CreateFunction() => Track(new ScriptVar(Flags.Function));

        // ── Internal / low-level ──────────────────────────────────────────────────
        // These cover VM-internal flag combinations (Symbol, Proxy, Regexp, etc.)
        // that external consumers do not need to construct directly.

        /// <summary>
        /// Returns a ScriptVar with the given flags and no value data.
        /// Use for internal VM and compiler types (Symbol, Proxy, Regexp, …).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ScriptVar WithFlags(Flags flags) => Track(new ScriptVar(flags));

        /// <summary>
        /// Parses a string literal into the appropriate typed ScriptVar.
        /// Used by the compiler for integer (decimal/hex/binary/octal), double,
        /// and regexp literals.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ScriptVar ParseLiteral(string value, Flags flags) => Track(new ScriptVar(value, flags));
    }
}
