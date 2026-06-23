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
    [ScriptClass("__timers__")]
    public static class TimerFunctionProvider
    {
        [ScriptMethod("setTimeout", "fn", "delay", AppearAtRoot = true)]
        public static void SetTimeoutImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var fn = var.GetParameter("fn");
            var delayVar = var.GetParameter("delay");
            var delay = delayVar.IsUndefined ? 0 : delayVar.Int;
            var q = TimerQueue.GetOrCreate(engine);
            var id = q.SetTimeout(fn, delay, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var.ReturnVar.Int = id;
        }

        [ScriptMethod("clearTimeout", "id", AppearAtRoot = true)]
        public static void ClearTimeoutImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var idVar = var.GetParameter("id");
            if (!idVar.IsUndefined)
                TimerQueue.GetOrCreate(engine).Clear(idVar.Int);
        }

        [ScriptMethod("setInterval", "fn", "interval", AppearAtRoot = true)]
        public static void SetIntervalImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var fn = var.GetParameter("fn");
            var intervalVar = var.GetParameter("interval");
            var interval = intervalVar.IsUndefined ? 0 : intervalVar.Int;
            var q = TimerQueue.GetOrCreate(engine);
            var id = q.SetInterval(fn, interval, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var.ReturnVar.Int = id;
        }

        [ScriptMethod("clearInterval", "id", AppearAtRoot = true)]
        public static void ClearIntervalImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var idVar = var.GetParameter("id");
            if (!idVar.IsUndefined)
                TimerQueue.GetOrCreate(engine).Clear(idVar.Int);
        }
    }
}
