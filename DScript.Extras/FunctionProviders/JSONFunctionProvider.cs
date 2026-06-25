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

using System.Globalization;
using System.IO;
using System.Text;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("JSON")]
    public static class JSONFunctionProvider
    {
        [ScriptMethod("parse", "str")]
        public static void JsonParseImpl(ScriptVar var, object userData)
        {
            var text = var.GetParameter("str").String ?? "";
            var.ReturnVar = new JsonParser(text).Parse();
        }

        // A direct recursive-descent JSON parser that builds the ScriptVar tree
        // without going through the script compiler/VM. The previous implementation
        // evaluated the input as DScript source (compile + run per call), which was
        // both slow and unsafe; this is a strict, allocation-light parser.
        private sealed class JsonParser
        {
            private readonly string s;
            private int i;

            public JsonParser(string text) { s = text; }

            public ScriptVar Parse()
            {
                SkipWhitespace();
                var value = ParseValue();
                SkipWhitespace();
                if (i < s.Length) throw Unexpected();
                return value;
            }

            private ScriptVar ParseValue()
            {
                SkipWhitespace();
                if (i >= s.Length) throw Unexpected();
                var c = s[i];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ScriptVar.FromString(ParseString());
                    case 't': Expect("true");  return ScriptVar.FromBool(true);
                    case 'f': Expect("false"); return ScriptVar.FromBool(false);
                    case 'n': Expect("null");  return ScriptVar.CreateNull();
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw Unexpected();
                }
            }

            private ScriptVar ParseObject()
            {
                i++; // '{'
                var obj = ScriptVar.CreateObject();
                SkipWhitespace();
                if (i < s.Length && s[i] == '}') { i++; return obj; }
                while (true)
                {
                    SkipWhitespace();
                    if (i >= s.Length || s[i] != '"') throw Unexpected();
                    var key = ParseString();
                    SkipWhitespace();
                    if (i >= s.Length || s[i] != ':') throw Unexpected();
                    i++; // ':'
                    obj.AddChild(key, ParseValue());
                    SkipWhitespace();
                    if (i >= s.Length) throw Unexpected();
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; break; }
                    throw Unexpected();
                }
                return obj;
            }

            private ScriptVar ParseArray()
            {
                i++; // '['
                var arr = ScriptVar.CreateArray();
                SkipWhitespace();
                if (i < s.Length && s[i] == ']') { i++; return arr; }
                var idx = 0;
                while (true)
                {
                    arr.SetArrayIndex(idx++, ParseValue());
                    SkipWhitespace();
                    if (i >= s.Length) throw Unexpected();
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; break; }
                    throw Unexpected();
                }
                return arr;
            }

            private string ParseString()
            {
                i++; // opening quote
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    var c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (i >= s.Length) break;
                        var e = s[i++];
                        switch (e)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (i + 4 > s.Length) throw Unexpected();
                                sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                                i += 4;
                                break;
                            default: throw Unexpected();
                        }
                    }
                    else sb.Append(c);
                }
                throw new ScriptException("SyntaxError: Unterminated string in JSON");
            }

            private ScriptVar ParseNumber()
            {
                var start = i;
                if (i < s.Length && s[i] == '-') i++;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                var isDouble = false;
                if (i < s.Length && s[i] == '.')
                {
                    isDouble = true; i++;
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                }
                if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                {
                    isDouble = true; i++;
                    if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                }
                var numStr = s.Substring(start, i - start);
                if (!isDouble && int.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                    return ScriptVar.FromInt(iv);
                return ScriptVar.FromDouble(double.Parse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture));
            }

            private void Expect(string literal)
            {
                if (i + literal.Length > s.Length ||
                    string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                    throw Unexpected();
                i += literal.Length;
            }

            private void SkipWhitespace()
            {
                while (i < s.Length)
                {
                    var c = s[i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                    else break;
                }
            }

            private ScriptException Unexpected() =>
                new ScriptException($"SyntaxError: Unexpected token in JSON at position {i}");
        }


        [ScriptMethod("stringify", "obj", "replacer")]
        public static void JsonStringifyImpl(ScriptVar var, object userData)
        {
            var stream = new MemoryStream();
            var.GetParameter("obj").GetJSON(stream, "");

            stream.Seek(0, SeekOrigin.Begin);

            var streamReader = new StreamReader(stream);
            var json = streamReader.ReadToEnd();

            var.ReturnVar.String = json;
        }
    }
}
