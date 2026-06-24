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

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("Object")]
    public static class ObjectFunctionProvider
    {

        [ScriptMethod("dump")]
        public static void ObjectDumpImpl(ScriptVar var, object userData)
        {
            var.GetParameter("this").Trace(0, null);
        }

        [ScriptMethod("clone")]
        public static void ObjectCloneImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("this");
            var.ReturnVar.CopyValue(obj);
        }

        [ScriptMethod("keys", "obj")]
        public static void ObjectKeysImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var.ReturnVar.SetArray();
            var idx = 0;
            ProviderHelpers.ForEachEnumerableChild(obj, link =>
                var.ReturnVar.SetArrayIndex(idx++, ScriptVar.FromString(link.Name)));
        }

        [ScriptMethod("hasOwnProperty", "name")]
        public static void ObjectHasOwnPropertyImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("this");
            var name = var.GetParameter("name").String;

            var.ReturnVar.Int = obj.FindChild(name) != null ? 1 : 0;
        }

        [ScriptMethod("values", "obj")]
        public static void ObjectValuesImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var.ReturnVar.SetArray();
            var idx = 0;
            ProviderHelpers.ForEachEnumerableChild(obj, link =>
                var.ReturnVar.SetArrayIndex(idx++, link.Var.DeepCopy()));
        }

        [ScriptMethod("entries", "obj")]
        public static void ObjectEntriesImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var.ReturnVar.SetArray();
            var idx = 0;
            ProviderHelpers.ForEachEnumerableChild(obj, link =>
            {
                var pair = ScriptVar.CreateUndefined();
                pair.SetArray();
                pair.SetArrayIndex(0, ScriptVar.FromString(link.Name));
                pair.SetArrayIndex(1, link.Var.DeepCopy());
                var.ReturnVar.SetArrayIndex(idx++, pair);
            });
        }

        [ScriptMethod("assign", "target", "s0", "s1", "s2", "s3")]
        public static void ObjectAssignImpl(ScriptVar var, object userData)
        {
            var target = var.GetParameter("target");
            foreach (var name in new[] { "s0", "s1", "s2", "s3" })
            {
                var source = var.GetParameter(name);
                if (source.IsUndefined || source.IsNull) continue;
                ProviderHelpers.ForEachOwnChild(source, link =>
                    target.AddChildNoDup(link.Name, link.Var.DeepCopy()));
            }
            var.ReturnVar = target;
        }

        [ScriptMethod("fromEntries", "entries")]
        public static void ObjectFromEntriesImpl(ScriptVar var, object userData)
        {
            var entries = var.GetParameter("entries");
            var result = ScriptVar.CreateUndefined();
            var len = entries.GetArrayLength();
            for (var i = 0; i < len; i++)
            {
                var pair = entries.GetArrayIndex(i);
                var key = pair.GetArrayIndex(0).String;
                var val = pair.GetArrayIndex(1).DeepCopy();
                result.AddChildNoDup(key, val);
            }
            var.ReturnVar = result;
        }

        [ScriptMethod("freeze", "obj")]
        public static void ObjectFreezeImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            obj.FreezeSelf();
            var.ReturnVar = obj;
        }

        [ScriptMethod("isFrozen", "obj")]
        public static void ObjectIsFrozenImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var.ReturnVar.Int = obj.IsFrozen ? 1 : 0;
        }

        [ScriptMethod("create", "proto")]
        public static void ObjectCreateImpl(ScriptVar var, object userData)
        {
            var proto = var.GetParameter("proto");
            var result = ScriptVar.CreateUndefined();
            if (!proto.IsNull && !proto.IsUndefined)
                result.AddChildNoDup(ScriptVar.PrototypeClassName, proto);
            var.ReturnVar = result;
        }

        [ScriptMethod("getOwnPropertyNames", "obj")]
        public static void ObjectGetOwnPropertyNamesImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var.ReturnVar.SetArray();
            var idx = 0;
            ProviderHelpers.ForEachOwnChild(obj, link =>
                var.ReturnVar.SetArrayIndex(idx++, ScriptVar.FromString(link.Name)));
        }

        [ScriptMethod("hasOwn", "obj", "key")]
        public static void ObjectHasOwnImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var key = var.GetParameter("key").String;
            var.ReturnVar.Int = obj.FindChild(key) != null ? 1 : 0;
        }

        [ScriptMethod("is", "a", "b")]
        public static void ObjectIsImpl(ScriptVar var, object userData)
        {
            var a = var.GetParameter("a");
            var b = var.GetParameter("b");

            if (a.IsDouble || b.IsDouble)
            {
                var da = a.Float;
                var db = b.Float;
                if (double.IsNaN(da) && double.IsNaN(db)) { var.ReturnVar.Int = 1; return; }
                // distinguish +0 and -0
                if (da == 0.0 && db == 0.0)
                {
                    var.ReturnVar.Int = (1.0 / da == 1.0 / db) ? 1 : 0;
                    return;
                }
                var.ReturnVar.Int = da == db ? 1 : 0;
                return;
            }

            var.ReturnVar.Int = a.Equal(b) ? 1 : 0;
        }

        [ScriptMethod("seal", "obj")]
        public static void ObjectSealImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            obj.SealSelf();
            var.ReturnVar = obj;
        }

        [ScriptMethod("isSealed", "obj")]
        public static void ObjectIsSealedImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var.ReturnVar.Int = obj.IsSealed ? 1 : 0;
        }

        [ScriptMethod("isExtensible", "obj")]
        public static void ObjectIsExtensibleImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var.ReturnVar.Int = obj.IsExtensible ? 1 : 0;
        }

        [ScriptMethod("preventExtensions", "obj")]
        public static void ObjectPreventExtensionsImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            obj.PreventExtensionsSelf();
            var.ReturnVar = obj;
        }

        [ScriptMethod("getPrototypeOf", "obj")]
        public static void ObjectGetPrototypeOfImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var proto = obj.FindChild(ScriptVar.PrototypeClassName);
            var.ReturnVar = proto != null ? proto.Var : ScriptVar.CreateUndefined();  // null if no prototype
        }

        [ScriptMethod("setPrototypeOf", "obj", "proto")]
        public static void ObjectSetPrototypeOfImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var proto = var.GetParameter("proto");
            var existing = obj.FindChild(ScriptVar.PrototypeClassName);
            if (existing != null)
                existing.ReplaceWith(proto);
            else
                obj.AddChildNoDup(ScriptVar.PrototypeClassName, proto);
            var.ReturnVar = obj;
        }

        [ScriptMethod("getOwnPropertyDescriptor", "obj", "key")]
        public static void ObjectGetOwnPropertyDescriptorImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var key = var.GetParameter("key").String;
            var link = obj.FindChild(key);
            if (link == null) { var.ReturnVar.SetUndefined(); return; }
            var.ReturnVar = BuildDescriptor(link);
        }

        [ScriptMethod("getOwnPropertyDescriptors", "obj")]
        public static void ObjectGetOwnPropertyDescriptorsImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var result = ScriptVar.CreateUndefined();
            ProviderHelpers.ForEachOwnChild(obj, link => result.AddChildNoDup(link.Name, BuildDescriptor(link)));
            var.ReturnVar = result;
        }

        private static ScriptVar BuildDescriptor(ScriptVarLink link)
        {
            var desc = ScriptVar.CreateUndefined();
            if (link.IsAccessor)
            {
                desc.AddChildNoDup("get", link.Getter ?? ScriptVar.CreateUndefined());
                desc.AddChildNoDup("set", link.Setter ?? ScriptVar.CreateUndefined());
            }
            else
            {
                desc.AddChildNoDup("value", link.Var.DeepCopy());
                desc.AddChildNoDup("writable", ScriptVar.FromInt(link.Writable ? 1 : 0));
            }
            desc.AddChildNoDup("enumerable", ScriptVar.FromInt(link.Enumerable ? 1 : 0));
            desc.AddChildNoDup("configurable", ScriptVar.FromInt(link.Configurable ? 1 : 0));
            return desc;
        }

        [ScriptMethod("defineProperty", "obj", "key", "descriptor")]
        public static void ObjectDefinePropertyImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            ApplyDescriptor(obj, var.GetParameter("key").String, var.GetParameter("descriptor"));
            var.ReturnVar = obj;
        }

        [ScriptMethod("defineProperties", "obj", "descriptors")]
        public static void ObjectDefinePropertiesImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            ProviderHelpers.ForEachOwnChild(var.GetParameter("descriptors"),
                link => ApplyDescriptor(obj, link.Name, link.Var));
            var.ReturnVar = obj;
        }

        private static void ApplyDescriptor(ScriptVar obj, string key, ScriptVar desc)
        {
            var link = obj.FindChild(key);
            var getterLink = desc.FindChild("get");
            var setterLink = desc.FindChild("set");

            if (getterLink != null || setterLink != null)
            {
                if (link == null) link = obj.AddChild(key, ScriptVar.CreateUndefined());
                if (getterLink != null) link.Getter = getterLink.Var;
                if (setterLink != null) link.Setter = setterLink.Var;
            }
            else
            {
                var valueLink = desc.FindChild("value");
                if (valueLink != null)
                {
                    if (link == null) link = obj.AddChild(key, valueLink.Var.DeepCopy());
                    else link.ReplaceWith(valueLink.Var);
                }
                else if (link == null)
                {
                    link = obj.AddChild(key, ScriptVar.CreateUndefined());
                }
                var writableLink = desc.FindChild("writable");
                if (writableLink != null) link.Writable = writableLink.Var.Bool;
            }

            var enumerableLink = desc.FindChild("enumerable");
            if (enumerableLink != null) link.Enumerable = enumerableLink.Var.Bool;
            var configurableLink = desc.FindChild("configurable");
            if (configurableLink != null) link.Configurable = configurableLink.Var.Bool;
        }

        [ScriptMethod("groupBy", "arr", "keyFn")]
        public static void ObjectGroupByImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var arr = var.GetParameter("arr");
            var keyFn = var.GetParameter("keyFn");
            var len = arr.GetArrayLength();

            var result = ScriptVar.CreateObject();
            for (var i = 0; i < len; i++)
            {
                var elem = arr.GetArrayIndex(i);
                var key = engine.CallFunction(keyFn, null, elem, ScriptVar.FromInt(i)).String;

                var group = result.FindChild(key);
                if (group == null)
                {
                    var newArr = ScriptVar.CreateUndefined();
                    newArr.SetArray();
                    result.AddChild(key, newArr);
                    group = result.FindChild(key);
                }
                var groupLen = group.Var.GetArrayLength();
                group.Var.SetArrayIndex(groupLen, elem.DeepCopy());
            }
            var.ReturnVar = result;
        }
    }
}
