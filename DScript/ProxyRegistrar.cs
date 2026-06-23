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

namespace DScript
{
    internal static class ProxyRegistrar
    {
        internal static void Register(ScriptEngine engine)
        {
            // new Proxy(target, handler)
            var proxyCtor = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            proxyCtor.AddChild("target", new ScriptVar(ScriptVar.Flags.Undefined));
            proxyCtor.AddChild("handler", new ScriptVar(ScriptVar.Flags.Undefined));
            proxyCtor.SetCallback((scope, _) =>
            {
                var target = scope.FindChild("target")?.Var;
                var handler = scope.FindChild("handler")?.Var;

                if (target == null || (!target.IsObject && !target.IsFunction && !target.IsArray))
                    throw new ScriptException("TypeError: Cannot create proxy with a non-object as target");
                if (handler == null || !handler.IsObject)
                    throw new ScriptException("TypeError: Cannot create proxy with a non-object as handler");

                var proxy = ScriptVar.CreateProxy(target, handler);
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(proxy);
            }, null);

            // Proxy.revocable(target, handler) — returns { proxy, revoke }
            var revocableFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            revocableFn.AddChild("target", new ScriptVar(ScriptVar.Flags.Undefined));
            revocableFn.AddChild("handler", new ScriptVar(ScriptVar.Flags.Undefined));
            revocableFn.SetCallback((scope, _) =>
            {
                var target = scope.FindChild("target")?.Var;
                var handler = scope.FindChild("handler")?.Var;

                if (target == null || (!target.IsObject && !target.IsFunction && !target.IsArray))
                    throw new ScriptException("TypeError: Cannot create proxy with a non-object as target");
                if (handler == null || !handler.IsObject)
                    throw new ScriptException("TypeError: Cannot create proxy with a non-object as handler");

                var proxy = ScriptVar.CreateProxy(target, handler);

                // revoke() nulls out the handler, making the proxy throw on any trap
                var revokeFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
                revokeFn.SetCallback((rScope, proxyRef) =>
                {
                    var p = (ScriptVar)proxyRef;
                    var handlerLink = p.FindChild("[[ProxyHandler]]");
                    handlerLink?.ReplaceWith(new ScriptVar(ScriptVar.Flags.Null));
                    // Also null target to prevent any further access
                    var targetLink = p.FindChild("[[ProxyTarget]]");
                    targetLink?.ReplaceWith(new ScriptVar(ScriptVar.Flags.Null));
                }, proxy);

                var result = new ScriptVar(ScriptVar.Flags.Object);
                result.AddChild("proxy", proxy);
                result.AddChild("revoke", revokeFn);
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(result);
            }, null);

            proxyCtor.AddChild("revocable", revocableFn);
            engine.Root.AddChild("Proxy", proxyCtor);
        }
    }
}
