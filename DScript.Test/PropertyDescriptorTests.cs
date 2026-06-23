using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class PropertyDescriptorTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        // ── getter / setter in object literals ───────────────────────────────

        [Test]
        public void Getter_IsInvokedOnPropertyRead()
        {
            var result = RunScript(@"
                var obj = { get value() { return 42; } };
                var __result__ = obj.value;
            ");
            Assert.That(result.Int, Is.EqualTo(42));
        }

        [Test]
        public void Setter_IsInvokedOnPropertyWrite()
        {
            var result = RunScript(@"
                var stored = 0;
                var obj = {
                    get x() { return stored; },
                    set x(v) { stored = v * 2; }
                };
                obj.x = 5;
                var __result__ = obj.x;
            ");
            Assert.That(result.Int, Is.EqualTo(10));
        }

        [Test]
        public void GetterOnly_WritesAreIgnoredInSloppyMode()
        {
            var result = RunScript(@"
                var obj = { get ro() { return 99; } };
                obj.ro = 1;
                var __result__ = obj.ro;
            ");
            Assert.That(result.Int, Is.EqualTo(99));
        }

        // ── method shorthand in object literals ──────────────────────────────

        [Test]
        public void MethodShorthand_IsCallable()
        {
            var result = RunScript(@"
                var obj = { add(a, b) { return a + b; } };
                var __result__ = obj.add(3, 4);
            ");
            Assert.That(result.Int, Is.EqualTo(7));
        }

        // ── getter / setter in class bodies ──────────────────────────────────

        [Test]
        public void ClassGetter_IsInvokedOnPropertyRead()
        {
            var result = RunScript(@"
                class Circle {
                    constructor(r) { this.r = r; }
                    get area() { return this.r * this.r * 3; }
                }
                var c = new Circle(4);
                var __result__ = c.area;
            ");
            Assert.That(result.Int, Is.EqualTo(48));
        }

        [Test]
        public void ClassSetter_IsInvokedOnPropertyWrite()
        {
            var result = RunScript(@"
                class Box {
                    constructor() { this._w = 0; }
                    get width() { return this._w; }
                    set width(v) { this._w = v + 1; }
                }
                var b = new Box();
                b.width = 9;
                var __result__ = b.width;
            ");
            Assert.That(result.Int, Is.EqualTo(10));
        }

        // ── Object.freeze ─────────────────────────────────────────────────────

        [Test]
        public void ObjectFreeze_PreventsMutation()
        {
            var result = RunScript(@"
                var obj = { x: 1 };
                Object.freeze(obj);
                obj.x = 99;
                var __result__ = obj.x;
            ");
            Assert.That(result.Int, Is.EqualTo(1));
        }

        [Test]
        public void ObjectIsFrozen_ReturnsTrueAfterFreeze()
        {
            var result = RunScript(@"
                var obj = { x: 1 };
                Object.freeze(obj);
                var __result__ = Object.isFrozen(obj) ? 1 : 0;
            ");
            Assert.That(result.Int, Is.EqualTo(1));
        }

        [Test]
        public void ObjectIsFrozen_ReturnsFalseBeforeFreeze()
        {
            var result = RunScript(@"
                var obj = { x: 1 };
                var __result__ = Object.isFrozen(obj) ? 1 : 0;
            ");
            Assert.That(result.Int, Is.EqualTo(0));
        }

        // ── Object.seal ──────────────────────────────────────────────────────

        [Test]
        public void ObjectSeal_PreventsNewProperties()
        {
            var result = RunScript(@"
                var obj = { x: 1 };
                Object.seal(obj);
                obj.y = 99;
                var __result__ = obj.y === undefined ? 1 : 0;
            ");
            Assert.That(result.Int, Is.EqualTo(1));
        }

        [Test]
        public void ObjectSeal_AllowsValueWrites()
        {
            var result = RunScript(@"
                var obj = { x: 1 };
                Object.seal(obj);
                obj.x = 42;
                var __result__ = obj.x;
            ");
            Assert.That(result.Int, Is.EqualTo(42));
        }

        [Test]
        public void ObjectIsSealed_ReturnsTrueAfterSeal()
        {
            var result = RunScript(@"
                var obj = { x: 1 };
                Object.seal(obj);
                var __result__ = Object.isSealed(obj) ? 1 : 0;
            ");
            Assert.That(result.Int, Is.EqualTo(1));
        }

        // ── Object.preventExtensions ─────────────────────────────────────────

        [Test]
        public void PreventExtensions_BlocksNewProperty()
        {
            var result = RunScript(@"
                var obj = { x: 1 };
                Object.preventExtensions(obj);
                obj.y = 99;
                var __result__ = obj.y === undefined ? 1 : 0;
            ");
            Assert.That(result.Int, Is.EqualTo(1));
        }

        [Test]
        public void IsExtensible_ReturnsFalseAfterPreventExtensions()
        {
            var result = RunScript(@"
                var obj = {};
                Object.preventExtensions(obj);
                var __result__ = Object.isExtensible(obj) ? 1 : 0;
            ");
            Assert.That(result.Int, Is.EqualTo(0));
        }

        // ── Object.create ────────────────────────────────────────────────────

        [Test]
        public void ObjectCreate_ChildInheritsProtoMethods()
        {
            var result = RunScript(@"
                var proto = { greet() { return 'hello'; } };
                var child = Object.create(proto);
                var __result__ = child.greet();
            ");
            Assert.That(result.String, Is.EqualTo("hello"));
        }

        // ── Object.getOwnPropertyDescriptor ──────────────────────────────────

        [Test]
        public void GetOwnPropertyDescriptor_ReturnsCorrectShape()
        {
            var result = RunScript(@"
                var obj = { x: 5 };
                var d = Object.getOwnPropertyDescriptor(obj, 'x');
                var __result__ = d.value;
            ");
            Assert.That(result.Int, Is.EqualTo(5));
        }

        [Test]
        public void GetOwnPropertyDescriptor_ReturnsUndefinedForMissingKey()
        {
            var result = RunScript(@"
                var obj = { x: 5 };
                var d = Object.getOwnPropertyDescriptor(obj, 'y');
                var __result__ = d === undefined ? 1 : 0;
            ");
            Assert.That(result.Int, Is.EqualTo(1));
        }

        // ── Object.defineProperty ─────────────────────────────────────────────

        [Test]
        public void DefineProperty_NonEnumerable_DoesNotAppearInForIn()
        {
            var result = RunScript(@"
                var obj = { a: 1 };
                Object.defineProperty(obj, 'b', { value: 2, writable: true, enumerable: false, configurable: true });
                var keys = [];
                for (var k in obj) { keys.push(k); }
                var __result__ = keys.length;
            ");
            Assert.That(result.Int, Is.EqualTo(1));
        }

        [Test]
        public void DefineProperty_NonWritable_IgnoresWrite()
        {
            var result = RunScript(@"
                var obj = {};
                Object.defineProperty(obj, 'c', { value: 10, writable: false, enumerable: true, configurable: false });
                obj.c = 99;
                var __result__ = obj.c;
            ");
            Assert.That(result.Int, Is.EqualTo(10));
        }

        [Test]
        public void DefineProperty_AccessorDescriptor_Works()
        {
            var result = RunScript(@"
                var obj = {};
                var _v = 0;
                Object.defineProperty(obj, 'val', {
                    get: function() { return _v; },
                    set: function(v) { _v = v * 3; }
                });
                obj.val = 4;
                var __result__ = obj.val;
            ");
            Assert.That(result.Int, Is.EqualTo(12));
        }

        // ── Object.keys/values/entries respect Enumerable ─────────────────────

        [Test]
        public void ObjectKeys_SkipsNonEnumerableProperties()
        {
            var result = RunScript(@"
                var obj = { a: 1 };
                Object.defineProperty(obj, 'hidden', { value: 2, enumerable: false });
                var __result__ = Object.keys(obj).length;
            ");
            Assert.That(result.Int, Is.EqualTo(1));
        }
    }
}
