using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class ObjectExtrasTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        [Test]
        public void Keys_ReturnsOwnKeys()
        {
            var r = RunScript("var o = {a:1,b:2}; var __result__ = Object.keys(o).length;");
            Assert.That(r.Int, Is.EqualTo(2));
        }

        [Test]
        public void Values_ReturnsValues()
        {
            var r = RunScript("var o = {a:1,b:2}; var v = Object.values(o); var __result__ = v[0] + v[1];");
            Assert.That(r.Int, Is.EqualTo(3));
        }

        [Test]
        public void Values_EmptyObject()
        {
            var r = RunScript("var __result__ = Object.values({}).length;");
            Assert.That(r.Int, Is.EqualTo(0));
        }

        [Test]
        public void Entries_ReturnsPairs()
        {
            var r = RunScript("var e = Object.entries({x:10}); var __result__ = e[0][0];");
            Assert.That(r.String, Is.EqualTo("x"));
        }

        [Test]
        public void Entries_PairValue()
        {
            var r = RunScript("var e = Object.entries({x:10}); var __result__ = e[0][1];");
            Assert.That(r.Int, Is.EqualTo(10));
        }

        [Test]
        public void Assign_MergesProperties()
        {
            var r = RunScript("var t = {a:1}; Object.assign(t, {b:2}); var __result__ = t.b;");
            Assert.That(r.Int, Is.EqualTo(2));
        }

        [Test]
        public void Assign_OverwritesExisting()
        {
            var r = RunScript("var t = {a:1}; Object.assign(t, {a:99}); var __result__ = t.a;");
            Assert.That(r.Int, Is.EqualTo(99));
        }

        [Test]
        public void Assign_MultipleSources()
        {
            var r = RunScript("var t = {}; Object.assign(t, {a:1}, {b:2}); var __result__ = t.a + t.b;");
            Assert.That(r.Int, Is.EqualTo(3));
        }


[Test]
        public void FromEntries_RoundTripsWithEntries()
        {
            var r = RunScript("var o = {x:5,y:6}; var o2 = Object.fromEntries(Object.entries(o)); var __result__ = o2.x + o2.y;");
            Assert.That(r.Int, Is.EqualTo(11));
        }

        [Test]
        public void Freeze_MarksObjectFrozen()
        {
            var r = RunScript("var o = {a:1}; Object.freeze(o); var __result__ = Object.isFrozen(o);");
            Assert.That(r.Bool, Is.True);
        }

        [Test]
        public void IsFrozen_UnfrozenObjectReturnsFalse()
        {
            var r = RunScript("var o = {a:1}; var __result__ = Object.isFrozen(o);");
            Assert.That(r.Bool, Is.False);
        }

        [Test]
        public void Create_CreatesNewObject()
        {
            var r = RunScript("var proto = {greet: function() { return 'hi'; }}; var o = Object.create(proto); var __result__ = typeof o;");
            Assert.That(r.String, Is.EqualTo("object"));
        }

        [Test]
        public void GetOwnPropertyNames_IncludesAllChildren()
        {
            var r = RunScript("var o = {a:1,b:2,c:3}; var __result__ = Object.getOwnPropertyNames(o).length;");
            Assert.That(r.Int, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void HasOwnProperty_ExistingKey_ReturnsTrue()
        {
            var r = RunScript("var o = {a:1}; var __result__ = o.hasOwnProperty('a');");
            Assert.That(r.Bool, Is.True);
        }

        [Test]
        public void HasOwnProperty_MissingKey_ReturnsFalse()
        {
            var r = RunScript("var o = {a:1}; var __result__ = o.hasOwnProperty('z');");
            Assert.That(r.Bool, Is.False);
        }
    }
}
