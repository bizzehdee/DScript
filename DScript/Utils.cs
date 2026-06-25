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

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DScript
{
    internal static class Utils
    {
        internal static bool IsWhitespace(this char ch)
        {
            return ch is ' ' or '\t' or '\n' or '\r';
        }

        internal static bool IsNumeric(this char ch)
        {
            return ch is >= '0' and <= '9';
        }

        [ExcludeFromCodeCoverage] // dead code — no callers in this assembly; InternalsVisibleTo not configured
        internal static bool IsNumber(this string str)
        {
            foreach (var ch in str)
            {
                if (!ch.IsNumeric()) return false;
            }
            return true;
        }

        internal static bool IsHexadecimal(this char ch)
        {
            return ch is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
        }

        internal static bool IsAlpha(this char ch)
        {
            return ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_' or '$';
        }

        [ExcludeFromCodeCoverage] // dead code — no callers in this assembly; InternalsVisibleTo not configured
        internal static bool IsIDString(this string str)
        {
            foreach (var ch in str)
            {
                if (ch.IsNumeric() || !ch.IsAlpha()) return false;
            }
            return true;
        }

        internal static string GetJSString(this string str)
        {
            var builder = new StringBuilder(str.Length + 10);
            builder.AppendJsString(str);
            return builder.ToString();
        }

        /// <summary>
        /// Append <paramref name="str"/> as a quoted, escaped JS/JSON string directly
        /// to <paramref name="builder"/> — avoids allocating an intermediate string
        /// (and a per-call StringBuilder) the way <see cref="GetJSString"/> does, which
        /// matters when serialising many strings (e.g. JSON.stringify).
        /// </summary>
        internal static void AppendJsString(this StringBuilder builder, string str)
        {
            builder.Append('"');

            foreach (var ch in str)
            {
                switch (ch)
                {
                    case '\\': builder.Append(@"\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\a': builder.Append("\\a"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\v': builder.Append("\\v"); break;
                    case '"': builder.Append("\\\""); break;
                    default:
                    {
                        if (ch < 0x20)
                        {
                            // Other control characters → \uXXXX (valid JSON; the old
                            // code masked to a byte and emitted \xNN, which both
                            // corrupted code points > 0xFF and is not valid JSON).
                            builder.Append("\\u");
                            builder.Append(((int)ch).ToString("x4"));
                        }
                        else
                        {
                            // Printable, including non-ASCII (✓, é, emoji surrogate
                            // halves) — emit as-is, matching JSON.stringify and V8.
                            builder.Append(ch);
                        }
                    }
                        break;
                }
            }

            builder.Append('"');
        }
    }
}
