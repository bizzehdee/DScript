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
    [ScriptClass("Set")]
    public static class SetFunctionProvider
    {
        private static SetObject GetSet(ScriptVar thisVar)
            => thisVar.GetData() as SetObject ?? new SetObject();

        /// <summary>
        /// Creates a new Set ScriptVar whose prototype link is inherited from
        /// <paramref name="thisVar"/> so that method dispatch works on the result.
        /// </summary>
        private static ScriptVar NewSetVarFrom(SetObject setObj, ScriptVar thisVar)
        {
            var sv = setObj.ToScriptVar();
            // Copy the prototype link from 'this' so the caller can invoke
            // methods on the returned set instance.
            var proto = thisVar.FindChild(ScriptVar.PrototypeClassName)?.Var;
            if (proto != null) sv.AddChild(ScriptVar.PrototypeClassName, proto);
            return sv;
        }

        [ScriptMethod("add", "val")]
        public static void SetAddImpl(ScriptVar var, object userData)
        {
            var thisVar = var.GetParameter("this");
            var set = GetSet(thisVar);
            var val = var.GetParameter("val");
            set.TryAdd(val);
            var.ReturnVar = thisVar;
        }

        [ScriptMethod("has", "val")]
        public static void SetHasImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = GetSet(var.GetParameter("this")).Contains(var.GetParameter("val")) ? 1 : 0;
        }

        [ScriptMethod("delete", "val")]
        public static void SetDeleteImpl(ScriptVar var, object userData)
        {
            var set = GetSet(var.GetParameter("this"));
            var val = var.GetParameter("val");
            foreach (var item in set.Data)
            {
                if (item.Equal(val)) { set.Data.Remove(item); var.ReturnVar.Int = 1; return; }
            }
            var.ReturnVar.Int = 0;
        }

        [ScriptMethod("clear")]
        public static void SetClearImpl(ScriptVar var, object userData)
        {
            GetSet(var.GetParameter("this")).Data.Clear();
        }

        // size is surfaced as a dynamic property by INativeContainer.GetSize() in
        // VirtualMachine — no static [ScriptProperty] registration is needed (a
        // static one would always return the count at registration time, i.e. 0).
        public static void SetSizeImpl(ScriptVar var, object userData)
        {
            var.Int = GetSet(var).Data.Count;
        }

        [ScriptMethod("keys")]
        [ScriptMethod("values")]
        public static void SetValuesImpl(ScriptVar var, object userData)
        {
            var set = GetSet(var.GetParameter("this"));
            var result = new ScriptVar();
            result.SetArray();
            var idx = 0;
            foreach (var item in set.Data)
                result.SetArrayIndex(idx++, item);
            var.ReturnVar = result;
        }

        [ScriptMethod("entries")]
        public static void SetEntriesImpl(ScriptVar var, object userData)
        {
            var set = GetSet(var.GetParameter("this"));
            var result = new ScriptVar();
            result.SetArray();
            var idx = 0;
            foreach (var item in set.Data)
            {
                var pair = new ScriptVar();
                pair.SetArray();
                pair.SetArrayIndex(0, item);
                pair.SetArrayIndex(1, item);
                result.SetArrayIndex(idx++, pair);
            }
            var.ReturnVar = result;
        }

        [ScriptMethod("forEach", "callback")]
        public static void SetForEachImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var set = GetSet(var.GetParameter("this"));
            var callback = var.GetParameter("callback");
            foreach (var item in set.Data)
                engine.CallFunction(callback, null, item, item);
        }

        [ScriptMethod("union", "other")]
        public static void SetUnionImpl(ScriptVar var, object userData)
        {
            var thisVar = var.GetParameter("this");
            var a = GetSet(thisVar);
            var b = GetSet(var.GetParameter("other"));
            var result = new SetObject();
            foreach (var item in a.Data) result.Data.Add(item);
            foreach (var item in b.Data)
                if (!result.Contains(item)) result.Data.Add(item);
            var.ReturnVar = NewSetVarFrom(result, thisVar);
        }

        [ScriptMethod("intersection", "other")]
        public static void SetIntersectionImpl(ScriptVar var, object userData)
        {
            var thisVar = var.GetParameter("this");
            var a = GetSet(thisVar);
            var b = GetSet(var.GetParameter("other"));
            var result = new SetObject();
            foreach (var item in a.Data)
                if (b.Contains(item)) result.Data.Add(item);
            var.ReturnVar = NewSetVarFrom(result, thisVar);
        }

        [ScriptMethod("difference", "other")]
        public static void SetDifferenceImpl(ScriptVar var, object userData)
        {
            var thisVar = var.GetParameter("this");
            var a = GetSet(thisVar);
            var b = GetSet(var.GetParameter("other"));
            var result = new SetObject();
            foreach (var item in a.Data)
                if (!b.Contains(item)) result.Data.Add(item);
            var.ReturnVar = NewSetVarFrom(result, thisVar);
        }

        [ScriptMethod("isSubsetOf", "other")]
        public static void SetIsSubsetOfImpl(ScriptVar var, object userData)
        {
            var a = GetSet(var.GetParameter("this"));
            var b = GetSet(var.GetParameter("other"));
            foreach (var item in a.Data)
                if (!b.Contains(item)) { var.ReturnVar.Int = 0; return; }
            var.ReturnVar.Int = 1;
        }
    }
}
