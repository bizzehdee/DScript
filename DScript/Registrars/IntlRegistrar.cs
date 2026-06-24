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
using System.Globalization;

namespace DScript.Registrars
{
    internal static class IntlRegistrar
    {
        internal static void Register(ScriptEngine engine)
        {
            var intl = new ScriptVar(ScriptVar.Flags.Object);

            RegisterGetCanonicalLocales(intl);
            RegisterCollator(intl, engine);
            RegisterNumberFormat(intl, engine);
            RegisterDateTimeFormat(intl, engine);
            RegisterDisplayNames(intl, engine);
            RegisterPluralRules(intl, engine);

            engine.Root.AddChild("Intl", intl);
        }

        // ── Intl.getCanonicalLocales(locales) ────────────────────────────────
        private static void RegisterGetCanonicalLocales(ScriptVar intl)
        {
            var fn = MakeNative1("locales", (scope, _) =>
            {
                var localesVar = scope.FindChild("locales")?.Var;
                var arr = new ScriptVar(ScriptVar.Flags.Array);
                int idx = 0;

                if (localesVar == null || localesVar.IsUndefined || localesVar.IsNull)
                {
                    arr.AddChild("length", new ScriptVar(0));
                    scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(arr);
                    return;
                }

                if (localesVar.IsString)
                {
                    var tag = NormalizeLocale(localesVar.String);
                    arr.AddChild(ScriptVar.IndexName(idx++), new ScriptVar(tag));
                }
                else if (localesVar.IsArray)
                {
                    var len = localesVar.GetArrayLength();
                    for (var i = 0; i < len; i++)
                    {
                        var item = localesVar.GetArrayIndex(i);
                        if (item != null && item.IsString)
                            arr.AddChild(ScriptVar.IndexName(idx++), new ScriptVar(NormalizeLocale(item.String)));
                    }
                }

                arr.AddChild("length", new ScriptVar(idx));
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(arr);
            });
            intl.AddChild("getCanonicalLocales", fn);
        }

        // ── Intl.Collator ────────────────────────────────────────────────────
        private static void RegisterCollator(ScriptVar intl, ScriptEngine engine)
        {
            var collatorCtor = MakeNative2("locale", "options", (scope, _) =>
            {
                var localeStr = scope.FindChild("locale")?.Var?.String ?? string.Empty;
                var culture = GetCulture(localeStr);

                var collatorObj = new ScriptVar(ScriptVar.Flags.Object);

                // compare(a, b) — returns negative, zero, or positive
                var compareFn = MakeNative2("a", "b", (cScope, cultureRef) =>
                {
                    var ci = (CultureInfo)cultureRef;
                    var sa = cScope.FindChild("a")?.Var?.String ?? string.Empty;
                    var sb = cScope.FindChild("b")?.Var?.String ?? string.Empty;
#pragma warning disable CA1309 // Culture-sensitive comparison is intentional: Intl.Collator must use locale-aware ordering
                    var result = string.Compare(sa, sb, ci, CompareOptions.None);
#pragma warning restore CA1309
                    cScope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(new ScriptVar(result));
                });
                compareFn.SetCallback(compareFn.GetCallback(), culture);
                collatorObj.AddChild("compare", compareFn);

                // resolvedOptions()
                var resolvedFn = MakeNative0((rScope, cultureRef) =>
                {
                    var ci = (CultureInfo)cultureRef;
                    var opts = new ScriptVar(ScriptVar.Flags.Object);
                    opts.AddChild("locale", new ScriptVar(ci.Name));
                    opts.AddChild("usage", new ScriptVar("sort"));
                    opts.AddChild("sensitivity", new ScriptVar("variant"));
                    rScope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(opts);
                });
                resolvedFn.SetCallback(resolvedFn.GetCallback(), culture);
                collatorObj.AddChild("resolvedOptions", resolvedFn);

                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(collatorObj);
            });
            intl.AddChild("Collator", collatorCtor);
        }

        // ── Intl.NumberFormat ────────────────────────────────────────────────
        private static void RegisterNumberFormat(ScriptVar intl, ScriptEngine engine)
        {
            var nfCtor = MakeNative2("locale", "options", (scope, _) =>
            {
                var localeStr = scope.FindChild("locale")?.Var?.String ?? string.Empty;
                var culture = GetCulture(localeStr);
                var options = scope.FindChild("options")?.Var;
                var style = options?.FindChild("style")?.Var?.String ?? "decimal";
                var currency = options?.FindChild("currency")?.Var?.String;
                var minFrac = options?.FindChild("minimumFractionDigits")?.Var?.Int ?? -1;
                var maxFrac = options?.FindChild("maximumFractionDigits")?.Var?.Int ?? -1;

                var nfObj = new ScriptVar(ScriptVar.Flags.Object);

                // format(number)
                var formatFn = MakeNative1("value", (fScope, ctx) =>
                {
                    var (ci, fStyle, fCurrency, fMinFrac, fMaxFrac) = ((CultureInfo, string, string, int, int))ctx;
                    var n = fScope.FindChild("value")?.Var?.Float ?? 0;
                    string result;
                    try
                    {
                        if (fStyle == "currency" && fCurrency != null)
                        {
                            var ri = new RegionInfo(ci.Name.Length > 0 ? ci.Name : "en-US");
                            result = n.ToString("C", ci);
                        }
                        else if (fStyle == "percent")
                        {
                            result = n.ToString("P", ci);
                        }
                        else
                        {
                            if (fMinFrac >= 0 || fMaxFrac >= 0)
                            {
                                var fmt = "N" + (fMaxFrac >= 0 ? fMaxFrac : (fMinFrac >= 0 ? fMinFrac : 2));
                                result = n.ToString(fmt, ci);
                            }
                            else
                            {
                                result = n.ToString("N", ci);
                            }
                        }
                    }
                    catch
                    {
                        result = n.ToString(CultureInfo.InvariantCulture);
                    }
                    fScope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(new ScriptVar(result));
                });
                formatFn.SetCallback(formatFn.GetCallback(), (culture, style, currency, minFrac, maxFrac));
                nfObj.AddChild("format", formatFn);

                // formatToParts(number) — simplified: returns [{type:"integer",value:n}]
                var formatToPartsFn = MakeNative1("value", (pScope, ctx) =>
                {
                    var (ci2, _, _, _, _) = ((CultureInfo, string, string, int, int))ctx;
                    var n = pScope.FindChild("value")?.Var?.Float ?? 0;
                    var arr = new ScriptVar(ScriptVar.Flags.Array);
                    var part = new ScriptVar(ScriptVar.Flags.Object);
                    part.AddChild("type", new ScriptVar("literal"));
                    part.AddChild("value", new ScriptVar(n.ToString(CultureInfo.InvariantCulture)));
                    arr.AddChild("0", part);
                    arr.AddChild("length", new ScriptVar(1));
                    pScope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(arr);
                });
                formatToPartsFn.SetCallback(formatToPartsFn.GetCallback(), (culture, style, currency, minFrac, maxFrac));
                nfObj.AddChild("formatToParts", formatToPartsFn);

                // resolvedOptions()
                var resolvedFn = MakeNative0((rScope, ctx) =>
                {
                    var (ci3, fStyle2, fCurrency2, fMinFrac2, fMaxFrac2) = ((CultureInfo, string, string, int, int))ctx;
                    var opts = new ScriptVar(ScriptVar.Flags.Object);
                    opts.AddChild("locale", new ScriptVar(ci3.Name.Length > 0 ? ci3.Name : "en-US"));
                    opts.AddChild("style", new ScriptVar(fStyle2));
                    opts.AddChild("minimumFractionDigits", new ScriptVar(fMinFrac2 >= 0 ? fMinFrac2 : 0));
                    opts.AddChild("maximumFractionDigits", new ScriptVar(fMaxFrac2 >= 0 ? fMaxFrac2 : 3));
                    rScope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(opts);
                });
                resolvedFn.SetCallback(resolvedFn.GetCallback(), (culture, style, currency, minFrac, maxFrac));
                nfObj.AddChild("resolvedOptions", resolvedFn);

                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(nfObj);
            });
            intl.AddChild("NumberFormat", nfCtor);
        }

        // ── Intl.DateTimeFormat ──────────────────────────────────────────────
        private static void RegisterDateTimeFormat(ScriptVar intl, ScriptEngine engine)
        {
            var dtfCtor = MakeNative2("locale", "options", (scope, _) =>
            {
                var localeStr = scope.FindChild("locale")?.Var?.String ?? string.Empty;
                var culture = GetCulture(localeStr);
                var options = scope.FindChild("options")?.Var;
                var dateStyle = options?.FindChild("dateStyle")?.Var?.String ?? "short";

                var dtfObj = new ScriptVar(ScriptVar.Flags.Object);

                // format(timestamp) — timestamp is milliseconds since Unix epoch
                var formatFn = MakeNative1("value", (fScope, ctx) =>
                {
                    var (ci, ds) = ((CultureInfo, string))ctx;
                    var ms = fScope.FindChild("value")?.Var?.Float ?? 0;
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime;
                    string result;
                    try
                    {
                        result = ds switch
                        {
                            "long" => dt.ToString("D", ci),
                            "medium" => dt.ToString("d", ci),
                            "full" => dt.ToString("F", ci),
                            _ => dt.ToString("d", ci)
                        };
                    }
                    catch
                    {
                        result = dt.ToString("d", CultureInfo.InvariantCulture);
                    }
                    fScope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(new ScriptVar(result));
                });
                formatFn.SetCallback(formatFn.GetCallback(), (culture, dateStyle));
                dtfObj.AddChild("format", formatFn);

                // formatToParts(timestamp) — simplified
                var formatToPartsFn = MakeNative1("value", (pScope, ctx) =>
                {
                    var (ci2, _) = ((CultureInfo, string))ctx;
                    var ms = pScope.FindChild("value")?.Var?.Float ?? 0;
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime;
                    var arr = new ScriptVar(ScriptVar.Flags.Array);
                    var part = new ScriptVar(ScriptVar.Flags.Object);
                    part.AddChild("type", new ScriptVar("literal"));
                    part.AddChild("value", new ScriptVar(dt.ToString("d", ci2)));
                    arr.AddChild("0", part);
                    arr.AddChild("length", new ScriptVar(1));
                    pScope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(arr);
                });
                formatToPartsFn.SetCallback(formatToPartsFn.GetCallback(), (culture, dateStyle));
                dtfObj.AddChild("formatToParts", formatToPartsFn);

                // resolvedOptions()
                var resolvedFn = MakeNative0((rScope, ctx) =>
                {
                    var (ci3, ds2) = ((CultureInfo, string))ctx;
                    var opts = new ScriptVar(ScriptVar.Flags.Object);
                    opts.AddChild("locale", new ScriptVar(ci3.Name.Length > 0 ? ci3.Name : "en-US"));
                    opts.AddChild("dateStyle", new ScriptVar(ds2));
                    rScope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(opts);
                });
                resolvedFn.SetCallback(resolvedFn.GetCallback(), (culture, dateStyle));
                dtfObj.AddChild("resolvedOptions", resolvedFn);

                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(dtfObj);
            });
            intl.AddChild("DateTimeFormat", dtfCtor);
        }

        // ── Intl.DisplayNames ────────────────────────────────────────────────
        private static void RegisterDisplayNames(ScriptVar intl, ScriptEngine engine)
        {
            var dnCtor = MakeNative2("locale", "options", (scope, _) =>
            {
                var localeStr = scope.FindChild("locale")?.Var?.String ?? string.Empty;
                var culture = GetCulture(localeStr);
                var options = scope.FindChild("options")?.Var;
                var type = options?.FindChild("type")?.Var?.String ?? "language";

                var dnObj = new ScriptVar(ScriptVar.Flags.Object);

                // of(code)
                var ofFn = MakeNative1("code", (oScope, ctx) =>
                {
                    var (ci, t) = ((CultureInfo, string))ctx;
                    var code = oScope.FindChild("code")?.Var?.String ?? string.Empty;
                    string result;
                    try
                    {
                        result = t switch
                        {
                            "language" => new CultureInfo(code).DisplayName,
                            "region" => new RegionInfo(code).DisplayName,
                            "script" => code,
                            "currency" => new RegionInfo(code).CurrencyEnglishName,
                            _ => code
                        };
                    }
                    catch
                    {
                        result = code;
                    }
                    oScope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(new ScriptVar(result));
                });
                ofFn.SetCallback(ofFn.GetCallback(), (culture, type));
                dnObj.AddChild("of", ofFn);

                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(dnObj);
            });
            intl.AddChild("DisplayNames", dnCtor);
        }

        // ── Intl.PluralRules ─────────────────────────────────────────────────
        private static void RegisterPluralRules(ScriptVar intl, ScriptEngine engine)
        {
            var prCtor = MakeNative2("locale", "options", (scope, _) =>
            {
                var localeStr = scope.FindChild("locale")?.Var?.String ?? string.Empty;
                var culture = GetCulture(localeStr);

                var prObj = new ScriptVar(ScriptVar.Flags.Object);

                // select(n) — returns "one", "two", "few", "many", "other"
                var selectFn = MakeNative1("value", (sScope, ctx) =>
                {
                    var ci = (CultureInfo)ctx;
                    var n = sScope.FindChild("value")?.Var?.Float ?? 0;
                    var plural = GetPluralCategory(n, ci);
                    sScope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(new ScriptVar(plural));
                });
                selectFn.SetCallback(selectFn.GetCallback(), culture);
                prObj.AddChild("select", selectFn);

                // resolvedOptions()
                var resolvedFn = MakeNative0((rScope, ctx) =>
                {
                    var ci2 = (CultureInfo)ctx;
                    var opts = new ScriptVar(ScriptVar.Flags.Object);
                    opts.AddChild("locale", new ScriptVar(ci2.Name.Length > 0 ? ci2.Name : "en-US"));
                    opts.AddChild("type", new ScriptVar("cardinal"));
                    rScope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(opts);
                });
                resolvedFn.SetCallback(resolvedFn.GetCallback(), culture);
                prObj.AddChild("resolvedOptions", resolvedFn);

                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(prObj);
            });
            intl.AddChild("PluralRules", prCtor);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string NormalizeLocale(string tag)
        {
            try
            {
                return CultureInfo.GetCultureInfo(tag).Name;
            }
            catch
            {
                return tag;
            }
        }

        private static CultureInfo GetCulture(string locale)
        {
            if (string.IsNullOrEmpty(locale)) return CultureInfo.InvariantCulture;
            try { return CultureInfo.GetCultureInfo(locale); }
            catch { return CultureInfo.InvariantCulture; }
        }

        private static string GetPluralCategory(double n, CultureInfo ci)
        {
            // Simplified English-centric plural rules; real Intl uses CLDR data
            var abs = Math.Abs(n);
            if (ci.Name.StartsWith("ar", StringComparison.Ordinal))
            {
                var mod100 = abs % 100;
                if (abs == 0) return "zero";
                if (abs == 1) return "one";
                if (abs == 2) return "two";
                if (mod100 >= 3 && mod100 <= 10) return "few";
                if (mod100 >= 11 && mod100 <= 99) return "many";
                return "other";
            }
            if (ci.Name.StartsWith("ru", StringComparison.Ordinal) ||
                ci.Name.StartsWith("uk", StringComparison.Ordinal))
            {
                var mod10 = abs % 10;
                var mod100 = abs % 100;
                if (mod10 == 1 && mod100 != 11) return "one";
                if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) return "few";
                return "many";
            }
            // Default: English-style (1 = "one", else "other")
            return abs == 1 ? "one" : "other";
        }

        private static ScriptVar MakeNative0(ScriptEngine.ScriptCallbackCB cb)
        {
            var fn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            fn.SetCallback(cb, null);
            return fn;
        }

        private static ScriptVar MakeNative1(string p1, ScriptEngine.ScriptCallbackCB cb)
        {
            var fn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            fn.AddChild(p1, new ScriptVar(ScriptVar.Flags.Undefined));
            fn.SetCallback(cb, null);
            return fn;
        }

        private static ScriptVar MakeNative2(string p1, string p2, ScriptEngine.ScriptCallbackCB cb)
        {
            var fn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            fn.AddChild(p1, new ScriptVar(ScriptVar.Flags.Undefined));
            fn.AddChild(p2, new ScriptVar(ScriptVar.Flags.Undefined));
            fn.SetCallback(cb, null);
            return fn;
        }
    }
}
