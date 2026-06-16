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
using System.Text;
using System.Text.RegularExpressions;

namespace DScript
{
    public sealed class ScriptVar : IDisposable
    {
        // Cache compiled regex patterns to avoid recompilation (performance optimization)
        private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();

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
            if (disposed) return;
            
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
            NumericMask = Null | Double | Integer,
            VarTypeMask =  Double | Integer | String | Function | Object | Array | Null | Regexp
        }

        public ScriptVarLink FirstChild { get; set; }
        public ScriptVarLink LastChild { get; set; }

        // The CLR zero-initializes every freshly allocated object, so the
        // constructors only need to set the fields that differ from default
        // (flags and the relevant value field). They deliberately avoid the old
        // Init() call, which re-zeroed already-zero fields on every allocation —
        // a measurable cost given how many short-lived ScriptVars the VM creates.
        public ScriptVar()
        {
            // Undefined == 0, so a zero-initialized instance is already correct.
        }

        public ScriptVar(int val)
        {
            flags = Flags.Integer;
            intData = val;
        }

        public ScriptVar(double val)
        {
            flags = Flags.Double;
            doubleData = val;
        }

        public ScriptVar(string val)
        {
            flags = Flags.String;
            scriptData = val;
        }

        public ScriptVar(bool val)
        {
            flags = Flags.Integer;
            intData = val ? 1 : 0;
        }

        public ScriptVar(string val, Flags flags)
        {
            this.flags = flags;
            if (flags.HasFlag(Flags.Integer))
            {
                if (val.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    intData = Convert.ToInt32(val, 16);
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

                var regexOpts = RegexOptions.Compiled | RegexOptions.ECMAScript;

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
                    }
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

        public bool IsNumeric => (flags & Flags.NumericMask) != 0;

        public bool IsFunction => (flags & Flags.Function) != 0;

        public bool IsObject => (flags & Flags.Object) != 0;

        public bool IsArray => (flags & Flags.Array) != 0;

        public bool IsNative => (flags & Flags.Native) != 0;

        public bool IsUndefined => (flags & Flags.VarTypeMask) == Flags.Undefined;

        public bool IsNull => (flags & Flags.Null) != 0;
        
        public bool IsRegexp => (flags & Flags.Regexp) != 0;

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
            get => Int != 0;
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
                return Float.ToString(CultureInfo.InvariantCulture);
            }
            if (IsNull) return "null";
            if (IsUndefined) return "undefined";

            if (scriptData is Vm.VmFunction fn) return fn.Source;

            return scriptData as string ?? string.Empty;
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
            if (IsObject) return "object";
            if (IsArray) return "array";
            if (IsFunction) return "function";
            if (IsString) return "string";
            if (IsNull) return "null";
            
            return "undefined";
        }

        private void SetInt(int num)
        {
            flags = (flags & ~Flags.VarTypeMask) | Flags.Integer;
            intData = num;
            doubleData = 0;
            scriptData = null;
        }

        private void SetDouble(double num)
        {
            flags = (flags & ~Flags.VarTypeMask) | Flags.Double;
            intData = 0;
            doubleData = num;
            scriptData = null;
        }

        private void SetString(string str)
        {
            flags = (flags & ~Flags.VarTypeMask) | Flags.String;
            intData = 0;
            doubleData = 0;
            scriptData = str;
        }

        public void SetUndefined()
        {
            flags = (flags & ~Flags.VarTypeMask) | Flags.Undefined;
            intData = 0;
            doubleData = 0;
            scriptData = null;
            RemoveAllChildren();
        }

        public void SetArray()
        {
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

            return AddChild(childName, new ScriptVar(null, varFlags), readOnly);
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

            var link = new ScriptVarLink(c, childName, readOnly)
            {
                Owned = true,
                Owner = this
            };

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

            return new ScriptVar(null, Flags.Null);
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

                AddChild(name, value);
                cachedArrayLength = -1;  // Invalidate cache on addition
            }
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

        public ScriptVar MathsOp(ScriptVar b, ScriptLex.LexTypes op)
        {
            var a = this;

            var opc = (char)op;

            if (op == ScriptLex.LexTypes.TypeEqual || op == ScriptLex.LexTypes.NTypeEqual)
            {
                var equal = ((a.flags & Flags.VarTypeMask) == (b.flags & Flags.VarTypeMask));

                if (equal)
                {
                    var contents = a.MathsOp(b, ScriptLex.LexTypes.Equal);
                    if (!contents.Bool) equal = false;
                }

                if (op == ScriptLex.LexTypes.TypeEqual)
                {
                    return new ScriptVar(equal);
                }

                return new ScriptVar(!equal);
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
                            case '+': return new ScriptVar(da + db);
                            case '-': return new ScriptVar(da - db);
                            case '*': return new ScriptVar(da * db);
                            // Match JS semantics for division by zero (Infinity/NaN)
                            // instead of throwing a DivideByZeroException.
                            case '/': return db == 0 ? new ScriptVar((double)da / db) : new ScriptVar(da / db);
                            case '&': return new ScriptVar(da & db);
                            case '|': return new ScriptVar(da | db);
                            case '^': return new ScriptVar(da ^ db);
                            case '%': return db == 0 ? new ScriptVar(double.NaN) : new ScriptVar(da % db);
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
                            case (char)ScriptLex.LexTypes.Equal: return new ScriptVar(Math.Abs(da - db) < 0.00001);
                            case (char)ScriptLex.LexTypes.NEqual: return new ScriptVar(Math.Abs(da - db) > 0.00001);
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

                var sda = a.GetString();
                var sdb = b.GetString();

                switch (opc)
                {
                    case '+': return new ScriptVar(sda + sdb, Flags.String);
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

        public void GetJSON(Stream stream, string linePrefix)
        {
            var streamWriter = new StreamWriter(stream);
            
            if (IsObject)
            {
                streamWriter.WriteLine("{");

                var link = FirstChild;

                while(link != null)
                {
                    streamWriter.Write(linePrefix);
                    streamWriter.Write(link.Name.GetJSString());
                    streamWriter.Write(": ");
                    streamWriter.Flush();

                    link.Var.GetJSON(stream, linePrefix + "    ");

                    link = link.Next;
                    if(link != null)
                    {
                        streamWriter.WriteLine(",");
                    }
                }
                streamWriter.WriteLine();
                streamWriter.WriteLine("}");
            }
            else if(IsArray)
            {
                streamWriter.WriteLine("[");

                var arrayLength = GetArrayLength();
                for(var x=0; x<arrayLength; x++)
                {
                    streamWriter.Flush();
                    GetArrayIndex(x).GetJSON(stream, linePrefix + "    ");
                    if(x<arrayLength-1)
                    {
                        streamWriter.WriteLine(",");
                    }
                }

                streamWriter.WriteLine("]");
            }
            else
            {
                streamWriter.Write(GetParsableString());
            }

            streamWriter.Flush();
        }

        public string GetParsableString()
        {
            if(IsNumeric)
            {
                return GetString();
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

        internal void SetData(object data)
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
