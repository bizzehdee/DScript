﻿/*
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DScript
{
    public sealed partial class ScriptVar : IDisposable
    {
        // Cache compiled regex patterns to avoid recompilation (performance optimization)
        private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();

        // Mapping from JS Unicode property names to .NET \p{} category codes.
        private static readonly System.Collections.Generic.Dictionary<string, string> UnicodePropertyMap
            = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["Letter"] = "L",            ["L"] = "L",
            ["Number"] = "N",            ["N"] = "N",
            ["Mark"] = "M",              ["M"] = "M",
            ["Punctuation"] = "P",       ["P"] = "P",
            ["Symbol"] = "S",            ["S"] = "S",
            ["Separator"] = "Z",         ["Z"] = "Z",
            ["Other"] = "C",             ["C"] = "C",
            ["Uppercase_Letter"] = "Lu", ["Lu"] = "Lu",
            ["Lowercase_Letter"] = "Ll", ["Ll"] = "Ll",
            ["Titlecase_Letter"] = "Lt", ["Lt"] = "Lt",
            ["Modifier_Letter"] = "Lm",  ["Lm"] = "Lm",
            ["Other_Letter"] = "Lo",     ["Lo"] = "Lo",
            ["Nonspacing_Mark"] = "Mn",  ["Mn"] = "Mn",
            ["Spacing_Mark"] = "Mc",     ["Mc"] = "Mc",
            ["Enclosing_Mark"] = "Me",   ["Me"] = "Me",
            ["Decimal_Number"] = "Nd",   ["Nd"] = "Nd",
            ["Letter_Number"] = "Nl",    ["Nl"] = "Nl",
            ["Other_Number"] = "No",     ["No"] = "No",
            ["Connector_Punctuation"] = "Pc", ["Pc"] = "Pc",
            ["Dash_Punctuation"] = "Pd", ["Pd"] = "Pd",
            ["Open_Punctuation"] = "Ps", ["Ps"] = "Ps",
            ["Close_Punctuation"] = "Pe",["Pe"] = "Pe",
            ["Initial_Punctuation"] = "Pi",["Pi"] = "Pi",
            ["Final_Punctuation"] = "Pf",["Pf"] = "Pf",
            ["Other_Punctuation"] = "Po",["Po"] = "Po",
            ["Math_Symbol"] = "Sm",      ["Sm"] = "Sm",
            ["Currency_Symbol"] = "Sc",  ["Sc"] = "Sc",
            ["Modifier_Symbol"] = "Sk",  ["Sk"] = "Sk",
            ["Other_Symbol"] = "So",     ["So"] = "So",
            ["Space_Separator"] = "Zs",  ["Zs"] = "Zs",
            ["Line_Separator"] = "Zl",   ["Zl"] = "Zl",
            ["Paragraph_Separator"] = "Zp", ["Zp"] = "Zp",
            ["Space"] = "Zs",
            ["ASCII"] = "IsBasicLatin",
            ["Alpha"] = "L",
        };

        /// <summary>
        /// Translates JS Unicode property escapes (\p{Name}) to .NET equivalents.
        /// Call this when the u or v flag is present.
        /// </summary>
        public static string TranslateUnicodeProperties(string pattern)
        {
            return Regex.Replace(pattern, @"\\([pP])\{([^}]+)\}", m =>
            {
                var isLower = m.Groups[1].Value == "p";
                var name = m.Groups[2].Value;
                string dotNetName;
                if (name.StartsWith("Script=", System.StringComparison.OrdinalIgnoreCase))
                    dotNetName = string.Concat("Is", name.AsSpan(7));
                else if (name.StartsWith("Script_Extensions=", System.StringComparison.OrdinalIgnoreCase))
                    dotNetName = string.Concat("Is", name.AsSpan(18));
                else if (UnicodePropertyMap.TryGetValue(name, out var mapped))
                    dotNetName = mapped;
                else
                    dotNetName = name; // pass through unchanged; .NET may or may not support it
                return isLower ? $"\\p{{{dotNetName}}}" : $"\\P{{{dotNetName}}}";
            });
        }

        // RegexOptions.Compiled emits IL at runtime via Reflection.Emit, which
        // Native AOT cannot do (the runtime silently ignores it there). Script
        // regex patterns are supplied at runtime, so the [GeneratedRegex] source
        // generator — which requires a compile-time-constant pattern — does not
        // apply. Instead, only request Compiled when dynamic code is actually
        // supported: under JIT this keeps the fast compiled engine for hot reuse,
        // and under AOT it avoids requesting a no-op option.
        private static readonly RegexOptions CompiledIfSupported =
            RuntimeFeature.IsDynamicCodeCompiled ? RegexOptions.Compiled : RegexOptions.None;

        // Cache of stringified small array indices so element access does not
        // allocate a name string per get/set. Out-of-range indices fall back.
        private static readonly string[] IndexNames = CreateIndexNames();

        private static string[] CreateIndexNames()
        {
            var names = new string[1024];
            for (var i = 0; i < names.Length; i++)
            {
                names[i] = i.ToString(CultureInfo.InvariantCulture);
            }
            return names;
        }

        // Internal so the VM can reuse the cached index-name strings for integer
        // [] keys instead of allocating a fresh Int.ToString() on every access.
        internal static string IndexName(int idx)
        {
            return idx >= 0 && idx < IndexNames.Length
                ? IndexNames[idx]
                : idx.ToString(CultureInfo.InvariantCulture);
        }

        #region IDisposable
        private bool disposed;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            // Interned objects are permanent — they must never be disposed or have
            // their children removed, as they are shared across the entire engine.
            if (disposed || (flags & Flags.Interned) != 0) return;

            if (disposing)
            {
                RemoveAllChildren();
            }

            // Indicate that the instance has been disposed.
            disposed = true;
        }
        #endregion

        private int refs;
        private Flags flags;
        private object scriptData;
        private int intData;
        private double doubleData;
        private int cachedArrayLength = -1;  // -1 means not cached

        private static int _symbolCounter;

        public static ScriptVar CreateSymbol(string description = null)
        {
            var sym = new ScriptVar();
            sym.flags = Flags.Symbol;
            sym.intData = System.Threading.Interlocked.Increment(ref _symbolCounter);
            sym.scriptData = description;
            return sym;
        }

        public string GetSymbolKey() => IsSymbol ? $"@@symbol:{intData}" : null;

        public string GetSymbolDescription() => IsSymbol ? scriptData as string : null;

        // Fast-path array iterator: avoids per-iteration object allocation in for..of
        // loops over arrays. Uses scriptData (unused for Object-typed vars) to hold the
        // source array and intData (unused for Object-typed vars) as the current index.
        internal bool IsNativeArrayIterator => (flags & Flags.NativeArrayIterator) != 0;
        internal ScriptVar NativeIterArray { get => (ScriptVar)scriptData; set => scriptData = value; }
        internal int NativeIterIndex { get => intData; set => intData = value; }

        internal static ScriptVar CreateNativeArrayIterator(ScriptVar array)
        {
            var iter = new ScriptVar(Flags.Object | Flags.NativeArrayIterator);
            iter.scriptData = array;
            iter.intData = 0;
            return iter;
        }

        // Native callbacks are rare (registered once per built-in function) but the
        // two dedicated fields they needed used to sit on every ScriptVar — including
        // the millions of short-lived primitives the VM allocates. They are folded
        // into a small holder stored in scriptData (unused for native functions
        // otherwise), shrinking every ScriptVar by 16 bytes.
        private sealed class NativeCallback
        {
            public readonly ScriptEngine.ScriptCallbackCB Callback;
            public readonly object UserData;
            public NativeCallback(ScriptEngine.ScriptCallbackCB callback, object userData)
            {
                Callback = callback;
                UserData = userData;
            }
        }

        // O(1) name -> child lookup, rebuilt lazily from the child linked list.
        // The linked list remains the source of truth for ordering (for...in,
        // JSON, array length); this index only accelerates FindChild. It is only
        // used once a var has more than LinearScanThreshold children, since for
        // small scopes (e.g. function call frames) a linear scan is faster and
        // avoids allocating/populating a dictionary per call.
        private const int LinearScanThreshold = 8;
        private Dictionary<string, ScriptVarLink> childIndex;
        private bool childIndexValid;
        private int childCount;

        // Incremented whenever the object's property set changes (child added or
        // removed). Used by the VM's inline property cache to validate cached lookups.
        private int shapeVersion;

        /// <summary>
        /// Monotonically increasing version counter that increments whenever a
        /// property is added to or removed from this object. A cached property
        /// lookup is valid only while this matches the version at cache-fill time.
        /// </summary>
        public int ShapeVersion => shapeVersion;

        public const string ReturnVarName = "return";
        public const string PrototypeClassName = "prototype";

        [Flags]
        public enum Flags
        {
            Undefined = 0,
            Function = 1,
            Object = 2,
            Array = 4,
            Double = 8,
            Integer = 16,
            String = 32,
            Null = 64,
            Native = 128,
            Regexp = 256,
            Symbol = 512,
            BigInt = 1024,
            Proxy = 2048,
            NonExtensible = 4096,   // Object.preventExtensions
            Sealed = 8192,          // Object.seal
            Frozen = 16384,         // Object.freeze
            NativeArrayIterator = 32768, // Fast-path iterator for arrays (no per-iteration allocation)
            Interned = 65536,           // Value is in the factory intern table — must not be disposed or mutated
            NumericMask = Null | Double | Integer,
            VarTypeMask =  Double | Integer | String | Function | Object | Array | Null | Regexp | Symbol | BigInt | Proxy
        }

        public ScriptVarLink FirstChild { get; set; }
        public ScriptVarLink LastChild { get; set; }

        // The CLR zero-initializes every freshly allocated object, so the
        // constructors only need to set the fields that differ from default
        // (flags and the relevant value field). They deliberately avoid the old
        // Init() call, which re-zeroed already-zero fields on every allocation —
        // a measurable cost given how many short-lived ScriptVars the VM creates.
        private ScriptVar()
        {
            // Undefined == 0, so a zero-initialized instance is already correct.
        }

        /// <summary>
        /// Create a typed but value-less ScriptVar (Object, Array, Function, Null,
        /// Undefined, ...). Unlike <see cref="ScriptVar(string, Flags)"/> this skips
        /// the literal-parsing branches and their flag tests, which is worthwhile on
        /// the hot paths that build call frames and aggregates. Must not be used
        /// with Integer/Double/Regexp flags, which require a value to parse.
        /// </summary>
        private ScriptVar(Flags flags)
        {
            this.flags = flags;
        }

        private ScriptVar(int val)
        {
            flags = Flags.Integer;
            intData = val;
        }

        private ScriptVar(double val)
        {
            flags = Flags.Double;
            doubleData = val;
        }

        private ScriptVar(string val)
        {
            flags = Flags.String;
            scriptData = val;
        }

        private ScriptVar(bool val)
        {
            flags = Flags.Integer;
            intData = val ? 1 : 0;
        }

        // Parse an integer literal token, preferring an int32 (DScript's small-int
        // fast path) but falling back to a double when the value exceeds int32. JS
        // has no integer type, so large literals like 1736855917056 or 0xFFFFFFFF are
        // doubles, not overflow errors. Returns true with intValue when it fits,
        // false with doubleValue otherwise.
        internal static bool TryParseIntegerLiteral(string val, out int intValue, out double doubleValue)
        {
            intValue = 0;
            doubleValue = 0;

            GetLiteralRadix(val, out var radix, out var digits);

            if (radix == 10)
            {
                if (long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lng))
                {
                    if (lng >= int.MinValue && lng <= int.MaxValue) { intValue = (int)lng; return true; }
                    doubleValue = lng;
                    return false;
                }
                // Beyond long range — parse as double (very large literals, may be Infinity).
                doubleValue = double.Parse(digits, NumberStyles.Float, CultureInfo.InvariantCulture);
                return false;
            }

            // Non-decimal literal: accumulate the unsigned magnitude. JS treats these
            // as positive numbers, so 0xFFFFFFFF is 4294967295, not -1 (a signed
            // 32-bit wrap). Stop as soon as it leaves int32 and fall back to a double.
            long acc = 0;
            foreach (var c in digits)
            {
                acc = acc * radix + DigitValue(c);
                if (acc > int.MaxValue)
                {
                    doubleValue = IntegerLiteralToDouble(val);
                    return false;
                }
            }
            intValue = (int)acc;
            return true;
        }

        private static int DigitValue(char c) =>
            c >= '0' && c <= '9' ? c - '0'
          : c >= 'a' && c <= 'f' ? c - 'a' + 10
          : c >= 'A' && c <= 'F' ? c - 'A' + 10
          : 0;

        // Classify an integer literal token into a radix and its digit string.
        private static void GetLiteralRadix(string val, out int radix, out string digits)
        {
            if (val.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) { radix = 16; digits = val.Substring(2); }
            else if (val.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) { radix = 2; digits = val.Substring(2); }
            else if (val.StartsWith("0o", StringComparison.OrdinalIgnoreCase)) { radix = 8; digits = val.Substring(2); }
            else if (val.Length > 1 && val[0] == '0') { radix = 8; digits = val; } // legacy octal
            else { radix = 10; digits = val; }
        }

        // Exact magnitude of an integer literal (any radix) as a double, used when it
        // overflows int32. BigInteger keeps non-decimal forms exact before the cast.
        private static double IntegerLiteralToDouble(string val)
        {
            GetLiteralRadix(val, out var radix, out var digits);
            if (radix == 10)
                return double.Parse(digits, NumberStyles.Float, CultureInfo.InvariantCulture);

            // Exact magnitude before the cast (non-decimal forms can exceed long).
            var acc = BigInteger.Zero;
            foreach (var c in digits)
                acc = acc * radix + DigitValue(c);
            return (double)acc;
        }

        private ScriptVar(string val, Flags flags)
        {
            this.flags = flags;
            if (flags.HasFlag(Flags.Integer))
            {
                if (val.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    intData = Convert.ToInt32(val, 16);
                }
                else if (val.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
                {
                    intData = Convert.ToInt32(val.Substring(2), 2);
                }
                else if (val.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
                {
                    intData = Convert.ToInt32(val.Substring(2), 8);
                }
                else if(val.StartsWith("0", StringComparison.OrdinalIgnoreCase))
                {
                    intData = Convert.ToInt32(val, 8);
                }
                else
                {
                    intData = int.Parse(val);
                }
            }
            else if (flags.HasFlag(Flags.Double))
            {
                if (!double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out doubleData))
                {
                    doubleData = Convert.ToDouble(val, CultureInfo.InvariantCulture);
                }
            }
            else if(flags.HasFlag(Flags.Regexp))
            {
                var lastIndexOf = val.LastIndexOf('/');
                
                if (lastIndexOf <= 0) return;
                
                var regexStr = val.Substring(1, lastIndexOf - 1);
                var opts = val.Substring(lastIndexOf + 1);

                var regexOpts = RegexOptions.ECMAScript | CompiledIfSupported;

                var hasIndices = false;
                var hasUnicode = false;
                foreach (var c in opts)
                {
                    switch (c)
                    {
                        case 'i':
                            regexOpts |= RegexOptions.IgnoreCase;
                            break;
                        case 'm':
                            regexOpts |= RegexOptions.Multiline;
                            break;
                        case 's':
                            regexOpts |= RegexOptions.Singleline;
                            break;
                        case 'd':
                            hasIndices = true;
                            break;
                        case 'u':
                        case 'v':
                            hasUnicode = true;
                            break;
                    }
                }
                if (hasIndices) intData |= 1;
                if (hasUnicode)
                {
                    regexOpts &= ~RegexOptions.ECMAScript;
                    regexOpts |= RegexOptions.CultureInvariant;
                    regexStr = TranslateUnicodeProperties(regexStr);
                }

                // Use cache key combining pattern and options
                var cacheKey = $"{regexStr}|{regexOpts}";
                scriptData = RegexCache.GetOrAdd(cacheKey, _ => new Regex(regexStr, regexOpts));
            }
            else
            {
                scriptData = val;
            }
        }

        public bool IsInt => (flags & Flags.Integer) != 0;

        public bool IsDouble => (flags & Flags.Double) != 0;

        public bool IsString => (flags & Flags.String) != 0;

        public bool RegexHasIndices =>
            (IsRegexp && (intData & 1) != 0) ||
            (FindChild("hasIndices")?.Var.Bool == true);

        public bool IsNumeric => (flags & Flags.NumericMask) != 0;

        public bool IsFunction => (flags & Flags.Function) != 0;

        public bool IsObject => (flags & Flags.Object) != 0;

        public bool IsArray => (flags & Flags.Array) != 0;

        public bool IsNative => (flags & Flags.Native) != 0;

        public bool IsUndefined => (flags & Flags.VarTypeMask) == Flags.Undefined;

        public bool IsNull => (flags & Flags.Null) != 0;
        
        public bool IsRegexp => (flags & Flags.Regexp) != 0;

        public bool IsSymbol => (flags & Flags.Symbol) != 0;

        public bool IsBigInt => (flags & Flags.BigInt) != 0;

        public bool IsProxy => (flags & Flags.Proxy) != 0;

        public bool IsFrozen => (flags & Flags.Frozen) != 0;
        public bool IsSealed => (flags & (Flags.Sealed | Flags.Frozen)) != 0;
        public bool IsExtensible => (flags & (Flags.NonExtensible | Flags.Sealed | Flags.Frozen)) == 0;

        /// <summary>Increments the shape version so the VM's inline property cache re-validates.</summary>
        internal void BumpShapeVersion() => shapeVersion++;

        public void FreezeSelf()
        {
            flags |= Flags.Frozen;
            var link = FirstChild;
            while (link != null)
            {
                link.Writable = false;
                link.Configurable = false;
                link = link.Next;
            }
        }

        public void SealSelf()
        {
            flags |= Flags.Sealed;
            var link = FirstChild;
            while (link != null)
            {
                link.Configurable = false;
                link = link.Next;
            }
        }

        public void PreventExtensionsSelf()
        {
            flags |= Flags.NonExtensible;
        }

        public static ScriptVar CreateProxy(ScriptVar target, ScriptVar handler)
        {
            var v = new ScriptVar(Flags.Proxy | Flags.Object);
            v.AddChild("[[ProxyTarget]]", target);
            v.AddChild("[[ProxyHandler]]", handler);
            return v;
        }

        public ScriptVar ProxyTarget => FindChild("[[ProxyTarget]]")?.Var;
        public ScriptVar ProxyHandler => FindChild("[[ProxyHandler]]")?.Var;

        public BigInteger BigIntData
        {
            get => scriptData is BigInteger b ? b : BigInteger.Zero;
            set { scriptData = value; }
        }

        public static ScriptVar CreateBigInt(BigInteger value)
        {
            var v = new ScriptVar();
            v.flags = Flags.BigInt;
            v.scriptData = value;
            return v;
        }

        public bool IsBasic => FirstChild == null;

        public ScriptVar this[string index] => GetParameter(index);

        public int Int
        {
            get => GetInt();
            set => SetInt(value);
        }

        public double Float
        {
            get => GetDouble();
            set => SetDouble(value);
        }

        private int GetInt()
        {
            if (IsInt) return intData;
            if (IsNull) return 0;
            if (IsUndefined) return 0;
            if (IsDouble) return (int)doubleData;
            return 0;
        }

        public bool Bool
        {
            // JavaScript ToBoolean: objects/arrays/functions are always truthy, a
            // non-empty string is truthy, numbers are truthy unless 0 / NaN, and
            // null/undefined are falsy. (Booleans are stored as the integers 0/1.)
            get
            {
                if (IsInt) return intData != 0;
                if (IsDouble) return doubleData != 0 && !double.IsNaN(doubleData);
                if (IsString) return GetString().Length != 0;
                if (IsBigInt) return !BigIntData.IsZero;
                if (IsUndefined || IsNull) return false;
                return true; // object, array, function, regexp, symbol, proxy
            }
            set => Int = value ? 1 : 0;
        }

        private double GetDouble()
        {
            if (IsDouble) return doubleData;
            if (IsInt) return Int;
            
            return 0;
        }

        public string String
        {
            get => GetString();
            set => SetString(value);
        }

        private string GetString()
        {
            if (IsInt)
            {
                return Int.ToString(CultureInfo.InvariantCulture);
            }
            if (IsDouble)
            {
                return FormatDouble(Float);
            }
            if (IsNull) return "null";
            if (IsUndefined) return "undefined";
            if (IsSymbol)
            {
                var desc = scriptData as string;
                return desc != null ? $"Symbol({desc})" : "Symbol()";
            }
            if (IsBigInt) return BigIntData.ToString(CultureInfo.InvariantCulture);

            if (scriptData is Vm.VmFunction fn) return fn.Source;

            if (scriptData is StringRope rope)
            {
                var flat = FlattenRope(rope);
                scriptData = flat; // collapse the rope so later reads are O(1)
                return flat;
            }

            return scriptData as string ?? string.Empty;
        }

        // Render a double using the ECMAScript Number::toString algorithm rather than
        // .NET's default, which switches to exponential notation around 1e15. JS only
        // uses exponential when the decimal point position n is > 21 or <= -6; in the
        // common range it prints full fixed-point digits (e.g. 1.73e17 -> the full
        // 18-digit integer, matching V8).
        internal static string FormatDouble(double d)
        {
            if (double.IsNaN(d)) return "NaN";
            if (double.IsPositiveInfinity(d)) return "Infinity";
            if (double.IsNegativeInfinity(d)) return "-Infinity";
            if (d == 0.0) return "0";

            var negative = d < 0;
            // "R" gives the shortest round-trippable decimal representation.
            var str = System.Math.Abs(d).ToString("R", CultureInfo.InvariantCulture);

            // Split off an exponent if present (e.g. "1.736E+17", "1E-07").
            var exp = 0;
            var ePos = str.IndexOf('E');
            if (ePos >= 0)
            {
                exp = int.Parse(str.AsSpan(ePos + 1), CultureInfo.InvariantCulture);
                str = str[..ePos];
            }

            // Recover the raw digit string and n = position of the decimal point.
            string digits;
            int n; // number of digits to the left of the decimal point
            var dot = str.IndexOf('.');
            if (dot >= 0)
            {
                var intPart = str.Substring(0, dot);
                var fracPart = str.Substring(dot + 1);
                digits = intPart + fracPart;
                n = intPart.Length;
            }
            else
            {
                digits = str;
                n = str.Length;
            }
            n += exp;

            // Strip leading zeros (each one shifts the decimal point left).
            var lead = 0;
            while (lead < digits.Length - 1 && digits[lead] == '0') { lead++; n--; }
            digits = digits.Substring(lead);
            // Strip trailing zeros — they are recovered via n (no value change).
            digits = digits.TrimEnd('0');
            if (digits.Length == 0) digits = "0";

            var k = digits.Length; // count of significant digits
            var sb = new StringBuilder();
            if (negative) sb.Append('-');

            if (k <= n && n <= 21)
            {
                // Integer with trailing zeros: digits then (n-k) zeros.
                sb.Append(digits);
                sb.Append('0', n - k);
            }
            else if (0 < n && n <= 21)
            {
                // Decimal point falls inside the digits.
                sb.Append(digits, 0, n);
                sb.Append('.');
                sb.Append(digits, n, k - n);
            }
            else if (-6 < n && n <= 0)
            {
                // Small magnitude: 0.00…digits.
                sb.Append("0.");
                sb.Append('0', -n);
                sb.Append(digits);
            }
            else
            {
                // Exponential notation (n > 21 or n <= -6).
                sb.Append(digits[0]);
                if (k > 1)
                {
                    sb.Append('.');
                    sb.Append(digits, 1, k - 1);
                }
                var e = n - 1;
                sb.Append('e');
                sb.Append(e >= 0 ? '+' : '-');
                sb.Append(System.Math.Abs(e).ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        // A lazily-concatenated ("cons") string: `a + b` builds one of these in O(1)
        // instead of copying, so repeated concatenation (e.g. s += x in a loop) is O(n)
        // overall rather than O(n^2). It is flattened to a real string on first read.
        // Left is a string or a nested StringRope; Right is always a flat string.
        private sealed class StringRope
        {
            public object Left;
            public string Right;
            public int Length;
        }

        // Iteratively flatten a left-leaning rope (no recursion — chains can be long).
        private static string FlattenRope(StringRope rope)
        {
            var sb = new System.Text.StringBuilder(rope.Length);
            var rights = new System.Collections.Generic.Stack<string>();
            object node = rope;
            while (node is StringRope sr) { rights.Push(sr.Right); node = sr.Left; }
            sb.Append((string)node);
            while (rights.Count > 0) sb.Append(rights.Pop());
            return sb.ToString();
        }

        // Concatenate two values as strings without materialising the left side, so a
        // chain of concatenations stays O(1) per step.
        private static ScriptVar ConcatStrings(ScriptVar a, ScriptVar b)
        {
            var right = b.GetString();
            object leftContent;
            int leftLen;
            if (a.IsString && a.scriptData is StringRope lr) { leftContent = lr; leftLen = lr.Length; }
            else if (a.IsString && a.scriptData is string ls)  { leftContent = ls; leftLen = ls.Length; }
            else { var s = a.GetString(); leftContent = s; leftLen = s.Length; }

            var sv = new ScriptVar(string.Empty, Flags.String);
            sv.scriptData = new StringRope { Left = leftContent, Right = right, Length = leftLen + right.Length };
            return sv;
        }

        public object GetData()
        {
            if (IsNull) return null;
            if (IsUndefined) return null;
            if (IsInt) return intData;
            if (IsDouble) return doubleData;

            return scriptData;
        }

        public string GetObjectType()
        {
            if (IsInt || IsDouble)
            {
                return "number";
            }
            if (IsObject || IsArray) return "object";
            if (IsFunction) return "function";
            if (IsString) return "string";
            if (IsNull) return "null";
            if (IsSymbol) return "symbol";
            if (IsBigInt) return "bigint";

            return "undefined";
        }

        private void SetInt(int num)
        {
            if ((flags & Flags.Interned) != 0)
                throw new InvalidOperationException(
                    $"Cannot mutate interned ScriptVar (value={intData}): use ScriptVar.FromInt({num}) and assign the result via ScriptVarLink.ReplaceWith");
            flags = (flags & ~Flags.VarTypeMask) | Flags.Integer;
            intData = num;
            doubleData = 0;
            scriptData = null;
        }

        private void SetDouble(double num)
        {
            if ((flags & Flags.Interned) != 0)
                throw new InvalidOperationException(
                    $"Cannot mutate interned ScriptVar (value={intData}): use ScriptVar.FromDouble({num}) and assign the result via ScriptVarLink.ReplaceWith");
            flags = (flags & ~Flags.VarTypeMask) | Flags.Double;
            intData = 0;
            doubleData = num;
            scriptData = null;
        }

        private void SetString(string str)
        {
            if ((flags & Flags.Interned) != 0)
                throw new InvalidOperationException(
                    $"Cannot mutate interned ScriptVar (value={intData}): use ScriptVar.FromString and assign the result via ScriptVarLink.ReplaceWith");
            flags = (flags & ~Flags.VarTypeMask) | Flags.String;
            intData = 0;
            doubleData = 0;
            scriptData = str;
        }

        public void SetUndefined()
        {
            if ((flags & Flags.Interned) != 0)
                throw new InvalidOperationException(
                    $"Cannot mutate interned ScriptVar (value={intData}) via SetUndefined");
            flags = (flags & ~Flags.VarTypeMask) | Flags.Undefined;
            intData = 0;
            doubleData = 0;
            scriptData = null;
            RemoveAllChildren();
        }

        public void SetArray()
        {
            if ((flags & Flags.Interned) != 0)
                throw new InvalidOperationException(
                    $"Cannot mutate interned ScriptVar (value={intData}) via SetArray");
            flags = (flags & ~Flags.VarTypeMask) | Flags.Array;
            intData = 0;
            doubleData = 0;
            scriptData = null;
            RemoveAllChildren();
            cachedArrayLength = 0;  // Empty array has length 0
        }

        public ScriptVar Ref()
        {
            refs++;
            return this;
        }

        public void UnRef()
        {
            if (refs <= 0) throw new ScriptException("No refs to unref");

            if ((--refs) == 0)
            {
                Dispose(true);
            }
        }

        public int GetRefs()
        {
            return refs;
        }

        public ScriptVarLink FindChild(string childName)
        {
            if (FirstChild == null) return null;

            // small scopes: a linear scan beats hashing and allocates nothing
            if (childCount <= LinearScanThreshold)
            {
                var v = FirstChild;
                while (v != null)
                {
                    if (v.Name == childName) return v;
                    v = v.Next;
                }
                return null;
            }

            if (!childIndexValid) RebuildChildIndex();

            return childIndex.TryGetValue(childName, out var link) ? link : null;
        }

        private void RebuildChildIndex()
        {
            childIndex ??= new Dictionary<string, ScriptVarLink>();
            childIndex.Clear();

            var v = FirstChild;
            while (v != null)
            {
                // first occurrence wins, matching the previous linear scan
                if (!childIndex.ContainsKey(v.Name))
                {
                    childIndex[v.Name] = v;
                }
                v = v.Next;
            }

            childIndexValid = true;
        }

        // Marks the lookup index stale; it is rebuilt on the next FindChild.
        internal void InvalidateChildIndex()
        {
            childIndexValid = false;
        }

        public ScriptVarLink FindChildOrCreate(string childName, Flags varFlags = Flags.Undefined, bool readOnly = false)
        {
            var l = FindChild(childName);
            if (l != null) return l;

            return AddChild(childName, new ScriptVar(varFlags), readOnly);
        }

        public ScriptVarLink FindChildOrCreateByPath(string path)
        {
            var p = path.IndexOf('.');
            if (p < 0) return FindChildOrCreate(path);

            var head = path.Substring(0, p);
            var tail = path.Substring(p + 1);

            return FindChildOrCreate(head, Flags.Object).Var.FindChildOrCreateByPath(tail);
        }

        public ScriptVarLink AddChild(string childName, ScriptVar child, bool readOnly = false)
        {
            if (IsUndefined)
            {
                flags = Flags.Object;
            }

            var c = child ?? new ScriptVar();

            var link = ScriptVarLink.Rent(c, childName, readOnly);
            link.Owned = true;
            link.Owner = this;

            if (LastChild != null)
            {
                LastChild.Next = link;
                link.Prev = LastChild;
            }
            else
            {
                FirstChild = link;
            }

            LastChild = link;
            childCount++;
            shapeVersion++;

            // keep the lookup index in sync while it is valid (first occurrence wins)
            if (childIndexValid && !childIndex.ContainsKey(childName))
            {
                childIndex[childName] = link;
            }

            // Invalidate array length cache if this is an array, as a numeric
            // child may have been added directly (bypassing SetArrayIndex)
            if (IsArray)
            {
                cachedArrayLength = -1;
            }

            return link;
        }

        public ScriptVarLink AddChildNoDup(string childName, ScriptVar child)
        {
            var c = child ?? new ScriptVar();

            var v = FindChild(childName);
            if (v != null)
            {
                v.ReplaceWith(c);
            }
            else
            {
                v = AddChild(childName, c);
            }

            return v;
        }

        public void RemoveChild(ScriptVar child)
        {
            var link = FirstChild;

            while (link != null)
            {
                if (link.Var == child) break;

                link = link.Next;
            }

            RemoveLink(link);
        }

        public void RemoveLink(ScriptVarLink link)
        {
            if (link == null) return;

            if (link.Next != null)
            {
                link.Next.Prev = link.Prev;
            }
            if (link.Prev != null)
            {
                link.Prev.Next = link.Next;
            }
            if (LastChild == link)
            {
                LastChild = link.Prev;
            }
            if (FirstChild == link)
            {
                FirstChild = link.Next;
            }

            childCount--;
            shapeVersion++;

            // removing a child may expose a shadowed duplicate name; rebuild lazily
            childIndexValid = false;

            // Invalidate array length cache if this is an array
            if (IsArray)
            {
                cachedArrayLength = -1;
            }
        }

        public void RemoveAllChildren()
        {
            var c = FirstChild;

            while (c != null)
            {
                var t = c.Next;
                c.Dispose();
                ScriptVarLink.Return(c);
                c = t;
            }

            FirstChild = null;
            LastChild = null;

            childIndex?.Clear();
            childIndexValid = false;
            childCount = 0;

            // Invalidate array length cache
            if (IsArray)
            {
                cachedArrayLength = 0;  // Empty array has length 0
            }
        }

        // Detach all children WITHOUT disposing them, so this var can be reused
        // as a fresh empty scope. Unlike RemoveAllChildren this does not UnRef the
        // children: it mirrors how an abandoned call frame is reclaimed today (the
        // links are simply dropped for the GC), which keeps any value that escaped
        // the frame (e.g. a returned parameter) alive via its existing references.
        // We traverse the list (O(n)) so that each link can be returned to the pool.
        internal void ResetForReuse()
        {
            var c = FirstChild;
            while (c != null)
            {
                var t = c.Next;
                ScriptVarLink.Return(c); // no UnRef — see comment above
                c = t;
            }
            FirstChild = null;
            LastChild = null;
            childIndex?.Clear();
            childIndexValid = false;
            childCount = 0;
        }

        public ScriptVar ReturnVar
        {
            get => GetParameter(ReturnVarName);
            set => FindChildOrCreate(ReturnVarName).ReplaceWith(value);
        }

        public ScriptVar GetParameter(string name)
        {
            return FindChildOrCreate(name).Var;
        }

        public ScriptVar GetArrayIndex(int idx)
        {
            var link = FindChild(IndexName(idx));
            if (link != null) return link.Var;

            return new ScriptVar(Flags.Null);
        }

        public void SetArrayIndex(int idx, ScriptVar value)
        {
            var name = IndexName(idx);
            var link = FindChild(name);

            if (link != null)
            {
                if (value.IsUndefined)
                {
                    RemoveLink(link);
                    cachedArrayLength = -1;  // Invalidate cache on removal
                }
                else
                {
                    link.ReplaceWith(value);
                    // No need to invalidate - index already exists
                }
            }
            else
            {
                if (value.IsUndefined) return;

                // AddChild invalidates the length cache (it can't know the index), but
                // here we do: keep the cache valid so repeated appends (push, building
                // arrays) stay O(1) instead of O(n) per element / O(n^2) overall.
                var prevLen = cachedArrayLength;
                AddChild(name, value);
                cachedArrayLength = prevLen < 0 ? -1
                                  : idx >= prevLen ? idx + 1   // extends the array
                                  : prevLen;                   // filled an interior hole
            }
        }

        // Appends value at the current end of the array in O(1).
        // Unlike SetArrayIndex, this maintains the cached array length so that
        // consecutive appends (e.g. spread operations) never trigger an O(n) walk.
        internal void AppendArrayElement(ScriptVar value)
        {
            var len = GetArrayLength(); // O(1) when cache is valid (maintained below)
            AddChild(IndexName(len), value); // AddChild invalidates cachedArrayLength
            cachedArrayLength = len + 1;     // restore immediately
        }

        public int GetArrayLength()
        {
            if (!IsArray) return 0;
            
            // Return cached value if available
            if (cachedArrayLength >= 0)
            {
                return cachedArrayLength;
            }
            
            // Calculate and cache
            var highest = -1;
            var link = FirstChild;

            while (link != null)
            {
                if (int.TryParse(link.Name, out var outputVal))
                {
                    if (outputVal > highest) highest = outputVal;
                }

                link = link.Next;
            }

            cachedArrayLength = highest + 1;
            return cachedArrayLength;
        }

        public int GetChildren()
        {
            var n = 0;
            var link = FirstChild;
            while (link != null)
            {
                n++;
                link = link.Next;
            }
            return n;
        }

        public bool Equal(ScriptVar v)
        {
            using var resV = MathsOp(v, ScriptLex.LexTypes.Equal);
            
            var res = resV.Bool;

            return res;
        }

        // A 64-bit integer arithmetic result as an int when it fits, else a double, so
        // overflowing int arithmetic promotes to a JS number rather than wrapping.
        private static ScriptVar IntOrDoubleResult(long value)
            => value >= int.MinValue && value <= int.MaxValue
                ? new ScriptVar((int)value)
                : new ScriptVar((double)value);

        public ScriptVar MathsOp(ScriptVar b, ScriptLex.LexTypes op)
        {
            var a = this;

            var opc = (char)op;

            if (op == ScriptLex.LexTypes.TypeEqual || op == ScriptLex.LexTypes.NTypeEqual)
            {
                bool equal;
                // Int and Double are the same JS type ("number"); compare them by exact
                // IEEE value, not by internal representation (so 5.0 === 5 is true and
                // 0.1 + 0.2 === 0.3 is false). NaN !== NaN, and -0 === +0, fall out of ==.
                if ((a.IsInt || a.IsDouble) && (b.IsInt || b.IsDouble))
                {
                    equal = a.Float == b.Float;
                }
                else if ((a.flags & Flags.VarTypeMask) == (b.flags & Flags.VarTypeMask))
                {
                    equal = a.MathsOp(b, ScriptLex.LexTypes.Equal).Bool;
                }
                else
                {
                    equal = false;
                }

                return new ScriptVar(op == ScriptLex.LexTypes.TypeEqual ? equal : !equal);
            }

            if (a.IsUndefined && b.IsUndefined)
            {
                switch (op)
                {
                    case ScriptLex.LexTypes.Equal:
                        return new ScriptVar(true);
                    case ScriptLex.LexTypes.NEqual:
                        return new ScriptVar(false);
                    default:
                        return new ScriptVar();
                }
            }
            else
            {
                if ((a.IsNumeric || a.IsUndefined) && (b.IsNumeric || b.IsUndefined))
                {
                    if (!a.IsDouble && !b.IsDouble)
                    {
                        //ints
                        var da = a.Int;
                        var db = b.Int;

                        switch (opc)
                        {
                            // +, -, * promote to double on 32-bit overflow (JS numbers
                            // are doubles), computed in 64-bit to detect it.
                            case '+': return IntOrDoubleResult((long)da + db);
                            case '-': return IntOrDoubleResult((long)da - db);
                            case '*': return IntOrDoubleResult((long)da * db);
                            // JS '/' is always real division (1/3 -> 0.333…); keep an
                            // int result only when it divides evenly. Division by zero
                            // yields Infinity/NaN rather than throwing.
                            case '/':
                                if (db == 0) return new ScriptVar((double)da / db);
                                if (db == -1) return IntOrDoubleResult(-(long)da); // avoids MinValue/-1 overflow
                                if (da % db == 0) return new ScriptVar(da / db);
                                return new ScriptVar((double)da / db);
                            case '&': return new ScriptVar(da & db);
                            case '|': return new ScriptVar(da | db);
                            case '^': return new ScriptVar(da ^ db);
                            case '%':
                                if (db == 0) return new ScriptVar(double.NaN);
                                if (db == -1) return new ScriptVar(0); // avoids MinValue%-1 overflow
                                return new ScriptVar(da % db);
                            case (char)ScriptLex.LexTypes.Power:
                            {
                                var r = Math.Pow(da, db);
                                return r == (int)r && r is >= int.MinValue and <= int.MaxValue
                                    ? new ScriptVar((int)r)
                                    : new ScriptVar(r);
                            }
                            case (char)ScriptLex.LexTypes.Equal: return new ScriptVar(da == db);
                            case (char)ScriptLex.LexTypes.NEqual: return new ScriptVar(da != db);
                            case '<': return new ScriptVar(da < db);
                            case (char)ScriptLex.LexTypes.LEqual: return new ScriptVar(da <= db);
                            case '>': return new ScriptVar(da > db);
                            case (char)ScriptLex.LexTypes.GEqual: return new ScriptVar(da >= db);

                            default: throw new ScriptException("Operation not supported on the Int datatype");
                        }
                    }
                    else
                    {
                        //doubles
                        var da = a.Float;
                        var db = b.Float;

                        switch (opc)
                        {
                            case '+': return new ScriptVar(da + db);
                            case '-': return new ScriptVar(da - db);
                            case '*': return new ScriptVar(da * db);
                            case '/': return new ScriptVar(da / db);
                            case '%': return new ScriptVar(da % db);
                            case (char)ScriptLex.LexTypes.Power: return new ScriptVar(Math.Pow(da, db));
                            // Exact IEEE comparison — JS == on numbers is not approximate
                            // (0.1 + 0.2 == 0.3 is false; NaN == NaN is false).
                            case (char)ScriptLex.LexTypes.Equal: return new ScriptVar(da == db);
                            case (char)ScriptLex.LexTypes.NEqual: return new ScriptVar(da != db);
                            case '<': return new ScriptVar(da < db);
                            case (char)ScriptLex.LexTypes.LEqual: return new ScriptVar(da <= db);
                            case '>': return new ScriptVar(da > db);
                            case (char)ScriptLex.LexTypes.GEqual: return new ScriptVar(da >= db);

                            default: throw new ScriptException("Operation not supported on the Int datatype");
                        }
                    }
                }
                if (a.IsArray)
                {
                    switch (op)
                    {
                        case ScriptLex.LexTypes.Equal: return new ScriptVar(a == b);
                        case ScriptLex.LexTypes.NEqual: return new ScriptVar(a != b);

                        default: throw new ScriptException("Operation not supported on the Array datatype");
                    }
                }

                if (a.IsObject)
                {
                    switch (op)
                    {
                        case ScriptLex.LexTypes.Equal: return new ScriptVar(a == b);
                        case ScriptLex.LexTypes.NEqual: return new ScriptVar(a != b);

                        default: throw new ScriptException("Operation not supported on the Object datatype");
                    }
                }

                // Symbols are equal only if they share the same unique ID.
                if (a.IsSymbol || b.IsSymbol)
                {
                    var symEqual = a.IsSymbol && b.IsSymbol && a.intData == b.intData;
                    switch (op)
                    {
                        case ScriptLex.LexTypes.Equal: return new ScriptVar(symEqual);
                        case ScriptLex.LexTypes.NEqual: return new ScriptVar(!symEqual);
                        default: throw new ScriptException("Operation not supported on the Symbol datatype");
                    }
                }

                // BigInt operations — both operands must be BigInt (no implicit mixed-type conversion)
                if (a.IsBigInt || b.IsBigInt)
                {
                    if (!a.IsBigInt || !b.IsBigInt)
                        throw new ScriptException("Cannot mix BigInt and other types; use explicit conversions");
                    var ba = a.BigIntData;
                    var bb = b.BigIntData;
                    switch (opc)
                    {
                        case '+': return CreateBigInt(ba + bb);
                        case '-': return CreateBigInt(ba - bb);
                        case '*': return CreateBigInt(ba * bb);
                        case '/':
                            if (bb == BigInteger.Zero) throw new ScriptException("Division by zero");
                            return CreateBigInt(ba / bb);
                        case '%':
                            if (bb == BigInteger.Zero) throw new ScriptException("Division by zero");
                            return CreateBigInt(ba % bb);
                        case (char)ScriptLex.LexTypes.Power:
                            if (bb < BigInteger.Zero)
                                throw new ScriptException("BigInt negative exponent");
                            if (bb > int.MaxValue)
                                throw new ScriptException("BigInt exponent too large");
                            return CreateBigInt(BigInteger.Pow(ba, (int)bb));
                        case '&': return CreateBigInt(ba & bb);
                        case '|': return CreateBigInt(ba | bb);
                        case '^': return CreateBigInt(ba ^ bb);
                        case '<': return new ScriptVar(ba < bb);
                        case '>': return new ScriptVar(ba > bb);
                        case (char)ScriptLex.LexTypes.LEqual: return new ScriptVar(ba <= bb);
                        case (char)ScriptLex.LexTypes.GEqual: return new ScriptVar(ba >= bb);
                        case (char)ScriptLex.LexTypes.Equal: return new ScriptVar(ba == bb);
                        case (char)ScriptLex.LexTypes.NEqual: return new ScriptVar(ba != bb);
                        default: throw new ScriptException("Operation not supported on BigInt");
                    }
                }

                // Concatenation builds a lazy rope (no materialisation of the left
                // operand), making s += x loops O(n) instead of O(n^2).
                if (opc == '+') return ConcatStrings(a, b);

                var sda = a.GetString();
                var sdb = b.GetString();

                switch (opc)
                {
                    case '+': return new ScriptVar(sda + sdb, Flags.String); // unreachable; kept for clarity
                    case (char)ScriptLex.LexTypes.Equal: return new ScriptVar(sda == sdb);
                    case (char)ScriptLex.LexTypes.NEqual: return new ScriptVar(sda != sdb);
                    case '<': return new ScriptVar(String.CompareOrdinal(sda, sdb) < 0);
                    case (char)ScriptLex.LexTypes.LEqual: return new ScriptVar((String.CompareOrdinal(sda, sdb) < 0) || sda == sdb);
                    case '>': return new ScriptVar(String.CompareOrdinal(sda, sdb) > 0);
                    case (char)ScriptLex.LexTypes.GEqual: return new ScriptVar((String.CompareOrdinal(sda, sdb) > 0) || sda == sdb);
                    default: throw new ScriptException("Operation not supported on the String datatype");
                }
            }
        }

        private void CopySimpleData(ScriptVar val)
        {
            if ((flags & Flags.Interned) != 0)
                throw new InvalidOperationException(
                    $"Cannot mutate interned ScriptVar (value={intData}) via CopySimpleData");
            scriptData = val.scriptData;
            intData = val.intData;
            doubleData = val.doubleData;
            flags = (flags & ~Flags.VarTypeMask) | (val.flags & Flags.VarTypeMask);
        }

        public void CopyValue(ScriptVar val)
        {
            if (val != null)
            {
                CopySimpleData(val);
                RemoveAllChildren();

                var link = val.FirstChild;

                while (link != null)
                {
                    var copied = link.Name != PrototypeClassName ? link.Var.DeepCopy() : link.Var;

                    AddChild(link.Name, copied);

                    link = link.Next;
                }
            }
            else
            {
                SetUndefined();
            }
        }

        public ScriptVar DeepCopy()
        {
            var newVar = new ScriptVar();
            newVar.CopySimpleData(this);

            var link = FirstChild;
            while (link != null)
            {
                var copied = link.Name != PrototypeClassName ? link.Var.DeepCopy() : link.Var;

                newVar.AddChild(link.Name, copied);

                link = link.Next;
            }

            return newVar;
        }

        public void SetCallback(ScriptEngine.ScriptCallbackCB callback, object userdata)
        {
            scriptData = new NativeCallback(callback, userdata);
        }

        public void Trace(int indent, string name)
        {
            System.Diagnostics.Trace.TraceInformation("{0}{1} = '{2}' ({3})", new string(' ', indent), name ?? "ROOT", GetString(), flags);

            var link = FirstChild;
            while (link != null)
            {
                link.Var.Trace(indent + 2, link.Name);
                link = link.Next;
            }
        }

        /// <summary>
        /// Append this value as compact JSON (no whitespace, matching
        /// <c>JSON.stringify(x)</c>) to <paramref name="sb"/>. Building into a shared
        /// <see cref="StringBuilder"/> avoids the per-node stream/writer allocation the
        /// old <see cref="GetJSON(Stream, string)"/> incurred.
        /// </summary>
        public void AppendJson(StringBuilder sb)
        {
            if (IsObject)
            {
                sb.Append('{');
                var link = FirstChild;
                var first = true;
                while (link != null)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.AppendJsString(link.Name);   // append directly, no temp string
                    sb.Append(':');
                    link.Var.AppendJson(sb);
                    link = link.Next;
                }
                sb.Append('}');
            }
            else if (IsArray)
            {
                sb.Append('[');
                var arrayLength = GetArrayLength();
                for (var x = 0; x < arrayLength; x++)
                {
                    if (x > 0) sb.Append(',');
                    GetArrayIndex(x).AppendJson(sb);
                }
                sb.Append(']');
            }
            else if (IsString)
            {
                sb.AppendJsString(GetString());   // append directly, no temp string
            }
            else
            {
                sb.Append(GetParsableString());
            }
        }

        /// <summary>
        /// Write this value as JSON to a stream. Retained for API compatibility;
        /// delegates to <see cref="AppendJson"/> (compact output). <paramref name="linePrefix"/>
        /// is ignored.
        /// </summary>
        public void GetJSON(Stream stream, string linePrefix)
        {
            var sb = new StringBuilder();
            AppendJson(sb);
            var streamWriter = new StreamWriter(stream);
            streamWriter.Write(sb.ToString());
            streamWriter.Flush();
        }

        public string GetParsableString()
        {
            if(IsNumeric)
            {
                return GetString();
            }
            if(IsBigInt)
            {
                // Console / parsable form mirrors a BigInt literal (digits + "n").
                return GetString() + "n";
            }
            if(IsFunction)
            {
                // compiled functions retain their source for round-tripping
                if (scriptData is Vm.VmFunction fn) return fn.Source;

                var builder = new StringBuilder();
                builder.Append("function (");
                var link = FirstChild;
                while(link != null)
                {
                    builder.Append(link.Name);
                    if (link.Next != null)
                    {
                        builder.Append(',');
                    }
                    link = link.Next;
                }
                builder.Append(')');
                builder.Append(GetString());

                return builder.ToString();
            }
            if(IsString)
            {
                return GetString().GetJSString();
            }
            if(IsNull)
            {
                return "null";
            }
            if(IsArray)
            {
                var len = GetArrayLength();
                if (len == 0) return "";
                var parts = new System.Text.StringBuilder();
                for (var i = 0; i < len; i++)
                {
                    if (i > 0) parts.Append(',');
                    var elem = GetArrayIndex(i);
                    if (elem != null && !elem.IsNull && !elem.IsUndefined)
                        parts.Append(elem.GetParsableString());
                }
                return parts.ToString();
            }
            if(IsObject)
            {
                return "[object Object]";
            }

            return "undefined";
        }

        public static implicit operator string(ScriptVar d)
        {
            return d.GetString();
        }

        public override string ToString()
        {
            return $"{GetObjectType()} , {String}";
        }

        public void SetData(object data)
        {
            scriptData = data;
        }

        public ScriptEngine.ScriptCallbackCB GetCallback()
        {
            return scriptData is NativeCallback nc ? nc.Callback : null;
        }

        public object GetCallbackUserData()
        {
            return scriptData is NativeCallback nc ? nc.UserData : null;
        }

        /// <summary>
        /// Serialize this ScriptVar and all its children to a BinaryWriter
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            // Write flags
            writer.Write((int)flags);
            
            // Write data based on type
            writer.Write(intData);
            writer.Write(doubleData);
            
            // Write string/object data. A native callback holder is not
            // serializable data (the delegate is re-attached on restore), so it is
            // treated exactly like the null case the dedicated field used to give.
            if (scriptData == null || scriptData is NativeCallback)
            {
                writer.Write(false); // null marker
            }
            else
            {
                writer.Write(true); // has data marker
                
                if (IsString || IsFunction)
                {
                    writer.Write(scriptData.ToString());
                }
                else if (IsRegexp)
                {
                    // Store regex pattern and options
                    if (scriptData is Regex regex)
                    {
                        writer.Write(regex.ToString());
                        writer.Write((int)regex.Options);
                    }
                    else
                    {
                        writer.Write(string.Empty);
                        writer.Write(0);
                    }
                }
                else
                {
                    // For other types, try to convert to string
                    writer.Write(scriptData.ToString() ?? string.Empty);
                }
            }
            
            // Write native function marker (can't be serialized)
            writer.Write(IsNative);
            
            // Write children count
            var childCount = GetChildren();
            writer.Write(childCount);

            // Write each child
            var link = FirstChild;
            while (link != null)
            {
                writer.Write(link.Name ?? string.Empty);
                writer.Write(link.IsConst);
                link.Var.Serialize(writer);
                link = link.Next;
            }
        }
        
        /// <summary>
        /// Deserialize a ScriptVar from a BinaryReader
        /// </summary>
        public static ScriptVar Deserialize(BinaryReader reader)
        {
            // Read flags
            var flags = (Flags)reader.ReadInt32();

            // Create a new ScriptVar using the default constructor to avoid null reference issues
            var var = new ScriptVar
            {
                flags = flags,

                // Read data
                intData = reader.ReadInt32(),
                doubleData = reader.ReadDouble()
            };

            // Read string/object data
            var hasData = reader.ReadBoolean();
            if (hasData)
            {
                if (var.IsString || var.IsFunction)
                {
                    var.scriptData = reader.ReadString();
                }
                else if (var.IsRegexp)
                {
                    var pattern = reader.ReadString();
                    var options = (RegexOptions)reader.ReadInt32();

                    // Drop Compiled when dynamic code is unsupported (Native AOT),
                    // where it would only be ignored — see CompiledIfSupported.
                    if (!RuntimeFeature.IsDynamicCodeCompiled)
                    {
                        options &= ~RegexOptions.Compiled;
                    }

                    if (!string.IsNullOrEmpty(pattern))
                    {
                        try
                        {
                            var.scriptData = new Regex(pattern, options);
                        }
                        catch
                        {
                            var.scriptData = null;
                        }
                    }
                }
                else
                {
                    var.scriptData = reader.ReadString();
                }
            }
            
            // Read native function marker
            var isNative = reader.ReadBoolean();
            if (isNative)
            {
                // Native functions cannot be serialized - they will need to be re-registered
                // Mark this so we know it needs to be handled on resume
                var.flags |= Flags.Native;
            }
            
            // Read children
            var childCount = reader.ReadInt32();
            for (var i = 0; i < childCount; i++)
            {
                var childName = reader.ReadString();
                var isConst = reader.ReadBoolean();
                var childVar = Deserialize(reader);
                var.AddChild(childName, childVar, isConst);
            }
            
            return var;
        }

    }
}
