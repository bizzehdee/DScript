using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class HostEventTests
    {
        private static ScriptEngine MakeEngine(string script = "")
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            if (!string.IsNullOrEmpty(script))
                engine.Run(ScriptEngine.Compile(script));
            return engine;
        }

        // ── basic dispatch ────────────────────────────────────────────────────

        [Test]
        public void RaiseEvent_NoHandlers_IsNoOp()
        {
            var engine = MakeEngine();
            Assert.DoesNotThrow(() => engine.RaiseEvent("unhandled"));
        }

        [Test]
        public void RaiseEvent_HandlerCalledWithArgs()
        {
            var engine = MakeEngine("var result = 0; on('tick', function(v) { result = v; });");
            engine.RaiseEvent("tick", new ScriptVar(42));
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(42));
        }

        [Test]
        public void RaiseEvent_MultipleHandlers_AllCalled()
        {
            var engine = MakeEngine(
                "var a = 0, b = 0;" +
                "on('ping', function() { a = 1; });" +
                "on('ping', function() { b = 2; });");
            engine.RaiseEvent("ping");
            Assert.That(engine.Root.GetParameter("a").Int, Is.EqualTo(1));
            Assert.That(engine.Root.GetParameter("b").Int, Is.EqualTo(2));
        }

        [Test]
        public void RaiseEvent_WrongEvent_DoesNotFire()
        {
            var engine = MakeEngine("var result = 0; on('click', function() { result = 99; });");
            engine.RaiseEvent("hover");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(0));
        }

        [Test]
        public void RaiseEvent_MultipleArgs_AllForwarded()
        {
            var engine = MakeEngine("var x = 0, y = 0; on('move', function(a, b) { x = a; y = b; });");
            engine.RaiseEvent("move", new ScriptVar(10), new ScriptVar(20));
            Assert.That(engine.Root.GetParameter("x").Int, Is.EqualTo(10));
            Assert.That(engine.Root.GetParameter("y").Int, Is.EqualTo(20));
        }

        // ── once ─────────────────────────────────────────────────────────────

        [Test]
        public void Once_FiredOnce_ThenRemovedAutomatically()
        {
            var engine = MakeEngine("var count = 0; once('tick', function() { count++; });");
            engine.RaiseEvent("tick");
            engine.RaiseEvent("tick");
            Assert.That(engine.Root.GetParameter("count").Int, Is.EqualTo(1));
        }

        // ── off ───────────────────────────────────────────────────────────────

        [Test]
        public void Off_RemovesHandler_SubsequentRaiseDoesNotFire()
        {
            var engine = MakeEngine(
                "var count = 0;" +
                "function handler() { count++; }" +
                "on('evt', handler);" +
                "off('evt', handler);");
            engine.RaiseEvent("evt");
            Assert.That(engine.Root.GetParameter("count").Int, Is.EqualTo(0));
        }

        // ── removeAllListeners ────────────────────────────────────────────────

        [Test]
        public void RemoveAllListeners_ClearsSpecificEvent()
        {
            var engine = MakeEngine(
                "var a = 0, b = 0;" +
                "on('a', function() { a = 1; });" +
                "on('b', function() { b = 1; });" +
                "removeAllListeners('a');");
            engine.RaiseEvent("a");
            engine.RaiseEvent("b");
            Assert.That(engine.Root.GetParameter("a").Int, Is.EqualTo(0));
            Assert.That(engine.Root.GetParameter("b").Int, Is.EqualTo(1));
        }

        [Test]
        public void RemoveAllListeners_NoArg_ClearsAll()
        {
            var engine = MakeEngine(
                "var a = 0, b = 0;" +
                "on('a', function() { a = 1; });" +
                "on('b', function() { b = 1; });" +
                "removeAllListeners();");
            engine.RaiseEvent("a");
            engine.RaiseEvent("b");
            Assert.That(engine.Root.GetParameter("a").Int, Is.EqualTo(0));
            Assert.That(engine.Root.GetParameter("b").Int, Is.EqualTo(0));
        }

        // ── no Extras → no-op ─────────────────────────────────────────────────

        [Test]
        public void RaiseEvent_WithoutExtras_IsNoOp()
        {
            var engine = new ScriptEngine(); // no RegisterFunctions
            Assert.DoesNotThrow(() => engine.RaiseEvent("click"));
        }

        // ── handlers registered AFTER an earlier raise ────────────────────────

        [Test]
        public void RaiseEvent_BeforeHandlerRegistered_DoesNotFire()
        {
            var engine = MakeEngine("var result = 0;");
            engine.RaiseEvent("early");
            engine.Run(ScriptEngine.Compile("on('early', function() { result = 7; });"));
            // handler registered after the raise — should NOT retroactively fire
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(0));
        }

        [Test]
        public void RaiseEvent_AfterHandlerRegistered_Fires()
        {
            var engine = MakeEngine("var result = 0; on('late', function(v) { result = v; });");
            engine.RaiseEvent("late", new ScriptVar(5));
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(5));
        }
    }
}
