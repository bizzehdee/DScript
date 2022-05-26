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
    [ScriptClass("root")]
    public static class RootFunctionProvider
    {
        [ScriptMethod("eval", "str", AppearAtRoot = true)]
        public static void EvalImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var script = var.GetParameter("str").String;
            var returnVal = engine.EvalComplex(script);
            var.ReturnVar = returnVal.Var;
        }

        [ScriptMethod("exec", "str", AppearAtRoot = true)]
        public static void ExecImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var script = var.GetParameter("str").String;
            engine.Execute(script);
        }

        [ScriptMethod("trace", AppearAtRoot = true)]
        public static void TraceImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            engine.Root.Trace(0, null);
        }

        [ScriptMethod("charToInt", AppearAtRoot = true)]
        public static void CharToIntImpl(ScriptVar var, object userData)
        {
            var charStr = var.GetParameter("char").String;
            var charParam = charStr[0];
            var charAsInt = Convert.ToInt32(charParam);
            var.ReturnVar.Int = charAsInt;
        }
    }
}
