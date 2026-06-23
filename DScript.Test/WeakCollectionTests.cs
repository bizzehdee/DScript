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

using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class WeakCollectionTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        // -------------------------------------------------------------------
        // WeakMap
        // -------------------------------------------------------------------

        [Test]
        public void WeakMap_SetAndGet()
        {
            var r = RunScript(@"
var wm = new WeakMap();
var key = {};
wm.set(key, 42);
__result__ = wm.get(key);
").Int;
            Assert.That(r, Is.EqualTo(42));
        }

        [Test]
        public void WeakMap_HasReturnsTrueAfterSet()
        {
            var r = RunScript(@"
var wm = new WeakMap();
var key = {};
wm.set(key, 1);
__result__ = wm.has(key) ? 1 : 0;
").Int;
            Assert.That(r, Is.EqualTo(1));
        }

        [Test]
        public void WeakMap_HasReturnsFalseForAbsentKey()
        {
            var r = RunScript(@"
var wm = new WeakMap();
var key = {};
__result__ = wm.has(key) ? 1 : 0;
").Int;
            Assert.That(r, Is.EqualTo(0));
        }

        [Test]
        public void WeakMap_Delete_RemovesKey()
        {
            var r = RunScript(@"
var wm = new WeakMap();
var key = {};
wm.set(key, 99);
wm.delete(key);
__result__ = wm.has(key) ? 1 : 0;
").Int;
            Assert.That(r, Is.EqualTo(0));
        }

        [Test]
        public void WeakMap_GetMissingKeyReturnsUndefined()
        {
            var r = RunScript(@"
var wm = new WeakMap();
var key = {};
__result__ = wm.get(key);
");
            Assert.That(r.IsUndefined, Is.True);
        }

        [Test]
        public void WeakMap_DifferentObjectKeysAreDistinct()
        {
            var r = RunScript(@"
var wm = new WeakMap();
var k1 = {};
var k2 = {};
wm.set(k1, 1);
wm.set(k2, 2);
__result__ = wm.get(k1) + wm.get(k2);
").Int;
            Assert.That(r, Is.EqualTo(3));
        }

        // -------------------------------------------------------------------
        // WeakSet
        // -------------------------------------------------------------------

        [Test]
        public void WeakSet_AddAndHas()
        {
            var r = RunScript(@"
var ws = new WeakSet();
var obj = {};
ws.add(obj);
__result__ = ws.has(obj) ? 1 : 0;
").Int;
            Assert.That(r, Is.EqualTo(1));
        }

        [Test]
        public void WeakSet_HasReturnsFalseForAbsent()
        {
            var r = RunScript(@"
var ws = new WeakSet();
var obj = {};
__result__ = ws.has(obj) ? 1 : 0;
").Int;
            Assert.That(r, Is.EqualTo(0));
        }

        [Test]
        public void WeakSet_Delete_RemovesObject()
        {
            var r = RunScript(@"
var ws = new WeakSet();
var obj = {};
ws.add(obj);
ws.delete(obj);
__result__ = ws.has(obj) ? 1 : 0;
").Int;
            Assert.That(r, Is.EqualTo(0));
        }

        [Test]
        public void WeakSet_DifferentObjectsDistinct()
        {
            var r = RunScript(@"
var ws = new WeakSet();
var o1 = {};
var o2 = {};
ws.add(o1);
__result__ = (ws.has(o1) && !ws.has(o2)) ? 1 : 0;
").Int;
            Assert.That(r, Is.EqualTo(1));
        }

        // -------------------------------------------------------------------
        // WeakRef
        // -------------------------------------------------------------------

        [Test]
        public void WeakRef_DerefReturnsTarget()
        {
            var r = RunScript(@"
var target = { x: 7 };
var ref = new WeakRef(target);
__result__ = ref.deref().x;
").Int;
            Assert.That(r, Is.EqualTo(7));
        }

        [Test]
        public void WeakRef_DerefReturnsLiveObject()
        {
            var r = RunScript(@"
var target = { alive: 1 };
var ref = new WeakRef(target);
var derefed = ref.deref();
__result__ = derefed.alive;
").Int;
            Assert.That(r, Is.EqualTo(1));
        }

        [Test]
        public void WeakRef_ModifyViaDeref()
        {
            var r = RunScript(@"
var obj = { v: 0 };
var ref = new WeakRef(obj);
ref.deref().v = 99;
__result__ = obj.v;
").Int;
            Assert.That(r, Is.EqualTo(99));
        }
    }
}
