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
            var link = obj.FirstChild;
            while (link != null)
            {
                if (link.Name != ScriptVar.PrototypeClassName)
                {
                    var.ReturnVar.SetArrayIndex(idx++, new ScriptVar(link.Name));
                }
                link = link.Next;
            }
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
            var link = obj.FirstChild;
            while (link != null)
            {
                if (link.Name != ScriptVar.PrototypeClassName)
                    var.ReturnVar.SetArrayIndex(idx++, link.Var.DeepCopy());
                link = link.Next;
            }
        }

        [ScriptMethod("entries", "obj")]
        public static void ObjectEntriesImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var.ReturnVar.SetArray();
            var idx = 0;
            var link = obj.FirstChild;
            while (link != null)
            {
                if (link.Name != ScriptVar.PrototypeClassName)
                {
                    var pair = new ScriptVar();
                    pair.SetArray();
                    pair.SetArrayIndex(0, new ScriptVar(link.Name));
                    pair.SetArrayIndex(1, link.Var.DeepCopy());
                    var.ReturnVar.SetArrayIndex(idx++, pair);
                }
                link = link.Next;
            }
        }

        [ScriptMethod("assign", "target", "s0", "s1", "s2", "s3")]
        public static void ObjectAssignImpl(ScriptVar var, object userData)
        {
            var target = var.GetParameter("target");
            foreach (var name in new[] { "s0", "s1", "s2", "s3" })
            {
                var source = var.GetParameter(name);
                if (source.IsUndefined || source.IsNull) continue;
                var link = source.FirstChild;
                while (link != null)
                {
                    if (link.Name != ScriptVar.PrototypeClassName)
                        target.AddChildNoDup(link.Name, link.Var.DeepCopy());
                    link = link.Next;
                }
            }
            var.ReturnVar = target;
        }

        [ScriptMethod("fromEntries", "entries")]
        public static void ObjectFromEntriesImpl(ScriptVar var, object userData)
        {
            var entries = var.GetParameter("entries");
            var result = new ScriptVar();
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
            obj.AddChildNoDup("__frozen__", new ScriptVar(1));
            var.ReturnVar = obj;
        }

        [ScriptMethod("isFrozen", "obj")]
        public static void ObjectIsFrozenImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var frozen = obj.FindChild("__frozen__");
            var.ReturnVar.Int = (frozen != null && frozen.Var.Bool) ? 1 : 0;
        }

        [ScriptMethod("create", "proto")]
        public static void ObjectCreateImpl(ScriptVar var, object userData)
        {
            var proto = var.GetParameter("proto");
            var result = new ScriptVar();
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
            var link = obj.FirstChild;
            while (link != null)
            {
                if (link.Name != ScriptVar.PrototypeClassName)
                    var.ReturnVar.SetArrayIndex(idx++, new ScriptVar(link.Name));
                link = link.Next;
            }
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
            obj.AddChildNoDup("__sealed__", new ScriptVar(1));
            var.ReturnVar = obj;
        }

        [ScriptMethod("isSealed", "obj")]
        public static void ObjectIsSealedImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("obj");
            var sealed_ = obj.FindChild("__sealed__");
            var.ReturnVar.Int = (sealed_ != null && sealed_.Var.Bool) ? 1 : 0;
        }

        [ScriptMethod("groupBy", "arr", "keyFn")]
        public static void ObjectGroupByImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var arr = var.GetParameter("arr");
            var keyFn = var.GetParameter("keyFn");
            var len = arr.GetArrayLength();

            var result = new ScriptVar(ScriptVar.Flags.Object);
            for (var i = 0; i < len; i++)
            {
                var elem = arr.GetArrayIndex(i);
                var key = engine.CallFunction(keyFn, null, elem, new ScriptVar(i)).String;

                var group = result.FindChild(key);
                if (group == null)
                {
                    var newArr = new ScriptVar();
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
