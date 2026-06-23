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
    [ScriptClass("assert")]
    public static class AssertFunctionProvider
    {
        private static void Fail(string message)
            => throw new ScriptException(message.Length > 0 ? message : "AssertionError");

        private static bool DeepEqual(ScriptVar a, ScriptVar b)
        {
            if (a.IsNull && b.IsNull) return true;
            if (a.IsUndefined && b.IsUndefined) return true;
            if (a.IsNull || b.IsNull || a.IsUndefined || b.IsUndefined) return false;
            if (a.IsObject && b.IsObject)
            {
                var la = a.FirstChild;
                while (la != null)
                {
                    if (la.Name == ScriptVar.PrototypeClassName) { la = la.Next; continue; }
                    var lb = b.FindChild(la.Name);
                    if (lb == null || !DeepEqual(la.Var, lb.Var)) return false;
                    la = la.Next;
                }
                la = b.FirstChild;
                while (la != null)
                {
                    if (la.Name == ScriptVar.PrototypeClassName) { la = la.Next; continue; }
                    if (a.FindChild(la.Name) == null) return false;
                    la = la.Next;
                }
                return true;
            }
            if (a.IsArray && b.IsArray)
            {
                var lenA = a.GetArrayLength();
                var lenB = b.GetArrayLength();
                if (lenA != lenB) return false;
                for (var i = 0; i < lenA; i++)
                    if (!DeepEqual(a.GetArrayIndex(i), b.GetArrayIndex(i))) return false;
                return true;
            }
            return a.Equal(b);
        }

        [ScriptMethod("ok", "val", "msg")]
        public static void AssertOkImpl(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val");
            var msgVar = var.GetParameter("msg");
            if (!val.Bool)
                Fail(msgVar.IsUndefined ? "Expected truthy value" : msgVar.String);
        }

        [ScriptMethod("equal", "a", "b", "msg")]
        public static void AssertEqualImpl(ScriptVar var, object userData)
        {
            var a = var.GetParameter("a");
            var b = var.GetParameter("b");
            var msgVar = var.GetParameter("msg");
            if (!(a.Equal(b)))
                Fail(msgVar.IsUndefined ? $"Expected {a.String} == {b.String}" : msgVar.String);
        }

        [ScriptMethod("strictEqual", "a", "b", "msg")]
        public static void AssertStrictEqualImpl(ScriptVar var, object userData)
        {
            var a = var.GetParameter("a");
            var b = var.GetParameter("b");
            var msgVar = var.GetParameter("msg");
            if (!a.Equal(b))
                Fail(msgVar.IsUndefined ? $"Expected {a.String} === {b.String}" : msgVar.String);
        }

        [ScriptMethod("notEqual", "a", "b", "msg")]
        public static void AssertNotEqualImpl(ScriptVar var, object userData)
        {
            var a = var.GetParameter("a");
            var b = var.GetParameter("b");
            var msgVar = var.GetParameter("msg");
            if (a.Equal(b))
                Fail(msgVar.IsUndefined ? $"Expected {a.String} != {b.String}" : msgVar.String);
        }

        [ScriptMethod("notStrictEqual", "a", "b", "msg")]
        public static void AssertNotStrictEqualImpl(ScriptVar var, object userData)
            => AssertNotEqualImpl(var, userData);

        [ScriptMethod("deepEqual", "a", "b", "msg")]
        public static void AssertDeepEqualImpl(ScriptVar var, object userData)
        {
            var a = var.GetParameter("a");
            var b = var.GetParameter("b");
            var msgVar = var.GetParameter("msg");
            if (!DeepEqual(a, b))
                Fail(msgVar.IsUndefined ? "Deep equality check failed" : msgVar.String);
        }

        [ScriptMethod("throws", "fn", "msg")]
        public static void AssertThrowsImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var fn = var.GetParameter("fn");
            var msgVar = var.GetParameter("msg");
            var threw = false;
            try { engine.CallFunction(fn, null); }
            catch (System.Exception ex) when (ex is ScriptException || ex is JITException) { threw = true; }
            if (!threw)
                Fail(msgVar.IsUndefined ? "Expected function to throw" : msgVar.String);
        }

        [ScriptMethod("doesNotThrow", "fn", "msg")]
        public static void AssertDoesNotThrowImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var fn = var.GetParameter("fn");
            var msgVar = var.GetParameter("msg");
            try { engine.CallFunction(fn, null); }
            catch (System.Exception ex) when (ex is ScriptException || ex is JITException)
            {
                Fail(msgVar.IsUndefined ? $"Expected function not to throw, but got: {ex.Message}" : msgVar.String);
            }
        }

        [ScriptMethod("fail", "msg")]
        public static void AssertFailImpl(ScriptVar var, object userData)
        {
            var msgVar = var.GetParameter("msg");
            Fail(msgVar.IsUndefined ? "Assertion failed" : msgVar.String);
        }
    }
}
