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

using System.Text;

namespace DScript
{
    internal static class Utils
    {
        internal static bool IsWhitespace(this char ch)
        {
            return (ch == ' ') || (ch == '\t') || (ch == '\n') || (ch == '\r');
        }

        internal static bool IsNumeric(this char ch)
        {
            return (ch >= '0') && (ch <= '9');
        }

        internal static bool IsNumber(this string str)
        {
            var c = str.Length;
            for (var i = 0; i < c; i++)
            {
                if (!str[i].IsNumeric()) return false;
            }
            return true;
        }

        internal static bool IsHexadecimal(this char ch)
        {
            return ((ch >= '0') && (ch <= '9')) || ((ch >= 'a') && (ch <= 'f')) || ((ch >= 'A') && (ch <= 'F'));
        }

        internal static bool IsAlpha(this char ch)
        {
            return ((ch >= 'a') && (ch <= 'z')) || ((ch >= 'A') && (ch <= 'Z')) || ch == '_' || ch == '$';
        }

        internal static bool IsIDString(this string str)
        {
            var c = str.Length;
            for (var i = 0; i < c; i++)
            {
                if (str[i].IsNumeric() || !str[i].IsAlpha()) return false;
            }
            return true;
        }

        internal static string GetJSString(this string str)
        {
            var builder = new StringBuilder(str.Length + 10);
            builder.Append('"');

            foreach (var ch in str)
            {
                switch (ch)
                {
                    case '\\':
                        builder.Append(@"\\");
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
                        var nCh = ((int)ch) & 0xFF;
                        if (nCh < 32 || nCh > 127)
                        {
                            builder.AppendFormat("\\x{0:x2}", nCh);
                        }
                        else
                        {
                            builder.Append(ch);
                        }
                    }
                        break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }
    }
}
