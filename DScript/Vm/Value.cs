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
    /// <summary>The representation a <see cref="Value"/> currently holds.</summary>
    public enum ValueKind : byte
    {
        Undefined,
        Null,
        /// <summary>A 32-bit integer (DScript booleans are integers too).</summary>
        Int,
        /// <summary>A double-precision float.</summary>
        Double,
        /// <summary>A reference to a <see cref="ScriptVar"/> (string, object, array, function, bigint, …).</summary>
        Ref,
    }

    /// <summary>
    /// A compact, allocation-free value representation for the VM's hot path
    /// (<b>Phase 11 spike — not yet wired into the interpreter</b>). Integers,
    /// doubles, <c>null</c> and <c>undefined</c> are stored inline in this struct, so
    /// passing them around allocates nothing; only non-primitive values carry a
    /// <see cref="ScriptVar"/> reference. Converting back to a <see cref="ScriptVar"/>
    /// (<see cref="ToScriptVar"/>) allocates only when crossing into the object world.
    ///
    /// This is the foundation for migrating the operand stack and arithmetic off
    /// per-value <see cref="ScriptVar"/> allocation. A future optimisation could
    /// NaN-box the payload into a single 8-byte field; the tagged layout here is the
    /// simple, correct starting point.
    /// </summary>
    public readonly struct Value
    {
        public ValueKind Kind { get; }
        // The integer payload is a 64-bit long so the Int kind covers the full DScript
        // integer range (int32 and LargeInt), matching ScriptVar.FromLong on conversion.
        private readonly long intValue;
        private readonly double doubleValue;
        private readonly ScriptVar reference;

        private Value(ValueKind kind, long i, double d, ScriptVar r)
        {
            Kind = kind;
            intValue = i;
            doubleValue = d;
            reference = r;
        }

        // ── factories ───────────────────────────────────────────────────────────

        public static readonly Value Undefined = new(ValueKind.Undefined, 0, 0.0, null);
        public static readonly Value Null = new(ValueKind.Null, 0, 0.0, null);

        public static Value Int(long value) => new(ValueKind.Int, value, 0.0, null);
        public static Value Double(double value) => new(ValueKind.Double, 0, value, null);
        public static Value Bool(bool value) => new(ValueKind.Int, value ? 1 : 0, 0.0, null);

        /// <summary>Wrap a non-primitive <see cref="ScriptVar"/> (string/object/array/…).</summary>
        public static Value Ref(ScriptVar value) => new(ValueKind.Ref, 0, 0.0, value);

        /// <summary>Classify an existing <see cref="ScriptVar"/> into a <see cref="Value"/>.</summary>
        public static Value From(ScriptVar v)
        {
            if (v == null) return Undefined;
            if (v.IsAnyInt) return Int(v.Long);
            if (v.IsDouble) return Double(v.Float);
            if (v.IsNull) return Null;
            if (v.IsUndefined) return Undefined;
            return Ref(v);
        }

        // ── queries ─────────────────────────────────────────────────────────────

        public bool IsUndefined => Kind == ValueKind.Undefined;
        public bool IsNull => Kind == ValueKind.Null;
        public bool IsInt => Kind == ValueKind.Int;
        public bool IsDouble => Kind == ValueKind.Double;
        public bool IsNumber => Kind is ValueKind.Int or ValueKind.Double;
        public bool IsRef => Kind == ValueKind.Ref;

        /// <summary>The 64-bit integer payload (valid when <see cref="IsInt"/>).</summary>
        public long AsLong => intValue;

        /// <summary>The integer payload truncated to int32 (valid when <see cref="IsInt"/>).</summary>
        public int AsInt => (int)intValue;

        /// <summary>The numeric value as a double; an <see cref="ValueKind.Int"/> widens to double.</summary>
        public double AsDouble => Kind == ValueKind.Int ? intValue : doubleValue;

        /// <summary>The referenced <see cref="ScriptVar"/> (valid when <see cref="IsRef"/>).</summary>
        public ScriptVar AsRef => reference;

        // ── conversion back to ScriptVar ──────────────────────────────────────────

        /// <summary>
        /// Materialize this value as a <see cref="ScriptVar"/>. Primitives allocate a
        /// fresh ScriptVar; a <see cref="ValueKind.Ref"/> returns its wrapped instance.
        /// </summary>
        public ScriptVar ToScriptVar() => Kind switch
        {
            ValueKind.Int => ScriptVar.FromLong(intValue),
            ValueKind.Double => ScriptVar.FromDouble(doubleValue),
            ValueKind.Null => ScriptVar.CreateNull(),
            ValueKind.Ref => reference,
            _ => ScriptVar.CreateUndefined(),
        };
    }
}
