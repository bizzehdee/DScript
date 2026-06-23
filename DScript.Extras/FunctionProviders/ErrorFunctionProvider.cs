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
    [ScriptClass("Errors")]
    public static class ErrorFunctionProvider
    {
        private static ScriptVar MakeError(string name, string message)
        {
            var obj = new ScriptVar(ScriptVar.Flags.Object);
            obj.AddChild("name", new ScriptVar(name));
            obj.AddChild("message", new ScriptVar(message));
            obj.AddChild("stack", new ScriptVar(""));
            return obj;
        }

        [ScriptMethod("Error", "msg", "options", AppearAtRoot = true)]
        public static void ErrorImpl(ScriptVar var, object userData)
        {
            var msgVar = var.GetParameter("msg");
            var obj = MakeError("Error", msgVar.IsUndefined ? "" : msgVar.String);
            var options = var.GetParameter("options");
            if (!options.IsUndefined && !options.IsNull)
            {
                var cause = options.FindChild("cause");
                if (cause != null)
                    obj.AddChild("cause", cause.Var.DeepCopy());
            }
            var.ReturnVar = obj;
        }

        [ScriptMethod("TypeError", "msg", AppearAtRoot = true)]
        public static void TypeErrorImpl(ScriptVar var, object userData)
        {
            var msgVar = var.GetParameter("msg");
            var.ReturnVar = MakeError("TypeError", msgVar.IsUndefined ? "" : msgVar.String);
        }

        [ScriptMethod("RangeError", "msg", AppearAtRoot = true)]
        public static void RangeErrorImpl(ScriptVar var, object userData)
        {
            var msgVar = var.GetParameter("msg");
            var.ReturnVar = MakeError("RangeError", msgVar.IsUndefined ? "" : msgVar.String);
        }

        [ScriptMethod("ReferenceError", "msg", AppearAtRoot = true)]
        public static void ReferenceErrorImpl(ScriptVar var, object userData)
        {
            var msgVar = var.GetParameter("msg");
            var.ReturnVar = MakeError("ReferenceError", msgVar.IsUndefined ? "" : msgVar.String);
        }

        [ScriptMethod("SyntaxError", "msg", AppearAtRoot = true)]
        public static void SyntaxErrorImpl(ScriptVar var, object userData)
        {
            var msgVar = var.GetParameter("msg");
            var.ReturnVar = MakeError("SyntaxError", msgVar.IsUndefined ? "" : msgVar.String);
        }

        [ScriptMethod("URIError", "msg", AppearAtRoot = true)]
        public static void URIErrorImpl(ScriptVar var, object userData)
        {
            var msgVar = var.GetParameter("msg");
            var.ReturnVar = MakeError("URIError", msgVar.IsUndefined ? "" : msgVar.String);
        }

        [ScriptMethod("EvalError", "msg", AppearAtRoot = true)]
        public static void EvalErrorImpl(ScriptVar var, object userData)
        {
            var msgVar = var.GetParameter("msg");
            var.ReturnVar = MakeError("EvalError", msgVar.IsUndefined ? "" : msgVar.String);
        }

        [ScriptMethod("AggregateError", "errors", "msg", AppearAtRoot = true)]
        public static void AggregateErrorImpl(ScriptVar var, object userData)
        {
            var msgVar = var.GetParameter("msg");
            var errors = var.GetParameter("errors");
            var obj = MakeError("AggregateError", msgVar.IsUndefined ? "" : msgVar.String);
            obj.AddChild("errors", errors.IsUndefined ? new ScriptVar() : errors.DeepCopy());
            var.ReturnVar = obj;
        }
    }
}
