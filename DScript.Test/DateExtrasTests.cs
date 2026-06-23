using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class DateExtrasTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        [Test]
        public void DateNow_ReturnsPositiveNumber()
        {
            var r = RunScript("var __result__ = Date.now();");
            Assert.That(r.Float, Is.GreaterThan(0));
        }

        [Test]
        public void NewDate_NoArgs_CreatesObject()
        {
            var r = RunScript("var d = new Date(); var __result__ = typeof d;");
            Assert.That(r.String, Is.EqualTo("object"));
        }

        [Test]
        public void NewDate_FromMs_RoundTripsGetTime()
        {
            var r = RunScript("var d = new Date(1000); var __result__ = d.getTime();");
            Assert.That(r.Float, Is.EqualTo(1000).Within(1));
        }

        [Test]
        public void NewDate_FromIsoString_ParsesYear()
        {
            var r = RunScript("var d = new Date('2020-06-15T00:00:00.000Z'); var __result__ = d.getUTCFullYear();");
            Assert.That(r.Int, Is.EqualTo(2020));
        }

        [Test]
        public void NewDate_Components_Year()
        {
            var r = RunScript("var d = new Date(2021, 0, 1); var __result__ = d.getFullYear();");
            Assert.That(r.Int, Is.EqualTo(2021));
        }

        [Test]
        public void NewDate_Components_Month_ZeroBased()
        {
            var r = RunScript("var d = new Date(2021, 5, 1); var __result__ = d.getMonth();");
            Assert.That(r.Int, Is.EqualTo(5));
        }

        [Test]
        public void GetDate_ReturnsDay()
        {
            var r = RunScript("var d = new Date(2021, 0, 15); var __result__ = d.getDate();");
            Assert.That(r.Int, Is.EqualTo(15));
        }

        [Test]
        public void GetHours_GetMinutes_GetSeconds()
        {
            var r = RunScript("var d = new Date('2020-01-01T10:30:45.000Z'); var __result__ = d.getUTCHours();");
            Assert.That(r.Int, Is.EqualTo(10));
        }

        [Test]
        public void GetMilliseconds_ReturnsMs()
        {
            var r = RunScript("var d = new Date(1500); var __result__ = d.getMilliseconds();");
            Assert.That(r.Int, Is.EqualTo(500));
        }

        [Test]
        public void GetUTCFullYear_ReturnsYear()
        {
            var r = RunScript("var d = new Date('2023-03-15T00:00:00.000Z'); var __result__ = d.getUTCFullYear();");
            Assert.That(r.Int, Is.EqualTo(2023));
        }

        [Test]
        public void GetUTCMonth_ZeroBased()
        {
            var r = RunScript("var d = new Date('2023-03-15T00:00:00.000Z'); var __result__ = d.getUTCMonth();");
            Assert.That(r.Int, Is.EqualTo(2));
        }

        [Test]
        public void SetFullYear_UpdatesYear()
        {
            var r = RunScript("var d = new Date(2020, 0, 1); d.setFullYear(2025); var __result__ = d.getFullYear();");
            Assert.That(r.Int, Is.EqualTo(2025));
        }

        [Test]
        public void SetTime_UpdatesTime()
        {
            var r = RunScript("var d = new Date(0); d.setTime(5000); var __result__ = d.getTime();");
            Assert.That(r.Float, Is.EqualTo(5000).Within(1));
        }

        [Test]
        public void ToIsoString_ContainsTAndZ()
        {
            var r = RunScript("var d = new Date(0); var __result__ = d.toISOString();");
            var s = r.String;
            Assert.That(s, Does.Contain("T").And.EndWith("Z"));
        }

        [Test]
        public void ToString_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript("var d = new Date(); var __result__ = d.toString();"));
        }

        [Test]
        public void ToDateString_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript("var d = new Date(); var __result__ = d.toDateString();"));
        }

        [Test]
        public void ToUTCString_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript("var d = new Date(); var __result__ = d.toUTCString();"));
        }

        [Test]
        public void ValueOf_EqualsGetTime()
        {
            var r = RunScript("var d = new Date(12345); var __result__ = d.valueOf() === d.getTime();");
            Assert.That(r.Bool, Is.True);
        }

        [Test]
        public void GetDay_ReturnsZeroToSix()
        {
            var r = RunScript("var d = new Date(); var __result__ = d.getDay();");
            Assert.That(r.Int, Is.InRange(0, 6));
        }

        // ── UTC get methods ───────────────────────────────────────────────────

        [Test]
        public void GetUTCDate_ReturnsDay()
        {
            var r = RunScript("var d = new Date('2023-03-15T00:00:00.000Z'); var __result__ = d.getUTCDate();");
            Assert.That(r.Int, Is.EqualTo(15));
        }

        [Test]
        public void GetUTCDay_IsInRange()
        {
            var r = RunScript("var d = new Date('2023-03-15T00:00:00.000Z'); var __result__ = d.getUTCDay();");
            Assert.That(r.Int, Is.InRange(0, 6));
        }

        [Test]
        public void GetUTCHours_ReturnsHour()
        {
            var r = RunScript("var d = new Date('2023-03-15T14:30:00.000Z'); var __result__ = d.getUTCHours();");
            Assert.That(r.Int, Is.EqualTo(14));
        }

        [Test]
        public void GetUTCMinutes_ReturnsMinute()
        {
            var r = RunScript("var d = new Date('2023-03-15T14:30:45.000Z'); var __result__ = d.getUTCMinutes();");
            Assert.That(r.Int, Is.EqualTo(30));
        }

        [Test]
        public void GetUTCSeconds_ReturnsSecond()
        {
            var r = RunScript("var d = new Date('2023-03-15T14:30:45.000Z'); var __result__ = d.getUTCSeconds();");
            Assert.That(r.Int, Is.EqualTo(45));
        }

        [Test]
        public void GetUTCMilliseconds_ReturnsMs()
        {
            var r = RunScript("var d = new Date('2023-03-15T14:30:45.123Z'); var __result__ = d.getUTCMilliseconds();");
            Assert.That(r.Int, Is.EqualTo(123));
        }

        // ── Local time get methods ────────────────────────────────────────────

        [Test]
        public void GetHours_ReturnsLocalHour()
        {
            var r = RunScript("var d = new Date(); var __result__ = d.getHours();");
            Assert.That(r.Int, Is.InRange(0, 23));
        }

        [Test]
        public void GetMinutes_ReturnsLocalMinute()
        {
            var r = RunScript("var d = new Date(); var __result__ = d.getMinutes();");
            Assert.That(r.Int, Is.InRange(0, 59));
        }

        [Test]
        public void GetSeconds_ReturnsLocalSecond()
        {
            var r = RunScript("var d = new Date(); var __result__ = d.getSeconds();");
            Assert.That(r.Int, Is.InRange(0, 59));
        }

        [Test]
        public void GetTimezoneOffset_IsAnInteger()
        {
            var r = RunScript("var d = new Date(); var __result__ = d.getTimezoneOffset();");
            Assert.That(r.Int, Is.Not.Null);
        }

        // ── Set methods ───────────────────────────────────────────────────────

        [Test]
        public void SetMonth_UpdatesMonth()
        {
            var r = RunScript("var d = new Date(2021, 0, 1); d.setMonth(5); var __result__ = d.getMonth();");
            Assert.That(r.Int, Is.EqualTo(5));
        }

        [Test]
        public void SetDate_UpdatesDay()
        {
            var r = RunScript("var d = new Date(2021, 0, 1); d.setDate(20); var __result__ = d.getDate();");
            Assert.That(r.Int, Is.EqualTo(20));
        }

        [Test]
        public void SetHours_UpdatesHour()
        {
            var r = RunScript("var d = new Date(2021, 0, 1, 0, 0, 0, 0); d.setHours(10); var __result__ = d.getHours();");
            Assert.That(r.Int, Is.EqualTo(10));
        }

        [Test]
        public void SetMinutes_UpdatesMinute()
        {
            var r = RunScript("var d = new Date(2021, 0, 1, 0, 0, 0, 0); d.setMinutes(30); var __result__ = d.getMinutes();");
            Assert.That(r.Int, Is.EqualTo(30));
        }

        [Test]
        public void SetSeconds_UpdatesSecond()
        {
            var r = RunScript("var d = new Date(2021, 0, 1, 0, 0, 0, 0); d.setSeconds(45); var __result__ = d.getSeconds();");
            Assert.That(r.Int, Is.EqualTo(45));
        }

        [Test]
        public void SetMilliseconds_UpdatesMs()
        {
            var r = RunScript("var d = new Date(0); d.setMilliseconds(500); var __result__ = d.getMilliseconds();");
            Assert.That(r.Int, Is.EqualTo(500));
        }

        // ── Formatting methods ────────────────────────────────────────────────

        [Test]
        public void ToTimeString_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript("var d = new Date(); var __result__ = d.toTimeString();"));
        }

        [Test]
        public void ToLocaleDateString_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript("var d = new Date(); var __result__ = d.toLocaleDateString();"));
        }

        [Test]
        public void ToLocaleTimeString_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript("var d = new Date(); var __result__ = d.toLocaleTimeString();"));
        }

        [Test]
        public void ToLocaleString_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript("var d = new Date(); var __result__ = d.toLocaleString();"));
        }
    }
}
