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
    [ScriptClass("Integer")]
    public static class IntegerFunctionProvider
    {
        [ScriptMethod("parseInt", "str")]
        [ScriptMethod("parseInt", "str", AppearAtRoot = true)]
        public static void IntParseIntImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("str").String;

            if (int.TryParse(str, out int intResult) == false)
            {
                intResult = 0;
            }

            var.ReturnVar.Int = intResult;
        }

        [ScriptMethod("parseFloat", "str")]
        [ScriptMethod("parseFloat", "str", AppearAtRoot = true)]
        public static void IntParseFloatImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("str").String;

            if (double.TryParse(str, out double intResult) == false)
            {
                intResult = 0;
            }

            var.ReturnVar.Float = intResult;
        }

        [ScriptMethod("valueOf", "str")]
        public static void IntValueOfImpl(ScriptVar var, object userData)
        {
            var str = var.GetParameter("str").String;

            var intResult = Convert.ToInt32(str[0]);

            var.ReturnVar.Int = intResult;
        }
    }
}
