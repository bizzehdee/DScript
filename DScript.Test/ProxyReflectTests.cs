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
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class ProxyReflectTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            var compiler = new DScriptCompiler();
            var chunk = compiler.CompileProgram(code);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r");
        }

        // ===== Reflect tests =====

        [Test]
        public void Reflect_Get_ReadsProperty()
        {
            var r = RunScript(@"
var obj = { x: 42 };
var r = Reflect.get(obj, 'x');
");
            Assert.That(r.Int, Is.EqualTo(42));
        }

        [Test]
        public void Reflect_Set_WritesProperty()
        {
            var r = RunScript(@"
var obj = {};
Reflect.set(obj, 'x', 99);
var r = obj.x;
");
            Assert.That(r.Int, Is.EqualTo(99));
        }

        [Test]
        public void Reflect_Has_ReturnsTrueForExistingKey()
        {
            var r = RunScript(@"
var obj = { a: 1 };
var r = Reflect.has(obj, 'a');
");
            Assert.That(r.Bool, Is.True);
        }

        [Test]
        public void Reflect_Has_ReturnsFalseForMissingKey()
        {
            var r = RunScript(@"
var obj = { a: 1 };
var r = Reflect.has(obj, 'b');
");
            Assert.That(r.Bool, Is.False);
        }

        [Test]
        public void Reflect_DeleteProperty_RemovesKey()
        {
            var r = RunScript(@"
var obj = { x: 1, y: 2 };
Reflect.deleteProperty(obj, 'x');
var r = Reflect.has(obj, 'x');
");
            Assert.That(r.Bool, Is.False);
        }

        [Test]
        public void Reflect_Apply_CallsFunction()
        {
            var r = RunScript(@"
function add(a, b) { return a + b; }
var r = Reflect.apply(add, null, [3, 4]);
");
            Assert.That(r.Int, Is.EqualTo(7));
        }

        [Test]
        public void Reflect_Apply_PassesThisArg()
        {
            var r = RunScript(@"
function getX() { return this.x; }
var obj = { x: 55 };
var r = Reflect.apply(getX, obj, []);
");
            Assert.That(r.Int, Is.EqualTo(55));
        }

        [Test]
        public void Reflect_OwnKeys_ReturnsPropertyNames()
        {
            var r = RunScript(@"
var obj = { a: 1, b: 2, c: 3 };
var keys = Reflect.ownKeys(obj);
var r = keys.length;
");
            Assert.That(r.Int, Is.EqualTo(3));
        }

        [Test]
        public void Reflect_Construct_CreatesInstance()
        {
            var r = RunScript(@"
function Point(x, y) { this.x = x; this.y = y; }
var p = Reflect.construct(Point, [3, 4]);
var r = p.x + p.y;
");
            Assert.That(r.Int, Is.EqualTo(7));
        }

        [Test]
        public void Reflect_GetPrototypeOf_ReturnsProto()
        {
            var r = RunScript(@"
function Foo() {}
var f = new Foo();
var proto = Reflect.getPrototypeOf(f);
var r = proto === Foo;
");
            Assert.That(r.Bool, Is.True);
        }

        [Test]
        public void Reflect_SetPrototypeOf_ChangesProto()
        {
            var r = RunScript(@"
var obj = {};
var proto = { x: 42 };
Reflect.setPrototypeOf(obj, proto);
var r = Reflect.getPrototypeOf(obj) === proto;
");
            Assert.That(r.Bool, Is.True);
        }

        [Test]
        public void Reflect_DefineProperty_SetsValue()
        {
            var r = RunScript(@"
var obj = {};
Reflect.defineProperty(obj, 'z', { value: 77 });
var r = obj.z;
");
            Assert.That(r.Int, Is.EqualTo(77));
        }

        [Test]
        public void Reflect_GetOwnPropertyDescriptor_ReturnsDescriptor()
        {
            var r = RunScript(@"
var obj = { n: 10 };
var desc = Reflect.getOwnPropertyDescriptor(obj, 'n');
var r = desc.value;
");
            Assert.That(r.Int, Is.EqualTo(10));
        }

        // ===== Proxy tests =====

        [Test]
        public void Proxy_GetTrap_InterceptsRead()
        {
            var r = RunScript(@"
var handler = {
    get: function(target, key) { return key === 'x' ? 99 : target[key]; }
};
var proxy = new Proxy({ x: 1 }, handler);
var r = proxy.x;
");
            Assert.That(r.Int, Is.EqualTo(99));
        }

        [Test]
        public void Proxy_SetTrap_InterceptsWrite()
        {
            var r = RunScript(@"
var count = 0;
var handler = {
    set: function(target, key, value) { count = count + 1; target[key] = value; }
};
var proxy = new Proxy({}, handler);
proxy.a = 1;
proxy.b = 2;
var r = count;
");
            Assert.That(r.Int, Is.EqualTo(2));
        }

        [Test]
        public void Proxy_HasTrap_InterceptsInOperator()
        {
            var r = RunScript(@"
var handler = {
    has: function(target, key) { return key === 'secret' || key in target; }
};
var proxy = new Proxy({}, handler);
var r = 'secret' in proxy;
");
            Assert.That(r.Bool, Is.True);
        }

        [Test]
        public void Proxy_DeleteTrap_InterceptsDelete()
        {
            var r = RunScript(@"
var deleteCount = 0;
var handler = {
    deleteProperty: function(target, key) { deleteCount = deleteCount + 1; delete target[key]; }
};
var proxy = new Proxy({ x: 1 }, handler);
delete proxy.x;
var r = deleteCount;
");
            Assert.That(r.Int, Is.EqualTo(1));
        }

        [Test]
        public void Proxy_ApplyTrap_InterceptsCall()
        {
            var r = RunScript(@"
var handler = {
    apply: function(target, thisArg, args) { return args[0] * 2; }
};
var proxy = new Proxy(function(x) { return x; }, handler);
var r = proxy(21);
");
            Assert.That(r.Int, Is.EqualTo(42));
        }

        [Test]
        public void Proxy_NoTrap_ForwardsToTarget()
        {
            var r = RunScript(@"
var proxy = new Proxy({ x: 7 }, {});
var r = proxy.x;
");
            Assert.That(r.Int, Is.EqualTo(7));
        }

        [Test]
        public void Proxy_Revocable_WorksBeforeRevoke()
        {
            var r = RunScript(@"
var result = Proxy.revocable({ x: 5 }, {});
var proxy = result.proxy;
var r = proxy.x;
");
            Assert.That(r.Int, Is.EqualTo(5));
        }

        [Test]
        public void Proxy_Revocable_RevokeNullsTarget()
        {
            // After revoke(), ProxyTarget is null; accessing should yield undefined
            var r = RunScript(@"
var result = Proxy.revocable({ x: 5 }, {});
var proxy = result.proxy;
result.revoke();
var r = proxy.x;
");
            // after revoke the target is nulled, so proxy.x returns undefined → Int = 0
            Assert.That(r.IsUndefined, Is.True);
        }
    }
}
