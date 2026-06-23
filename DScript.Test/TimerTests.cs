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

using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class TimerTests
    {
        private static ScriptEngine MakeEngine()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            return engine;
        }

        // --- setTimeout ---

        [Test]
        public void SetTimeout_ZeroDelay_FiresAfterDrain()
        {
            var engine = MakeEngine();
            engine.Execute("var called = 0; setTimeout(function() { called = called + 1; }, 0);");
            Assert.That(engine.Root.GetParameter("called").Int, Is.EqualTo(0));
            // Drain with a future time so all timers are due
            TimerQueue.GetOrCreate(engine).Drain(engine, long.MaxValue);
            Assert.That(engine.Root.GetParameter("called").Int, Is.EqualTo(1));
        }

        [Test]
        public void SetTimeout_ReturnsNumericId()
        {
            var engine = MakeEngine();
            engine.Execute("var result = setTimeout(function(){}, 0);");
            Assert.That(engine.Root.GetParameter("result").Int, Is.GreaterThan(0));
        }

        [Test]
        public void SetTimeout_NoDelay_DefaultsToZero()
        {
            var engine = MakeEngine();
            engine.Execute("var called = 0; setTimeout(function() { called = 1; });");
            TimerQueue.GetOrCreate(engine).Drain(engine, long.MaxValue);
            Assert.That(engine.Root.GetParameter("called").Int, Is.EqualTo(1));
        }

        [Test]
        public void SetTimeout_MultipleTimers_AllFire()
        {
            var engine = MakeEngine();
            engine.Execute(
                "var count = 0; " +
                "setTimeout(function() { count = count + 1; }, 0); " +
                "setTimeout(function() { count = count + 1; }, 0);");
            TimerQueue.GetOrCreate(engine).Drain(engine, long.MaxValue);
            Assert.That(engine.Root.GetParameter("count").Int, Is.EqualTo(2));
        }

        // --- clearTimeout ---

        [Test]
        public void ClearTimeout_CancelsPendingTimer()
        {
            var engine = MakeEngine();
            engine.Execute(
                "var called = 0; " +
                "var id = setTimeout(function() { called = 1; }, 0); " +
                "clearTimeout(id);");
            TimerQueue.GetOrCreate(engine).Drain(engine, long.MaxValue);
            Assert.That(engine.Root.GetParameter("called").Int, Is.EqualTo(0));
        }

        [Test]
        public void ClearTimeout_UnknownId_DoesNotThrow()
        {
            var engine = MakeEngine();
            Assert.DoesNotThrow(() => engine.Execute("clearTimeout(9999);"));
        }

        // --- setInterval ---

        [Test]
        public void SetInterval_ReturnsNumericId()
        {
            var engine = MakeEngine();
            engine.Execute("var result = setInterval(function(){}, 100);");
            Assert.That(engine.Root.GetParameter("result").Int, Is.GreaterThan(0));
        }

        [Test]
        public void SetInterval_FiresOnDrain()
        {
            var engine = MakeEngine();
            engine.Execute("var count = 0; setInterval(function() { count = count + 1; }, 0);");
            var q = TimerQueue.GetOrCreate(engine);
            q.Drain(engine, long.MaxValue);
            Assert.That(engine.Root.GetParameter("count").Int, Is.GreaterThanOrEqualTo(1));
        }

        // --- clearInterval ---

        [Test]
        public void ClearInterval_StopsRepeating()
        {
            var engine = MakeEngine();
            engine.Execute(
                "var count = 0; " +
                "var id = setInterval(function() { count = count + 1; }, 0); " +
                "clearInterval(id);");
            var q = TimerQueue.GetOrCreate(engine);
            q.Drain(engine, long.MaxValue);
            Assert.That(engine.Root.GetParameter("count").Int, Is.EqualTo(0));
        }

        // --- TimerQueue.PendingCount ---

        [Test]
        public void TimerQueue_PendingCount_ReflectsRegisteredTimers()
        {
            var engine = MakeEngine();
            engine.Execute("setTimeout(function(){}, 10000);");
            var q = TimerQueue.GetOrCreate(engine);
            Assert.That(q.PendingCount, Is.EqualTo(1));
        }

        [Test]
        public void TimerQueue_Drain_RemovesFiredTimers()
        {
            var engine = MakeEngine();
            engine.Execute("setTimeout(function(){}, 0);");
            var q = TimerQueue.GetOrCreate(engine);
            q.Drain(engine, long.MaxValue);
            Assert.That(q.PendingCount, Is.EqualTo(0));
        }
    }
}
