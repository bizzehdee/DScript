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
using System.Runtime.CompilerServices;

namespace DScript.Extras.FunctionProviders
{
    /// <summary>
    /// Equality comparer for Map/Set keys implementing JavaScript's SameValueZero
    /// semantics: numbers compare by value (with <c>NaN</c> equal to itself and
    /// <c>+0</c>/<c>-0</c> equal), strings by value, and objects/arrays/functions by
    /// reference. Being hashable, it lets Map/Set use O(1) dictionary lookups instead
    /// of an O(n) linear scan with a per-comparison allocation.
    /// </summary>
    public sealed class ScriptVarKeyComparer : IEqualityComparer<ScriptVar>
    {
        public static readonly ScriptVarKeyComparer Instance = new();

        public bool Equals(ScriptVar a, ScriptVar b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;

            if ((a.IsInt || a.IsDouble) && (b.IsInt || b.IsDouble))
            {
                double da = a.Float, db = b.Float;
                // SameValueZero: NaN equals NaN; otherwise ordinary numeric equality
                // (which already treats +0 and -0 as equal).
                if (double.IsNaN(da)) return double.IsNaN(db);
                return da == db;
            }
            if (a.IsString && b.IsString) return a.String == b.String;
            if (a.IsNull) return b.IsNull;
            if (a.IsUndefined) return b.IsUndefined;

            // Objects, arrays, functions, etc. — identity (already failed above).
            return false;
        }

        public int GetHashCode(ScriptVar v)
        {
            if (v is null) return 0;
            if (v.IsInt || v.IsDouble)
            {
                var d = v.Float;
                if (double.IsNaN(d)) return 0x7FF8_0000;
                if (d == 0.0) return 0; // unify +0 and -0
                return d.GetHashCode();
            }
            if (v.IsString) return v.String.GetHashCode();
            if (v.IsNull) return 1;
            if (v.IsUndefined) return 2;
            return RuntimeHelpers.GetHashCode(v); // reference identity
        }
    }
}
