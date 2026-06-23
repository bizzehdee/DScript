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
    [ScriptClass("WeakSet")]
    public static class WeakSetFunctionProvider
    {
        private static WeakSetObject GetSet(ScriptVar thisVar)
            => thisVar.GetData() as WeakSetObject ?? new WeakSetObject();

        [ScriptMethod("add", "value")]
        public static void WeakSetAddImpl(ScriptVar var, object userData)
        {
            var thisVar = var.GetParameter("this");
            var set = GetSet(thisVar);
            var value = var.GetParameter("value");
            set.Data.Add(value);
            var.ReturnVar = thisVar;
        }

        [ScriptMethod("has", "value")]
        public static void WeakSetHasImpl(ScriptVar var, object userData)
        {
            var set = GetSet(var.GetParameter("this"));
            var value = var.GetParameter("value");
            var.ReturnVar.Int = set.Data.Contains(value) ? 1 : 0;
        }

        [ScriptMethod("delete", "value")]
        public static void WeakSetDeleteImpl(ScriptVar var, object userData)
        {
            var set = GetSet(var.GetParameter("this"));
            var value = var.GetParameter("value");
            var.ReturnVar.Int = set.Data.Remove(value) ? 1 : 0;
        }
    }
}
