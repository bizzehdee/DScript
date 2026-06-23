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

namespace DScript.Extras
{
    internal static class EventEmitterRegistrar
    {
        private const string ListenerPrefix = "__ev_";
        private const string OncePrefix = "__evonce_";
        private const int DefaultMaxListeners = 10;

        internal static void Register(ScriptEngine engine)
        {
            var emitterCtorVar = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            emitterCtorVar.SetCallback((scope, _) =>
            {
                // Constructor body is empty — methods come from __proto__ chain.
            }, null);

            // .on(event, fn)
            AddMethod(emitterCtorVar, engine, "on", new[] { "event", "fn" }, (scope, eng) =>
            {
                var thisVar = scope.FindChild("this")?.Var;
                if (thisVar == null) return;
                var eventName = scope.FindChild("event")?.Var?.String ?? "";
                var fn = scope.FindChild("fn")?.Var;
                if (fn == null || !fn.IsFunction) return;
                var arr = GetOrCreateListenerArray(thisVar, ListenerPrefix + eventName);
                arr.SetArrayIndex(arr.GetArrayLength(), fn.DeepCopy());
                WarnIfExceedsMax(thisVar, eventName);
            });

            // .once(event, fn)
            AddMethod(emitterCtorVar, engine, "once", new[] { "event", "fn" }, (scope, eng) =>
            {
                var thisVar = scope.FindChild("this")?.Var;
                if (thisVar == null) return;
                var eventName = scope.FindChild("event")?.Var?.String ?? "";
                var fn = scope.FindChild("fn")?.Var;
                if (fn == null || !fn.IsFunction) return;
                var arr = GetOrCreateListenerArray(thisVar, OncePrefix + eventName);
                arr.SetArrayIndex(arr.GetArrayLength(), fn.DeepCopy());
            });

            // .off(event, fn)
            AddMethod(emitterCtorVar, engine, "off", new[] { "event", "fn" }, (scope, eng) =>
            {
                var thisVar = scope.FindChild("this")?.Var;
                if (thisVar == null) return;
                var eventName = scope.FindChild("event")?.Var?.String ?? "";
                var fn = scope.FindChild("fn")?.Var;
                var arr = thisVar.FindChild(ListenerPrefix + eventName);
                if (arr == null || fn == null) return;
                // Remove first matching listener
                RemoveFirstMatch(arr.Var, fn);
            });

            // .removeAllListeners(event?)
            AddMethod(emitterCtorVar, engine, "removeAllListeners", new[] { "event" }, (scope, eng) =>
            {
                var thisVar = scope.FindChild("this")?.Var;
                if (thisVar == null) return;
                var eventVar = scope.FindChild("event")?.Var;
                if (eventVar == null || eventVar.IsUndefined)
                {
                    // Remove all listener arrays
                    var toRemove = new System.Collections.Generic.List<string>();
                    var link = thisVar.FirstChild;
                    while (link != null)
                    {
                        if (link.Name.StartsWith(ListenerPrefix) || link.Name.StartsWith(OncePrefix))
                            toRemove.Add(link.Name);
                        link = link.Next;
                    }
                    foreach (var n in toRemove)
                    {
                        var lnk = thisVar.FindChild(n);
                        if (lnk != null) thisVar.RemoveLink(lnk);
                    }
                }
                else
                {
                    var eventName = eventVar.String;
                    var lnk1 = thisVar.FindChild(ListenerPrefix + eventName);
                    if (lnk1 != null) thisVar.RemoveLink(lnk1);
                    var lnk2 = thisVar.FindChild(OncePrefix + eventName);
                    if (lnk2 != null) thisVar.RemoveLink(lnk2);
                }
            });

            // .emit(event, arg0, arg1)
            AddMethod(emitterCtorVar, engine, "emit", new[] { "event", "arg0", "arg1" }, (scope, eng) =>
            {
                var thisVar = scope.FindChild("this")?.Var;
                if (thisVar == null) { scope.ReturnVar = new ScriptVar(false); return; }
                var eventName = scope.FindChild("event")?.Var?.String ?? "";
                var a0 = scope.FindChild("arg0")?.Var ?? new ScriptVar(ScriptVar.Flags.Undefined);
                var a1 = scope.FindChild("arg1")?.Var ?? new ScriptVar(ScriptVar.Flags.Undefined);
                var called = false;
                var arrLink = thisVar.FindChild(ListenerPrefix + eventName);
                if (arrLink != null)
                {
                    var len = arrLink.Var.GetArrayLength();
                    for (var i = 0; i < len; i++)
                    {
                        var fn = arrLink.Var.GetArrayIndex(i);
                        if (!fn.IsFunction) continue;
                        called = true;
                        if (!a0.IsUndefined && !a1.IsUndefined)
                            eng.CallFunction(fn, thisVar, a0, a1);
                        else if (!a0.IsUndefined)
                            eng.CallFunction(fn, thisVar, a0);
                        else
                            eng.CallFunction(fn, thisVar);
                    }
                }
                // Fire once listeners and clear them
                var onceLink = thisVar.FindChild(OncePrefix + eventName);
                if (onceLink != null)
                {
                    var onceLen = onceLink.Var.GetArrayLength();
                    for (var i = 0; i < onceLen; i++)
                    {
                        var fn = onceLink.Var.GetArrayIndex(i);
                        if (!fn.IsFunction) continue;
                        called = true;
                        if (!a0.IsUndefined && !a1.IsUndefined)
                            eng.CallFunction(fn, thisVar, a0, a1);
                        else if (!a0.IsUndefined)
                            eng.CallFunction(fn, thisVar, a0);
                        else
                            eng.CallFunction(fn, thisVar);
                    }
                    onceLink.Var.RemoveAllChildren();
                }
                scope.ReturnVar = new ScriptVar(called);
            });

            // .listeners(event)
            AddMethod(emitterCtorVar, engine, "listeners", new[] { "event" }, (scope, eng) =>
            {
                var thisVar = scope.FindChild("this")?.Var;
                if (thisVar == null) { scope.ReturnVar = EmptyArray(); return; }
                var eventName = scope.FindChild("event")?.Var?.String ?? "";
                var arrLink = thisVar.FindChild(ListenerPrefix + eventName);
                scope.ReturnVar = arrLink?.Var ?? EmptyArray();
            });

            // .listenerCount(event)
            AddMethod(emitterCtorVar, engine, "listenerCount", new[] { "event" }, (scope, eng) =>
            {
                var thisVar = scope.FindChild("this")?.Var;
                if (thisVar == null) { scope.ReturnVar = new ScriptVar(0); return; }
                var eventName = scope.FindChild("event")?.Var?.String ?? "";
                var arrLink = thisVar.FindChild(ListenerPrefix + eventName);
                scope.ReturnVar = new ScriptVar(arrLink?.Var.GetArrayLength() ?? 0);
            });

            emitterCtorVar.AddChild("defaultMaxListeners", new ScriptVar(DefaultMaxListeners));

            engine.Root.AddChild("EventEmitter", emitterCtorVar);
        }

        private static void AddMethod(ScriptVar ctorVar, ScriptEngine engine, string name, string[] paramNames,
            System.Action<ScriptVar, ScriptEngine> body)
        {
            var fn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            foreach (var p in paramNames)
                fn.AddChild(p, new ScriptVar(ScriptVar.Flags.Undefined));
            var eng = engine;
            fn.SetCallback((scope, _) => body(scope, eng), null);
            ctorVar.AddChild(name, fn);
        }

        private static ScriptVar GetOrCreateListenerArray(ScriptVar thisVar, string key)
        {
            var link = thisVar.FindChild(key);
            if (link != null) return link.Var;
            var arr = new ScriptVar();
            arr.SetArray();
            thisVar.AddChild(key, arr);
            return thisVar.FindChild(key)!.Var;
        }

        private static void RemoveFirstMatch(ScriptVar arr, ScriptVar fn)
        {
            var len = arr.GetArrayLength();
            for (var i = 0; i < len; i++)
            {
                var elem = arr.GetArrayIndex(i);
                if (elem == fn || elem.Equal(fn))
                {
                    // Shift remaining elements down
                    for (var j = i; j < len - 1; j++)
                        arr.SetArrayIndex(j, arr.GetArrayIndex(j + 1));
                    arr.SetArrayIndex(len - 1, new ScriptVar(ScriptVar.Flags.Undefined));
                    // Update length
                    var lenLink = arr.FindChild("length");
                    if (lenLink != null) lenLink.Var.Int = len - 1;
                    return;
                }
            }
        }

        private static void WarnIfExceedsMax(ScriptVar thisVar, string eventName)
        {
            var arrLink = thisVar.FindChild(ListenerPrefix + eventName);
            if (arrLink == null) return;
            var count = arrLink.Var.GetArrayLength();
            if (count > DefaultMaxListeners)
                System.Console.Error.WriteLine(
                    $"MaxListenersExceededWarning: {count} listeners added for '{eventName}'. Use emitter.setMaxListeners() to increase limit.");
        }

        private static ScriptVar EmptyArray()
        {
            var arr = new ScriptVar();
            arr.SetArray();
            return arr;
        }
    }
}
