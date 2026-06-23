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

using System.Collections.Generic;
using DScript.Vm;

namespace DScript
{
    internal static class SymbolRegistrar
    {
        private sealed class SymbolRegistry
        {
            public readonly Dictionary<string, ScriptVar> KeyToSymbol = new();
            public readonly Dictionary<string, string> SymbolKeyToKey = new();
        }

        internal static void Register(ScriptEngine engine)
        {
            var registry = new SymbolRegistry();

            // Symbol(description?) — ordinary call creates a new symbol.
            // new Symbol() throws TypeError.
            var symbolCtor = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            symbolCtor.AddChild("description", new ScriptVar(ScriptVar.Flags.Undefined));
            symbolCtor.SetCallback((scope, _) =>
            {
                var thisVar = scope.FindChild("this")?.Var;
                if (thisVar != null && thisVar.IsObject)
                    throw new ScriptException("TypeError: Symbol is not a constructor");
                var descVar = scope.FindChild("description")?.Var;
                string desc = (descVar != null && !descVar.IsUndefined) ? descVar.String : null;
                var sym = ScriptVar.CreateSymbol(desc);
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(sym);
            }, null);

            // Symbol.prototype.description getter — returns the raw description string
            // (or undefined for anonymous symbols) when accessed on a symbol instance.
            var descGetter = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            descGetter.SetCallback((scope, _) =>
            {
                var sym = scope.FindChild("this")?.Var;
                if (sym == null || !sym.IsSymbol)
                    throw new ScriptException("TypeError: Symbol.prototype.description requires a Symbol");
                var rawDesc = sym.GetSymbolDescription();
                var result = rawDesc != null
                    ? new ScriptVar(rawDesc)
                    : new ScriptVar(ScriptVar.Flags.Undefined);
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(result);
            }, null);
            var descLink = symbolCtor.FindChild("description") ?? symbolCtor.AddChild("description", new ScriptVar());
            descLink.Getter = descGetter;

            // Well-known symbols exposed as static properties
            symbolCtor.AddChild("iterator",    WellKnownSymbols.Iterator);
            symbolCtor.AddChild("hasInstance", WellKnownSymbols.HasInstance);
            symbolCtor.AddChild("toPrimitive", WellKnownSymbols.ToPrimitive);
            symbolCtor.AddChild("toStringTag", WellKnownSymbols.ToStringTag);

            // Symbol.for(key) — global symbol registry
            var forFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            forFn.AddChild("key", new ScriptVar(ScriptVar.Flags.Undefined));
            forFn.SetCallback((scope, reg) =>
            {
                var r = (SymbolRegistry)reg;
                var keyVar = scope.FindChild("key")?.Var;
                var key = keyVar != null ? keyVar.String : "undefined";
                if (!r.KeyToSymbol.TryGetValue(key, out var sym))
                {
                    sym = ScriptVar.CreateSymbol(key);
                    r.KeyToSymbol[key] = sym;
                    r.SymbolKeyToKey[sym.GetSymbolKey()] = key;
                }
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(sym);
            }, registry);
            symbolCtor.AddChild("for", forFn);

            // Symbol.keyFor(sym) — retrieve key for a registry symbol
            var keyForFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            keyForFn.AddChild("sym", new ScriptVar(ScriptVar.Flags.Undefined));
            keyForFn.SetCallback((scope, reg) =>
            {
                var r = (SymbolRegistry)reg;
                var symVar = scope.FindChild("sym")?.Var;
                if (symVar == null || !symVar.IsSymbol)
                    throw new ScriptException("TypeError: Symbol.keyFor requires a Symbol");
                var symKey = symVar.GetSymbolKey();
                ScriptVar result = r.SymbolKeyToKey.TryGetValue(symKey, out var k)
                    ? new ScriptVar(k)
                    : new ScriptVar(ScriptVar.Flags.Undefined);
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(result);
            }, registry);
            symbolCtor.AddChild("keyFor", keyForFn);

            engine.Root.AddChild("Symbol", symbolCtor);
        }
    }
}
