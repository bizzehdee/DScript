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
            var thisVar = var.GetParameter("this");
            var regex = GetRegex(thisVar);
            var str = var.GetParameter("str").String;
            var match = regex.Match(str);
            if (!match.Success)
            {
                var.ReturnVar.SetUndefined();
                return;
            }
            var arr = ScriptVar.CreateUndefined();
            arr.SetArray();
            for (var i = 0; i < match.Groups.Count; i++)
            {
                var g = match.Groups[i];
                arr.SetArrayIndex(i, g.Success ? ScriptVar.FromString(g.Value) : ScriptVar.CreateUndefined());
            }
            arr.AddChild("index", ScriptVar.FromInt(match.Index));
            arr.AddChild("input", ScriptVar.FromString(str));
            // Build .groups for named captures
            var groups = BuildNamedGroups(regex, match);
            arr.AddChild("groups", groups);
            if (thisVar.RegexHasIndices)
                arr.AddChild("indices", BuildIndices(regex, match));
            var.ReturnVar = arr;
        }

        /// <summary>Builds a groups object from named captures, or undefined if none.</summary>
        internal static ScriptVar BuildNamedGroups(Regex regex, Match match)
            => BuildNamedGroups(GetNamedGroupNames(regex), match);

        /// <summary>
        /// Builds a groups object from pre-computed group names.
        /// Call <see cref="GetNamedGroupNames"/> once per regex and reuse across matches.
        /// </summary>
        internal static ScriptVar BuildNamedGroups(string[] namedGroupNames, Match match)
        {
            if (namedGroupNames.Length == 0) return ScriptVar.CreateUndefined();
            var groups = ScriptVar.CreateObject();
            foreach (var gn in namedGroupNames)
            {
                var g = match.Groups[gn];
                groups.AddChildNoDup(gn, g.Success ? ScriptVar.FromString(g.Value) : ScriptVar.CreateUndefined());
            }
            return groups;
        }

        /// <summary>Returns only the non-numeric group names (named captures) for a regex.</summary>
        internal static string[] GetNamedGroupNames(Regex regex)
        {
            var all = regex.GetGroupNames();
            var named = new System.Collections.Generic.List<string>(all.Length);
            foreach (var gn in all)
                if (!int.TryParse(gn, out _)) named.Add(gn);
            return named.ToArray();
        }

        /// <summary>Builds the .indices array for the d flag: each element is [start, end] or undefined.</summary>
        internal static ScriptVar BuildIndices(Regex regex, Match match)
        {
            var indices = ScriptVar.CreateUndefined();
            indices.SetArray();
            for (var i = 0; i < match.Groups.Count; i++)
            {
                var g = match.Groups[i];
                if (g.Success)
                {
                    var pair = ScriptVar.CreateUndefined(); pair.SetArray();
                    pair.SetArrayIndex(0, ScriptVar.FromInt(g.Index));
                    pair.SetArrayIndex(1, ScriptVar.FromInt(g.Index + g.Length));
                    indices.SetArrayIndex(i, pair);
                }
                else
                {
                    indices.SetArrayIndex(i, ScriptVar.CreateUndefined());
                }
            }
            // Named-group indices
            var groupNames = regex.GetGroupNames();
            var hasNamed = false;
            foreach (var gn in groupNames)
                if (!int.TryParse(gn, out _)) { hasNamed = true; break; }
            if (hasNamed)
            {
                var namedIndices = ScriptVar.CreateObject();
                foreach (var gn in groupNames)
                {
                    if (int.TryParse(gn, out _)) continue;
                    var g = match.Groups[gn];
                    if (g.Success)
                    {
                        var pair = ScriptVar.CreateUndefined(); pair.SetArray();
                        pair.SetArrayIndex(0, ScriptVar.FromInt(g.Index));
                        pair.SetArrayIndex(1, ScriptVar.FromInt(g.Index + g.Length));
                        namedIndices.AddChildNoDup(gn, pair);
                    }
                    else
                    {
                        namedIndices.AddChildNoDup(gn, ScriptVar.CreateUndefined());
                    }
                }
                indices.AddChild("groups", namedIndices);
            }
            else
            {
                indices.AddChild("groups", ScriptVar.CreateUndefined());
            }
            return indices;
        }
    }
}
