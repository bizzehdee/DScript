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

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("RegExp")]
    public static class RegExpFunctionProvider
    {
        private static Regex GetRegex(ScriptVar thisVar)
        {
            var data = thisVar.GetData();
            if (data is Regex r) return r;
            // Fallback for regex literals (stored as Regex in scriptData via Regexp flag)
            return data as Regex ?? new Regex(thisVar.String);
        }

        [ScriptMethod("test", "str")]
        public static void RegExpTestImpl(ScriptVar var, object userData)
        {
            var regex = GetRegex(var.GetParameter("this"));
            var str = var.GetParameter("str").String;
            var.ReturnVar.Bool = regex.IsMatch(str);
        }

        [ScriptMethod("exec", "str")]
        public static void RegExpExecImpl(ScriptVar var, object userData)
        {
            var regex = GetRegex(var.GetParameter("this"));
            var str = var.GetParameter("str").String;
            var match = regex.Match(str);
            if (!match.Success)
            {
                var.ReturnVar.SetUndefined();
                return;
            }
            var arr = new ScriptVar();
            arr.SetArray();
            for (var i = 0; i < match.Groups.Count; i++)
            {
                var g = match.Groups[i];
                arr.SetArrayIndex(i, g.Success ? new ScriptVar(g.Value) : new ScriptVar(ScriptVar.Flags.Undefined));
            }
            arr.AddChild("index", new ScriptVar(match.Index));
            arr.AddChild("input", new ScriptVar(str));
            // Build .groups for named captures
            var groups = BuildNamedGroups(regex, match);
            arr.AddChild("groups", groups);
            var.ReturnVar = arr;
        }

        /// <summary>Builds a groups object from named captures, or undefined if none.</summary>
        internal static ScriptVar BuildNamedGroups(Regex regex, Match match)
        {
            var groupNames = regex.GetGroupNames();
            var hasNamed = false;
            foreach (var gn in groupNames)
            {
                if (!int.TryParse(gn, out _)) { hasNamed = true; break; }
            }
            if (!hasNamed) return new ScriptVar(ScriptVar.Flags.Undefined);
            var groups = new ScriptVar(ScriptVar.Flags.Object);
            foreach (var gn in groupNames)
            {
                if (int.TryParse(gn, out _)) continue;
                var g = match.Groups[gn];
                groups.AddChildNoDup(gn, g.Success ? new ScriptVar(g.Value) : new ScriptVar(ScriptVar.Flags.Undefined));
            }
            return groups;
        }
    }
}
