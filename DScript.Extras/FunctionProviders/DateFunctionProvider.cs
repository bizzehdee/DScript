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
using System.Globalization;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("Date")]
    public static class DateFunctionProvider
    {
        // ── Static method ─────────────────────────────────────────────────────

        [ScriptMethod("now")]
        public static void DateNowImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        // ── Helper ────────────────────────────────────────────────────────────

        private static DateObject GetDateObject(ScriptVar thisVar)
        {
            var data = thisVar.GetData();
            return data as DateObject ?? new DateObject(DateTimeOffset.UtcNow);
        }

        // ── Instance get methods ──────────────────────────────────────────────

        [ScriptMethod("getTime")]
        public static void DateGetTimeImpl(ScriptVar var, object userData)
        {
            var d = GetDateObject(var.GetParameter("this"));
            if (d.IsInvalid) { var.ReturnVar.Float = double.NaN; return; }
            var.ReturnVar.Float = d.Value.ToUnixTimeMilliseconds();
        }

        [ScriptMethod("toJSON")]
        public static void DateToJSONImpl(ScriptVar var, object userData)
        {
            var d = GetDateObject(var.GetParameter("this"));
            if (d.IsInvalid) { var.ReturnVar = ScriptVar.CreateNull(); return; }
            var utc = d.Value.UtcDateTime;
            var.ReturnVar.String = utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        [ScriptMethod("getFullYear")]
        public static void DateGetFullYearImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = GetDateObject(var.GetParameter("this")).Value.LocalDateTime.Year;
        }

        [ScriptMethod("getMonth")]
        public static void DateGetMonthImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = GetDateObject(var.GetParameter("this")).Value.LocalDateTime.Month - 1;
        }

        [ScriptMethod("getDate")]
        public static void DateGetDateImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = GetDateObject(var.GetParameter("this")).Value.LocalDateTime.Day;
        }

        [ScriptMethod("getDay")]
        public static void DateGetDayImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = (int)GetDateObject(var.GetParameter("this")).Value.LocalDateTime.DayOfWeek;
        }

        [ScriptMethod("getHours")]
        public static void DateGetHoursImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = GetDateObject(var.GetParameter("this")).Value.LocalDateTime.Hour;
        }

        [ScriptMethod("getMinutes")]
        public static void DateGetMinutesImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = GetDateObject(var.GetParameter("this")).Value.LocalDateTime.Minute;
        }

        [ScriptMethod("getSeconds")]
        public static void DateGetSecondsImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = GetDateObject(var.GetParameter("this")).Value.LocalDateTime.Second;
        }

        [ScriptMethod("getMilliseconds")]
        public static void DateGetMillisecondsImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = GetDateObject(var.GetParameter("this")).Value.LocalDateTime.Millisecond;
        }

        [ScriptMethod("getTimezoneOffset")]
        public static void DateGetTimezoneOffsetImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = -(int)DateTimeOffset.Now.Offset.TotalMinutes;
        }

        // ── UTC get methods ───────────────────────────────────────────────────

        [ScriptMethod("getUTCFullYear")]
        public static void DateGetUTCFullYearImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = GetDateObject(var.GetParameter("this")).Value.UtcDateTime.Year;
        }

        [ScriptMethod("getUTCMonth")]
        public static void DateGetUTCMonthImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = GetDateObject(var.GetParameter("this")).Value.UtcDateTime.Month - 1;
        }

        [ScriptMethod("getUTCDate")]
        public static void DateGetUTCDateImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = GetDateObject(var.GetParameter("this")).Value.UtcDateTime.Day;
        }

        [ScriptMethod("getUTCDay")]
        public static void DateGetUTCDayImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = (int)GetDateObject(var.GetParameter("this")).Value.UtcDateTime.DayOfWeek;
        }

        [ScriptMethod("getUTCHours")]
        public static void DateGetUTCHoursImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = GetDateObject(var.GetParameter("this")).Value.UtcDateTime.Hour;
        }

        [ScriptMethod("getUTCMinutes")]
        public static void DateGetUTCMinutesImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = GetDateObject(var.GetParameter("this")).Value.UtcDateTime.Minute;
        }

        [ScriptMethod("getUTCSeconds")]
        public static void DateGetUTCSecondsImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = GetDateObject(var.GetParameter("this")).Value.UtcDateTime.Second;
        }

        [ScriptMethod("getUTCMilliseconds")]
        public static void DateGetUTCMillisecondsImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = GetDateObject(var.GetParameter("this")).Value.UtcDateTime.Millisecond;
        }

        // ── Instance set methods ──────────────────────────────────────────────

        [ScriptMethod("setTime", "ms")]
        public static void DateSetTimeImpl(ScriptVar var, object userData)
        {
            var d = GetDateObject(var.GetParameter("this"));
            d.Value = DateTimeOffset.FromUnixTimeMilliseconds((long)var.GetParameter("ms").Float);
            var.ReturnVar.Float = d.Value.ToUnixTimeMilliseconds();
        }

        [ScriptMethod("setFullYear", "y")]
        public static void DateSetFullYearImpl(ScriptVar var, object userData)
        {
            var d = GetDateObject(var.GetParameter("this"));
            var dt = d.Value.LocalDateTime;
            d.Value = new DateTimeOffset(new DateTime(var.GetParameter("y").Int, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Millisecond));
            var.ReturnVar.Float = d.Value.ToUnixTimeMilliseconds();
        }

        [ScriptMethod("setMonth", "m")]
        public static void DateSetMonthImpl(ScriptVar var, object userData)
        {
            var d = GetDateObject(var.GetParameter("this"));
            var dt = d.Value.LocalDateTime;
            d.Value = new DateTimeOffset(new DateTime(dt.Year, var.GetParameter("m").Int + 1, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Millisecond));
            var.ReturnVar.Float = d.Value.ToUnixTimeMilliseconds();
        }

        [ScriptMethod("setDate", "day")]
        public static void DateSetDateImpl(ScriptVar var, object userData)
        {
            var d = GetDateObject(var.GetParameter("this"));
            var dt = d.Value.LocalDateTime;
            d.Value = new DateTimeOffset(new DateTime(dt.Year, dt.Month, var.GetParameter("day").Int, dt.Hour, dt.Minute, dt.Second, dt.Millisecond));
            var.ReturnVar.Float = d.Value.ToUnixTimeMilliseconds();
        }

        [ScriptMethod("setHours", "h")]
        public static void DateSetHoursImpl(ScriptVar var, object userData)
        {
            var d = GetDateObject(var.GetParameter("this"));
            var dt = d.Value.LocalDateTime;
            d.Value = new DateTimeOffset(new DateTime(dt.Year, dt.Month, dt.Day, var.GetParameter("h").Int, dt.Minute, dt.Second, dt.Millisecond));
            var.ReturnVar.Float = d.Value.ToUnixTimeMilliseconds();
        }

        [ScriptMethod("setMinutes", "m")]
        public static void DateSetMinutesImpl(ScriptVar var, object userData)
        {
            var d = GetDateObject(var.GetParameter("this"));
            var dt = d.Value.LocalDateTime;
            d.Value = new DateTimeOffset(new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, var.GetParameter("m").Int, dt.Second, dt.Millisecond));
            var.ReturnVar.Float = d.Value.ToUnixTimeMilliseconds();
        }

        [ScriptMethod("setSeconds", "s")]
        public static void DateSetSecondsImpl(ScriptVar var, object userData)
        {
            var d = GetDateObject(var.GetParameter("this"));
            var dt = d.Value.LocalDateTime;
            d.Value = new DateTimeOffset(new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, var.GetParameter("s").Int, dt.Millisecond));
            var.ReturnVar.Float = d.Value.ToUnixTimeMilliseconds();
        }

        [ScriptMethod("setMilliseconds", "ms")]
        public static void DateSetMillisecondsImpl(ScriptVar var, object userData)
        {
            var d = GetDateObject(var.GetParameter("this"));
            var dt = d.Value.LocalDateTime;
            d.Value = new DateTimeOffset(new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, var.GetParameter("ms").Int));
            var.ReturnVar.Float = d.Value.ToUnixTimeMilliseconds();
        }

        // ── Formatting methods ────────────────────────────────────────────────

        [ScriptMethod("toISOString")]
        public static void DateToISOStringImpl(ScriptVar var, object userData)
        {
            var utc = GetDateObject(var.GetParameter("this")).Value.UtcDateTime;
            var.ReturnVar.String = utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        [ScriptMethod("toUTCString")]
        public static void DateToUTCStringImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.String = GetDateObject(var.GetParameter("this")).Value.UtcDateTime
                .ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT";
        }

        [ScriptMethod("toString")]
        public static void DateToStringImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.String = GetDateObject(var.GetParameter("this")).Value.LocalDateTime
                .ToString("ddd MMM dd yyyy HH:mm:ss 'GMT'zzz", CultureInfo.InvariantCulture);
        }

        [ScriptMethod("toDateString")]
        public static void DateToDateStringImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.String = GetDateObject(var.GetParameter("this")).Value.LocalDateTime
                .ToString("ddd MMM dd yyyy", CultureInfo.InvariantCulture);
        }

        [ScriptMethod("toTimeString")]
        public static void DateToTimeStringImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.String = GetDateObject(var.GetParameter("this")).Value.LocalDateTime
                .ToString("HH:mm:ss 'GMT'zzz", CultureInfo.InvariantCulture);
        }

        [ScriptMethod("toLocaleDateString")]
        public static void DateToLocaleDateStringImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.String = GetDateObject(var.GetParameter("this")).Value.LocalDateTime
                .ToString("d", CultureInfo.CurrentCulture);
        }

        [ScriptMethod("toLocaleTimeString")]
        public static void DateToLocaleTimeStringImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.String = GetDateObject(var.GetParameter("this")).Value.LocalDateTime
                .ToString("t", CultureInfo.CurrentCulture);
        }

        [ScriptMethod("toLocaleString")]
        public static void DateToLocaleStringImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.String = GetDateObject(var.GetParameter("this")).Value.LocalDateTime
                .ToString("G", CultureInfo.CurrentCulture);
        }

        [ScriptMethod("valueOf")]
        public static void DateValueOfImpl(ScriptVar var, object userData)
        {
            var d = GetDateObject(var.GetParameter("this"));
            if (d.IsInvalid) { var.ReturnVar.Float = double.NaN; return; }
            var.ReturnVar.Float = d.Value.ToUnixTimeMilliseconds();
        }
    }
}
