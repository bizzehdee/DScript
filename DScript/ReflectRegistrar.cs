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

using DScript.Vm;

namespace DScript
{
    internal static class ReflectRegistrar
    {
        internal static void Register(ScriptEngine engine)
        {
            var reflect = new ScriptVar(ScriptVar.Flags.Object);

            // Reflect.apply(fn, thisArg, argsList)
            var applyFn = MakeNative3("target", "thisArg", "argumentsList", (scope, _) =>
            {
                var target = scope.FindChild("target")?.Var;
                var thisArg = scope.FindChild("thisArg")?.Var;
                var argsList = scope.FindChild("argumentsList")?.Var;

                if (target == null || !target.IsFunction)
                    throw new ScriptException("TypeError: Reflect.apply: target must be a function");

                var args = SpreadArgs(argsList);
                var vm = new VirtualMachine(engine);
                var result = vm.InvokeCallable(target, thisArg, args);
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(result);
            });
            reflect.AddChild("apply", applyFn);

            // Reflect.construct(target, argumentsList, newTarget?)
            var constructFn = MakeNative3("target", "argumentsList", "newTarget", (scope, _) =>
            {
                var target = scope.FindChild("target")?.Var;
                var argsList = scope.FindChild("argumentsList")?.Var;

                if (target == null || !target.IsFunction)
                    throw new ScriptException("TypeError: Reflect.construct: target must be a function");

                var args = SpreadArgs(argsList);
                var instance = new ScriptVar(ScriptVar.Flags.Object);

                // Set prototype from target.prototype
                var proto = target.FindChild(ScriptVar.PrototypeClassName)?.Var;
                if (proto != null)
                    instance.AddChild(ScriptVar.PrototypeClassName, proto);

                var vm = new VirtualMachine(engine);
                var ctorResult = vm.InvokeCallable(target, instance, args);

                // If ctor returns an object, use it; otherwise use the created instance
                var result = (ctorResult != null && ctorResult.IsObject) ? ctorResult : instance;
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(result);
            });
            reflect.AddChild("construct", constructFn);

            // Reflect.get(target, propertyKey, receiver?)
            var getFn = MakeNative3("target", "propertyKey", "receiver", (scope, _) =>
            {
                var target = scope.FindChild("target")?.Var;
                var key = scope.FindChild("propertyKey")?.Var?.String ?? "undefined";

                if (target == null)
                    throw new ScriptException("TypeError: Reflect.get: target must be an object");

                var link = target.FindChild(key);
                var result = link?.Var ?? new ScriptVar(ScriptVar.Flags.Undefined);
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(result);
            });
            reflect.AddChild("get", getFn);

            // Reflect.set(target, propertyKey, value, receiver?)
            var setFn = MakeNative4("target", "propertyKey", "value", "receiver", (scope, _) =>
            {
                var target = scope.FindChild("target")?.Var;
                var key = scope.FindChild("propertyKey")?.Var?.String ?? "undefined";
                var value = scope.FindChild("value")?.Var ?? new ScriptVar();

                if (target == null)
                    throw new ScriptException("TypeError: Reflect.set: target must be an object");

                var link = target.FindChild(key);
                if (link != null)
                    link.ReplaceWith(value);
                else
                    target.AddChild(key, value);

                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(new ScriptVar(true));
            });
            reflect.AddChild("set", setFn);

            // Reflect.has(target, propertyKey) — like the `in` operator
            var hasFn = MakeNative2("target", "propertyKey", (scope, _) =>
            {
                var target = scope.FindChild("target")?.Var;
                var key = scope.FindChild("propertyKey")?.Var?.String ?? "undefined";

                if (target == null)
                    throw new ScriptException("TypeError: Reflect.has: target must be an object");

                var found = target.FindChild(key) != null;
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(new ScriptVar(found));
            });
            reflect.AddChild("has", hasFn);

            // Reflect.deleteProperty(target, propertyKey)
            var deleteFn = MakeNative2("target", "propertyKey", (scope, _) =>
            {
                var target = scope.FindChild("target")?.Var;
                var key = scope.FindChild("propertyKey")?.Var?.String ?? "undefined";

                if (target == null)
                    throw new ScriptException("TypeError: Reflect.deleteProperty: target must be an object");

                var link = target.FindChild(key);
                var success = link != null;
                if (success)
                {
                    target.RemoveLink(link);
                    link.Dispose();
                }
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(new ScriptVar(success));
            });
            reflect.AddChild("deleteProperty", deleteFn);

            // Reflect.ownKeys(target) — returns array of own property names
            var ownKeysFn = MakeNative1("target", (scope, _) =>
            {
                var target = scope.FindChild("target")?.Var;
                if (target == null)
                    throw new ScriptException("TypeError: Reflect.ownKeys: target must be an object");

                var arr = new ScriptVar(ScriptVar.Flags.Array);
                int idx = 0;
                var link = target.FirstChild;
                while (link != null)
                {
                    if (link.Name != ScriptVar.PrototypeClassName)
                        arr.AddChild(ScriptVar.IndexName(idx++), new ScriptVar(link.Name));
                    link = link.Next;
                }
                arr.AddChild("length", new ScriptVar(idx));
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(arr);
            });
            reflect.AddChild("ownKeys", ownKeysFn);

            // Reflect.defineProperty(target, propertyKey, descriptor)
            var definePropFn = MakeNative3("target", "propertyKey", "descriptor", (scope, _) =>
            {
                var target = scope.FindChild("target")?.Var;
                var key = scope.FindChild("propertyKey")?.Var?.String ?? "undefined";
                var desc = scope.FindChild("descriptor")?.Var;

                if (target == null)
                    throw new ScriptException("TypeError: Reflect.defineProperty: target must be an object");

                var val = desc?.FindChild("value")?.Var ?? new ScriptVar(ScriptVar.Flags.Undefined);
                var link = target.FindChild(key);
                if (link != null)
                    link.ReplaceWith(val);
                else
                    target.AddChild(key, val);

                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(new ScriptVar(true));
            });
            reflect.AddChild("defineProperty", definePropFn);

            // Reflect.getOwnPropertyDescriptor(target, propertyKey)
            var getDescFn = MakeNative2("target", "propertyKey", (scope, _) =>
            {
                var target = scope.FindChild("target")?.Var;
                var key = scope.FindChild("propertyKey")?.Var?.String ?? "undefined";

                if (target == null)
                    throw new ScriptException("TypeError: Reflect.getOwnPropertyDescriptor: target must be an object");

                var link = target.FindChild(key);
                if (link == null)
                {
                    scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(new ScriptVar(ScriptVar.Flags.Undefined));
                    return;
                }

                var desc = new ScriptVar(ScriptVar.Flags.Object);
                desc.AddChild("value", link.Var);
                desc.AddChild("writable", new ScriptVar(!link.IsConst));
                desc.AddChild("enumerable", new ScriptVar(true));
                desc.AddChild("configurable", new ScriptVar(true));
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(desc);
            });
            reflect.AddChild("getOwnPropertyDescriptor", getDescFn);

            // Reflect.getPrototypeOf(target)
            var getProtoFn = MakeNative1("target", (scope, _) =>
            {
                var target = scope.FindChild("target")?.Var;
                if (target == null)
                    throw new ScriptException("TypeError: Reflect.getPrototypeOf: target must be an object");

                var proto = target.FindChild(ScriptVar.PrototypeClassName)?.Var
                            ?? new ScriptVar(ScriptVar.Flags.Null);
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(proto);
            });
            reflect.AddChild("getPrototypeOf", getProtoFn);

            // Reflect.setPrototypeOf(target, proto)
            var setProtoFn = MakeNative2("target", "proto", (scope, _) =>
            {
                var target = scope.FindChild("target")?.Var;
                var proto = scope.FindChild("proto")?.Var;

                if (target == null)
                    throw new ScriptException("TypeError: Reflect.setPrototypeOf: target must be an object");

                var existing = target.FindChild(ScriptVar.PrototypeClassName);
                if (proto == null || proto.IsNull || proto.IsUndefined)
                {
                    if (existing != null) { target.RemoveLink(existing); existing.Dispose(); }
                }
                else
                {
                    if (existing != null)
                        existing.ReplaceWith(proto);
                    else
                        target.AddChild(ScriptVar.PrototypeClassName, proto);
                }
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(new ScriptVar(true));
            });
            reflect.AddChild("setPrototypeOf", setProtoFn);

            // Reflect.isExtensible — always returns true (DScript objects are always extensible)
            var isExtFn = MakeNative1("target", (scope, _) =>
            {
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(new ScriptVar(true));
            });
            reflect.AddChild("isExtensible", isExtFn);

            // Reflect.preventExtensions — no-op, returns true
            var preventFn = MakeNative1("target", (scope, _) =>
            {
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(new ScriptVar(true));
            });
            reflect.AddChild("preventExtensions", preventFn);

            engine.Root.AddChild("Reflect", reflect);
        }

        private static ScriptVar[] SpreadArgs(ScriptVar argsList)
        {
            if (argsList == null || !argsList.IsArray) return [];
            var len = argsList.GetArrayLength();
            var args = new ScriptVar[len];
            for (var i = 0; i < len; i++)
                args[i] = argsList.GetArrayIndex(i) ?? new ScriptVar();
            return args;
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

        private static ScriptVar MakeNative3(string p1, string p2, string p3, ScriptEngine.ScriptCallbackCB cb)
        {
            var fn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            fn.AddChild(p1, new ScriptVar(ScriptVar.Flags.Undefined));
            fn.AddChild(p2, new ScriptVar(ScriptVar.Flags.Undefined));
            fn.AddChild(p3, new ScriptVar(ScriptVar.Flags.Undefined));
            fn.SetCallback(cb, null);
            return fn;
        }

        private static ScriptVar MakeNative4(string p1, string p2, string p3, string p4, ScriptEngine.ScriptCallbackCB cb)
        {
            var fn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            fn.AddChild(p1, new ScriptVar(ScriptVar.Flags.Undefined));
            fn.AddChild(p2, new ScriptVar(ScriptVar.Flags.Undefined));
            fn.AddChild(p3, new ScriptVar(ScriptVar.Flags.Undefined));
            fn.AddChild(p4, new ScriptVar(ScriptVar.Flags.Undefined));
            fn.SetCallback(cb, null);
            return fn;
        }
    }
}
