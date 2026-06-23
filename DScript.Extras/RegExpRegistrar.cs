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

using System.Text.RegularExpressions;

namespace DScript.Extras
{
    internal static class RegExpRegistrar
    {
        internal static void Register(ScriptEngine engine)
        {
            var regExpCtorVar = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            regExpCtorVar.AddChild("pattern", new ScriptVar(ScriptVar.Flags.Undefined));
            regExpCtorVar.AddChild("flags", new ScriptVar(ScriptVar.Flags.Undefined));
            regExpCtorVar.SetCallback((scope, _) =>
            {
                var pattern = scope.FindChild("pattern")?.Var?.String ?? "";
                var flagsVar = scope.FindChild("flags")?.Var;
                var flags = (flagsVar == null || flagsVar.IsUndefined) ? "" : flagsVar.String;

                var opts = RegexOptions.None;
                var global = false;
                var hasS = false;
                var hasIndices = false;
                var hasUnicode = false;
                foreach (var c in flags)
                {
                    switch (c)
                    {
                        case 'i': opts |= RegexOptions.IgnoreCase; break;
                        case 'm': opts |= RegexOptions.Multiline; break;
                        case 's': opts |= RegexOptions.Singleline; hasS = true; break;
                        case 'g': global = true; break;
                        case 'd': hasIndices = true; break;
                        case 'u': case 'v': hasUnicode = true; break;
                    }
                }

                var translatedPattern = hasUnicode
                    ? ScriptVar.TranslateUnicodeProperties(pattern)
                    : pattern;
                if (hasUnicode) opts |= RegexOptions.CultureInvariant;

                // Produce the sorted canonical flags string (ES spec order: d,g,i,m,s,u,v,y)
                var sortedFlags = string.Concat(
                    System.Linq.Enumerable.Where("dgimsuyv", f => flags.Contains(f)));

                var regex = new Regex(translatedPattern, opts);
                var thisVar = scope.FindChild("this")?.Var;
                if (thisVar != null)
                {
                    thisVar.SetData(regex);
                    thisVar.AddChild("source", new ScriptVar(pattern));
                    thisVar.AddChild("flags", new ScriptVar(sortedFlags));
                    thisVar.AddChild("global", new ScriptVar(global));
                    thisVar.AddChild("ignoreCase", new ScriptVar((opts & RegexOptions.IgnoreCase) != 0));
                    thisVar.AddChild("multiline", new ScriptVar((opts & RegexOptions.Multiline) != 0));
                    thisVar.AddChild("dotAll", new ScriptVar(hasS));
                    thisVar.AddChild("hasIndices", new ScriptVar(hasIndices));
                }
            }, null);

            engine.Root.AddChild("RegExp", regExpCtorVar);
        }
    }
}
