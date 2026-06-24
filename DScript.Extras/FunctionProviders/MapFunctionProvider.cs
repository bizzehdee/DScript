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

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("Map")]
    public static class MapFunctionProvider
    {
        private static MapObject GetMap(ScriptVar thisVar)
            => thisVar.GetData() as MapObject ?? new MapObject();

        [ScriptMethod("get", "key")]
        public static void MapGetImpl(ScriptVar var, object userData)
        {
            var map = GetMap(var.GetParameter("this"));
            var key = var.GetParameter("key");
            foreach (var kvp in map.Data)
            {
                if (kvp.Key.Equal(key)) { var.ReturnVar = kvp.Value; return; }
            }
            var.ReturnVar.SetUndefined();
        }

        [ScriptMethod("set", "key", "val")]
        public static void MapSetImpl(ScriptVar var, object userData)
        {
            var thisVar = var.GetParameter("this");
            var map = GetMap(thisVar);
            var key = var.GetParameter("key");
            var val = var.GetParameter("val").DeepCopy();
            // replace if key already exists (reference equality for objects)
            foreach (var existing in map.Data.Keys)
            {
                if (existing.Equal(key)) { map.Data[existing] = val; var.ReturnVar = thisVar; return; }
            }
            map.Data[key] = val;
            var.ReturnVar = thisVar;
        }

        [ScriptMethod("has", "key")]
        public static void MapHasImpl(ScriptVar var, object userData)
        {
            var map = GetMap(var.GetParameter("this"));
            var key = var.GetParameter("key");
            foreach (var k in map.Data.Keys)
                if (k.Equal(key)) { var.ReturnVar.Int = 1; return; }
            var.ReturnVar.Int = 0;
        }

        [ScriptMethod("delete", "key")]
        public static void MapDeleteImpl(ScriptVar var, object userData)
        {
            var map = GetMap(var.GetParameter("this"));
            var key = var.GetParameter("key");
            foreach (var k in map.Data.Keys)
            {
                if (k.Equal(key)) { map.Data.Remove(k); var.ReturnVar.Int = 1; return; }
            }
            var.ReturnVar.Int = 0;
        }

        [ScriptMethod("clear")]
        public static void MapClearImpl(ScriptVar var, object userData)
        {
            GetMap(var.GetParameter("this")).Data.Clear();
        }

        [ScriptMethod("size")]
        public static void MapSizeImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = GetMap(var.GetParameter("this")).Data.Count;
        }

        [ScriptMethod("keys")]
        public static void MapKeysImpl(ScriptVar var, object userData)
        {
            var map = GetMap(var.GetParameter("this"));
            var result = ScriptVar.CreateUndefined();
            result.SetArray();
            var idx = 0;
            foreach (var k in map.Data.Keys)
                result.SetArrayIndex(idx++, k);
            var.ReturnVar = result;
        }

        [ScriptMethod("values")]
        public static void MapValuesImpl(ScriptVar var, object userData)
        {
            var map = GetMap(var.GetParameter("this"));
            var result = ScriptVar.CreateUndefined();
            result.SetArray();
            var idx = 0;
            foreach (var v in map.Data.Values)
                result.SetArrayIndex(idx++, v);
            var.ReturnVar = result;
        }

        [ScriptMethod("entries")]
        public static void MapEntriesImpl(ScriptVar var, object userData)
        {
            var map = GetMap(var.GetParameter("this"));
            var result = ScriptVar.CreateUndefined();
            result.SetArray();
            var idx = 0;
            foreach (var kvp in map.Data)
            {
                var pair = ScriptVar.CreateUndefined();
                pair.SetArray();
                pair.SetArrayIndex(0, kvp.Key);
                pair.SetArrayIndex(1, kvp.Value);
                result.SetArrayIndex(idx++, pair);
            }
            var.ReturnVar = result;
        }

        [ScriptMethod("forEach", "callback")]
        public static void MapForEachImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var map = GetMap(var.GetParameter("this"));
            var callback = var.GetParameter("callback");
            foreach (var kvp in map.Data)
                engine.CallFunction(callback, null, kvp.Value, kvp.Key);
        }

        [ScriptMethod("groupBy", "arr", "keyFn")]
        public static void MapGroupByImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var arr = var.GetParameter("arr");
            var keyFn = var.GetParameter("keyFn");
            var len = arr.GetArrayLength();

            var mapObj = new MapObject();
            for (var i = 0; i < len; i++)
            {
                var elem = arr.GetArrayIndex(i);
                var key = engine.CallFunction(keyFn, null, elem, ScriptVar.FromInt(i));

                // Find existing group for this key
                ScriptVar group = null;
                foreach (var k in mapObj.Data.Keys)
                {
                    if (k.Equal(key)) { group = mapObj.Data[k]; break; }
                }

                if (group == null)
                {
                    group = ScriptVar.CreateUndefined();
                    group.SetArray();
                    mapObj.Data[key.DeepCopy()] = group;
                }
                group.SetArrayIndex(group.GetArrayLength(), elem.DeepCopy());
            }

            var.ReturnVar = mapObj.ToScriptVar();
        }
    }
}
