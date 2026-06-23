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
using System.Linq;

namespace DScript.Extras.FunctionProviders
{
    // Shared utilities used across FunctionProvider implementations.
    internal static class ProviderHelpers
    {
        // Resolves a potentially-negative index to a clamped position in [0, length].
        // Negative values count from the end of the sequence (e.g. -1 => length-1).
        internal static int NormalizeIndex(int index, int length)
        {
            if (index < 0) index = length + index;
            return Math.Clamp(index, 0, length);
        }

        // Resolves a start/end pair for ES slice semantics: undefined defaults to 0/length,
        // negative values count from the end, and both are clamped to [0, length].
        internal static (int start, int end) NormalizeSliceRange(
            ScriptVar startVar, ScriptVar endVar, int length)
        {
            var start = startVar.IsUndefined ? 0 : startVar.Int;
            var end   = endVar.IsUndefined   ? length : endVar.Int;
            return (NormalizeIndex(start, length), NormalizeIndex(end, length));
        }

        // Generates a padding string of exactly `needed` characters from the fill template.
        internal static string BuildPadding(string fill, int needed)
            => string.Concat(Enumerable.Repeat(fill, (needed / fill.Length) + 1))
                     .Substring(0, needed);

        // Iterates enumerable, non-prototype children of obj in insertion order.
        internal static void ForEachEnumerableChild(ScriptVar obj, Action<ScriptVarLink> action)
        {
            var link = obj.FirstChild;
            while (link != null)
            {
                if (link.Name != ScriptVar.PrototypeClassName && link.Enumerable)
                    action(link);
                link = link.Next;
            }
        }

        // Iterates all non-prototype children of obj (including non-enumerable).
        internal static void ForEachOwnChild(ScriptVar obj, Action<ScriptVarLink> action)
        {
            var link = obj.FirstChild;
            while (link != null)
            {
                if (link.Name != ScriptVar.PrototypeClassName)
                    action(link);
                link = link.Next;
            }
        }
    }
}
