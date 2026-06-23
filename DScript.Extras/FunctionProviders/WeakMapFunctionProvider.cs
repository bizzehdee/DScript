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
    [ScriptClass("WeakMap")]
    public static class WeakMapFunctionProvider
    {
        private static WeakMapObject GetMap(ScriptVar thisVar)
            => thisVar.GetData() as WeakMapObject ?? new WeakMapObject();

        [ScriptMethod("get", "key")]
        public static void WeakMapGetImpl(ScriptVar var, object userData)
        {
            var map = GetMap(var.GetParameter("this"));
            var key = var.GetParameter("key");
            if (map.Data.TryGetValue(key, out var val))
                var.ReturnVar = val;
            else
                var.ReturnVar.SetUndefined();
        }

        [ScriptMethod("set", "key", "val")]
        public static void WeakMapSetImpl(ScriptVar var, object userData)
        {
            var thisVar = var.GetParameter("this");
            var map = GetMap(thisVar);
            var key = var.GetParameter("key");
            var val = var.GetParameter("val").DeepCopy();
            map.Data[key] = val;
            var.ReturnVar = thisVar;
        }

        [ScriptMethod("has", "key")]
        public static void WeakMapHasImpl(ScriptVar var, object userData)
        {
            var map = GetMap(var.GetParameter("this"));
            var key = var.GetParameter("key");
            var.ReturnVar.Int = map.Data.ContainsKey(key) ? 1 : 0;
        }

        [ScriptMethod("delete", "key")]
        public static void WeakMapDeleteImpl(ScriptVar var, object userData)
        {
            var map = GetMap(var.GetParameter("this"));
            var key = var.GetParameter("key");
            var.ReturnVar.Int = map.Data.Remove(key) ? 1 : 0;
        }
    }
}
