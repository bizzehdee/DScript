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

// ReSharper disable once CheckNamespace

namespace DScript.Test
{
    /// <summary>
    /// Correctness tests for the Lever-1 flat-slot storage optimisation on shaped
    /// (class-instance) objects.  Each test exercises property reads / writes via
    /// the shape-keyed inline cache and the flat _slots array so regressions in
    /// the slot-index path surface immediately.
    /// </summary>
    [TestFixture]
    public class FlatSlotStorageTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        // ── slot read / write ─────────────────────────────────────────────────

        [Test]
        public void ShapedObject_TwoProps_ReadBothSlots()
        {
            // slot 0 (x) and slot 1 (y) — verifies direct array access for both indices
            var result = RunScript(
                "class P { constructor(x,y) { this.x=x; this.y=y; } }" +
                "var o = new P(3, 7);" +
                "var __result__ = o.x * 10 + o.y;");
            Assert.That(result.Int, Is.EqualTo(37));
        }

        [Test]
        public void ShapedObject_FourProps_ReadAllSlots()
        {
            // slot 0,1,2,3 — exercises all indices from a class with 4 properties
            var result = RunScript(
                "class Q { constructor(a,b,c,d) { this.a=a; this.b=b; this.c=c; this.d=d; } }" +
                "var o = new Q(1,2,3,4);" +
                "var __result__ = o.a + o.b * 10 + o.c * 100 + o.d * 1000;");
            Assert.That(result.Int, Is.EqualTo(4321));
        }

        [Test]
        public void ShapedObject_PropertyUpdate_SlotReflectsNewValue()
        {
            // write then overwrite: slot must reflect the latest value
            var result = RunScript(
                "class P { constructor(x) { this.x=x; } }" +
                "var o = new P(10);" +
                "o.x = 99;" +
                "var __result__ = o.x;");
            Assert.That(result.Int, Is.EqualTo(99));
        }

        [Test]
        public void ShapedObject_ManyProperties_BeyondInitialCapacity()
        {
            // 8 properties — forces _slots to grow beyond the initial capacity-4 array
            var result = RunScript(
                "class Big {" +
                "  constructor() {" +
                "    this.a=1; this.b=2; this.c=3; this.d=4;" +
                "    this.e=5; this.f=6; this.g=7; this.h=8;" +
                "  }" +
                "}" +
                "var o = new Big();" +
                "var __result__ = o.a+o.b+o.c+o.d+o.e+o.f+o.g+o.h;");
            Assert.That(result.Int, Is.EqualTo(36));
        }

        [Test]
        public void ShapedObject_PropertyWrite_UpdatesSlotValue()
        {
            // write to a deep slot (index 3) and read it back
            var result = RunScript(
                "class Q { constructor(a,b,c,d) { this.a=a; this.b=b; this.c=c; this.d=d; } }" +
                "var o = new Q(1,2,3,4);" +
                "o.d = 40;" +
                "var __result__ = o.a + o.b + o.c + o.d;");
            Assert.That(result.Int, Is.EqualTo(46)); // 1+2+3+40
        }

        // ── enumeration order ─────────────────────────────────────────────────

        [Test]
        public void ShapedObject_ForIn_EnumerationOrderPreserved()
        {
            // for-in must yield properties in insertion order even with flat slots
            var result = RunScript(
                "class P { constructor() { this.x=1; this.y=2; this.z=3; } }" +
                "var o = new P(); var keys = [];" +
                "for (var k in o) { keys.push(k); }" +
                "var __result__ = keys.join(',');");
            Assert.That(result.String, Is.EqualTo("x,y,z"));
        }

        [Test]
        public void ShapedObject_ObjectKeys_ReturnsInsertionOrder()
        {
            var result = RunScript(
                "class P { constructor() { this.a=1; this.b=2; this.c=3; } }" +
                "var o = new P();" +
                "var __result__ = Object.keys(o).join(',');");
            Assert.That(result.String, Is.EqualTo("a,b,c"));
        }

        // ── shape invalidation ────────────────────────────────────────────────

        [Test]
        public void ShapedObject_PropertyDelete_ShapeInvalidatedButReadStillWorks()
        {
            // deleting a property invalidates the shape; remaining properties still readable
            var result = RunScript(
                "class P { constructor(x,y) { this.x=x; this.y=y; } }" +
                "var o = new P(3,7);" +
                "delete o.x;" +
                "var __result__ = o.y;");
            Assert.That(result.Int, Is.EqualTo(7));
        }

        [Test]
        public void ShapedObject_GetterInstalled_ShapeInvalidatedGracefully()
        {
            // installing a getter on a shaped property drops shape tracking;
            // the getter must still return the correct value
            var result = RunScript(
                "class P { constructor(v) { this.v=v; } }" +
                "var o = new P(42);" +
                "Object.defineProperty(o, 'v', { get: function() { return 99; } });" +
                "var __result__ = o.v;");
            Assert.That(result.Int, Is.EqualTo(99));
        }

        // ── inline cache correctness with flat slots ───────────────────────────

        [Test]
        public void ShapedObjects_SameShapeInHotLoop_InlineCacheHits()
        {
            // 10k objects with the same shape — cache stays warm and all reads
            // go through the shape-keyed O(1) slot path
            var result = RunScript(
                "class P { constructor(x,y) { this.x=x; this.y=y; } }" +
                "var sum = 0;" +
                "for (var i = 0; i < 10000; i++) {" +
                "  var o = new P(i, i+1);" +
                "  sum += o.x + o.y;" +
                "}" +
                "var __result__ = sum;");
            // sum of (i + i+1) for i=0..9999 = sum(2i+1) = 2*(0+...+9999) + 10000
            // = 2*49995000 + 10000 = 100000000
            Assert.That(result.Int, Is.EqualTo(100000000));
        }

        [Test]
        public void ShapedObjects_PolymorphicShapes_BothCached()
        {
            // two shapes alternate — bimorphic inline cache must handle both correctly
            var result = RunScript(
                "class A { constructor(x) { this.x=x; } }" +
                "class B { constructor(x,y) { this.x=x; this.y=y; } }" +
                "var sumA = 0, sumB = 0;" +
                "for (var i = 0; i < 100; i++) {" +
                "  var a = new A(i); sumA += a.x;" +
                "  var b = new B(i, i*2); sumB += b.x + b.y;" +
                "}" +
                "var __result__ = sumA + sumB;");
            // sumA = 0+...+99 = 4950; sumB = sum(i + 2i) = sum(3i) = 3*4950 = 14850
            Assert.That(result.Int, Is.EqualTo(4950 + 14850));
        }

        // ── prototype chain with flat slots ───────────────────────────────────

        [Test]
        public void ShapedObject_PrototypeRead_FallsThroughToParent()
        {
            // property on the prototype must still be found when the instance
            // doesn't have the property in its own slots
            var result = RunScript(
                "class Animal { speak() { return 42; } }" +
                "var a = new Animal();" +
                "var __result__ = a.speak();");
            Assert.That(result.Int, Is.EqualTo(42));
        }

        [Test]
        public void ShapedObject_InheritedInstanceAndOwnProps_BothReadable()
        {
            var result = RunScript(
                "class Base { constructor() { this.base=1; } }" +
                "class Derived extends Base { constructor() { super(); this.own=2; } }" +
                "var o = new Derived();" +
                "var __result__ = o.base * 10 + o.own;");
            Assert.That(result.Int, Is.EqualTo(12));
        }

    }
}
