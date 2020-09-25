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
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DScript
{
    public class ScriptVar : IDisposable
    {
        #region IDisposable
        private bool _disposed;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    RemoveAllChildren();
                }

                // Indicate that the instance has been disposed.
                _disposed = true;
            }
        }
        #endregion

        private int refs;
        private Flags flags;
        private object data;
        private int intData;
        private double doubleData;
        private ScriptEngine.ScriptCallbackCB callback;
        private object callbackUserData;

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

        public ScriptVar()
        {
            refs = 0;
            flags = Flags.Undefined;
            Init();
        }

        public ScriptVar(int val)
        {
            refs = 0;
            flags = Flags.Integer;
            Init();
            intData = val;
        }

        public ScriptVar(double val)
        {
            refs = 0;
            flags = Flags.Double;
            Init();
            doubleData = val;
        }

        public ScriptVar(string val)
        {
            refs = 0;
            flags = Flags.String;
            Init();
            data = val;
        }

        public ScriptVar(bool val)
        {
            refs = 0;
            flags = Flags.Integer;
            Init();
            intData = val ? 1 : 0;
        }

        public ScriptVar(string val, Flags flags)
        {
            refs = 0;
            this.flags = flags;
            Init();
            if (flags.HasFlag(Flags.Integer))
            {
                var strData = val;
                if (strData.StartsWith("0x"))
                {
                    intData = Convert.ToInt32(strData, 16);
                }
                else if(strData.StartsWith("0"))
                {
                    intData = Convert.ToInt32(strData, 8);
                }
                else
                {
                    intData = int.Parse(strData);
                }
            }
            else if (flags.HasFlag(Flags.Double))
            {
                var strData = val;
                if (double.TryParse(strData, out doubleData) == false)
                {
                    doubleData = Convert.ToDouble(strData);
                }
            }
            else if(flags.HasFlag(Flags.Regexp))
            {
                var lastIndexOf = val.LastIndexOf('/');
                if (lastIndexOf > 0)
                {
                    var regexStr = val.Substring(1, lastIndexOf - 1);
                    var opts = val.Substring(lastIndexOf + 1);

                    var regexOpts = RegexOptions.Compiled | RegexOptions.ECMAScript;

                    foreach (var c in opts)
                    {
                        if (c == 'i')
                        {
                            regexOpts |= RegexOptions.IgnoreCase;
                        }
                        else if(c=='m')
                        {
                            regexOpts |= RegexOptions.Multiline;
                        }
                    }

                    data = new Regex(regexStr, regexOpts);
                }
            }
            else
            {
                data = val;
            }
        }

        private void Init()
        {
            FirstChild = null;
            LastChild = null;
            callback = null;
            callbackUserData = null;
            data = null;
            intData = 0;
            doubleData = 0;
        }

        public bool IsInt
        {
            get { return (flags & Flags.Integer) != 0; }
        }

        public bool IsDouble
        {
            get { return (flags & Flags.Double) != 0; }
        }

        public bool IsString
        {
            get { return (flags & Flags.String) != 0; }
        }

        public bool IsNumeric
        {
            get { return (flags & Flags.NumericMask) != 0; }
        }

        public bool IsFunction
        {
            get { return (flags & Flags.Function) != 0; }
        }

        public bool IsObject
        {
            get { return (flags & Flags.Object) != 0; }
        }

        public bool IsArray
        {
            get { return (flags & Flags.Array) != 0; }
        }

        public bool IsNative
        {
            get { return (flags & Flags.Native) != 0; }
        }

        public bool IsUndefined
        {
            get { return (flags & Flags.VarTypeMask) == Flags.Undefined; }
        }

        public bool IsNull
        {
            get { return (flags & Flags.Null) != 0; }
        }

        public bool IsBasic
        {
            get { return FirstChild == null; }
        }

        public ScriptVar this[string index]
        {
            get { return GetParameter(index); }
        }

        public int Int
        {
            get
            {
                return GetInt();
            }
            set
            {
                SetInt(value);
            }
        }

        public double Float
        {
            get
            {
                return GetDouble();
            }
            set
            {
                SetDouble(value);
            }
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
            get
            {
                return Int != 0;
            }
            set
            {
                Int = value ? 1 : 0;
            }
        }

        private double GetDouble()
        {
            if (IsDouble) return doubleData;
            if (IsInt) return (double)Int;
            if (IsNull) return 0;
            if (IsUndefined) return 0;
            return 0;
        }

        public string String
        {
            get
            {
                return GetString();
            }
            set
            {
                SetString(value);
            }
        }

        private string GetString()
        {
            if (IsInt)
            {
                return string.Format("{0:D}", Int);
            }
            if (IsDouble)
            {
                return string.Format("{0}", Float);
            }
            if (IsNull) return "null";
            if (IsUndefined) return "undefined";

            return (string)data;
        }

        public object GetData()
        {
            if (IsNull) return null;
            if (IsUndefined) return null;
            if (IsInt) return intData;
            if (IsDouble) return doubleData;

            return data;
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
            data = null;
        }

        private void SetDouble(double num)
        {
            flags = (flags & ~Flags.VarTypeMask) | Flags.Double;
            intData = 0;
            doubleData = num;
            data = null;
        }

        private void SetString(string str)
        {
            flags = (flags & ~Flags.VarTypeMask) | Flags.String;
            intData = 0;
            doubleData = 0;
            data = str;
        }

        public void SetUndefined()
        {
            flags = (flags & ~Flags.VarTypeMask) | Flags.Undefined;
            intData = 0;
            doubleData = 0;
            data = null;
            RemoveAllChildren();
        }

        public void SetArray()
        {
            flags = (flags & ~Flags.VarTypeMask) | Flags.Array;
            intData = 0;
            doubleData = 0;
            data = null;
            RemoveAllChildren();
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
            var v = FirstChild;

            while (v != null)
            {
                if (v.Name == childName)
                {
                    return v;
                }

                v = v.Next;
            }

            return null;
        }

        public ScriptVarLink FindChildOrCreate(string childName, Flags varFlags = Flags.Undefined)
        {
            var l = FindChild(childName);
            if (l != null) return l;

            return AddChild(childName, new ScriptVar(null, varFlags));
        }

        public ScriptVarLink FindChildOrCreateByPath(string path)
        {
            var p = path.IndexOf('.');
            if (p < 0) return FindChildOrCreate(path);

            var parts = path.Split('.');

            return FindChildOrCreate(parts[0], Flags.Object).Var.FindChildOrCreateByPath(parts[1]);
        }

        public ScriptVarLink AddChild(string childName, ScriptVar child)
        {
            if (IsUndefined)
            {
                flags = Flags.Object;
            }

            var c = child ?? new ScriptVar();

            var link = new ScriptVarLink(c, childName)
            {
                Owned = true
            };

            if (LastChild != null)
            {
                LastChild.Next = link;
                link.Prev = LastChild;
                LastChild = link;
            }
            else
            {
                FirstChild = link;
                LastChild = link;
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
        }

        public void RemoveAllChildren()
        {
            var c = FirstChild;

            while (c != null)
            {
                var t = c.Next;
                c = t;
            }

            FirstChild = null;
            LastChild = null;
        }

        public ScriptVar ReturnVar
        {
            get
            {
                return GetParameter(ReturnVarName);
            }
            set
            {
                FindChildOrCreate(ReturnVarName).ReplaceWith(value);
            }
        }

        public ScriptVar GetParameter(string name)
        {
            return FindChildOrCreate(name).Var;
        }

        public ScriptVar GetArrayIndex(int idx)
        {
            var link = FindChild(string.Format("{0}", idx));
            if (link != null) return link.Var;

            return new ScriptVar(null, Flags.Null);
        }

        public void SetArrayIndex(int idx, ScriptVar value)
        {
            var link = FindChild(string.Format("{0}", idx));

            if (link != null)
            {
                if (value.IsUndefined)
                {
                    RemoveLink(link);
                }
                else
                {
                    link.ReplaceWith(value);
                }
            }
            else
            {
                if (!value.IsUndefined)
                {
                    AddChild(string.Format("{0}", idx), value);
                }
            }
        }

        public int GetArrayLength()
        {
            var highest = -1;

            if (!IsArray) return 0;

            var link = FirstChild;

            while (link != null)
            {
                if (int.TryParse(link.Name, out int outputVal))
                {
                    if (outputVal > highest) highest = outputVal;
                }

                link = link.Next;
            }

            return highest + 1;
        }

        public int GettChildren()
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
            bool res;

            using (var resV = MathsOp(v, ScriptLex.LexTypes.Equal))
            {
                res = resV.Bool;
            }

            return res;
        }

        public ScriptVar MathsOp(ScriptVar b, ScriptLex.LexTypes op)
        {
            var a = this;

            char opc = (char)op;

            if (op == ScriptLex.LexTypes.TypeEqual || op == ScriptLex.LexTypes.NTypeEqual)
            {
                bool equal = ((a.flags & Flags.VarTypeMask) == (b.flags & Flags.VarTypeMask));

                if (equal)
                {
                    ScriptVar contents = a.MathsOp(b, ScriptLex.LexTypes.Equal);
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
                if (op == ScriptLex.LexTypes.Equal)
                {
                    return new ScriptVar(true);
                }
                if (op == ScriptLex.LexTypes.NEqual)
                {
                    return new ScriptVar(false);
                }

                return new ScriptVar();
            }
            else if ((a.IsNumeric || a.IsUndefined) && (b.IsNumeric || b.IsUndefined))
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
                        case '/': return new ScriptVar(da / db);
                        case '&': return new ScriptVar(da & db);
                        case '|': return new ScriptVar(da | db);
                        case '^': return new ScriptVar(da ^ db);
                        case '%': return new ScriptVar(da % db);
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
            else if (a.IsArray)
            {
                switch (op)
                {
                    case ScriptLex.LexTypes.Equal: return new ScriptVar(a == b);
                    case ScriptLex.LexTypes.NEqual: return new ScriptVar(a != b);

                    default: throw new ScriptException("Operation not supported on the Array datatype");
                }
            }
            else if (a.IsObject)
            {
                switch (op)
                {
                    case ScriptLex.LexTypes.Equal: return new ScriptVar(a == b);
                    case ScriptLex.LexTypes.NEqual: return new ScriptVar(a != b);

                    default: throw new ScriptException("Operation not supported on the Object datatype");
                }
            }
            else
            {
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

        protected void CopySimpleData(ScriptVar val)
        {
            data = val.data;
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
            this.callback = callback;
            callbackUserData = userdata;
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
                    streamWriter.Write(GetJSString(link.Name));
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
                for(int x=0; x<arrayLength; x++)
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

        private string GetJSString(string str)
        {
            var builder = new StringBuilder();
            for (int x = 0; x < str.Length; x++)
            {
                var chr = str[x];
                switch(chr)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\a':
                        builder.Append("\\a");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    case '\v':
                        builder.Append("\\v");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    default:
                        {
                            var nChr = (int)chr & 0xff;
                            if(nChr < 32 || nChr > 127)
                            {
                                builder.AppendFormat("\\x{0:X2}", nChr);
                            }
                            else
                            {
                                builder.Append(chr);
                            }
                        }
                        break;
                }
            }

            return "\"" + builder.ToString() + "\"";
        }

        public string GetParsableString()
        {
            if(IsNumeric)
            {
                return GetString();
            }
            if(IsFunction)
            {
                var builder = new StringBuilder();
                builder.Append("function (");
                var link = FirstChild;
                while(link != null)
                {
                    builder.Append(link.Name);
                    if (link.Next != null)
                    {
                        builder.Append(", ");
                    }
                    link = link.Next;
                }
                builder.Append(")");
                builder.Append(GetString());

                return builder.ToString();
            }
            if(IsString)
            {
                return GetJSString(GetString());
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
            return string.Format("{0} , {1}", GetObjectType(), String);
        }

        internal void SetData(object data)
        {
            this.data = data;
        }

        internal ScriptEngine.ScriptCallbackCB GetCallback()
        {
            return callback;
        }

        internal object GetCallbackUserData()
        {
            return callbackUserData;
        }

    }
}
