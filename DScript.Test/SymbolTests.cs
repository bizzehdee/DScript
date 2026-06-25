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
    public class SymbolTests
    {
        private static ScriptVar Run(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r");
        }

        private static string RunStr(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r").String;
        }

        // --- computed keys ---

        [Test]
        public void Symbol_ComputedKeyInObjectLiteral_RoundTrips()
        {
            // { [s]: 123 } stored under key.String instead of the symbol's identity
            // key, so o[s] (which reads via the identity key) returned undefined.
            var r = Run("var s = Symbol(); var o = { [s]: 123 }; var r = o[s];").Int;
            Assert.That(r, Is.EqualTo(123));
        }

        [Test]
        public void ComputedKey_IntegerInObjectLiteral_RoundTrips()
        {
            var r = Run("var o = { [1 + 1]: 7 }; var r = o[2];").Int;
            Assert.That(r, Is.EqualTo(7));
        }

        [Test]
        public void ComputedKey_DuplicateOverwrites()
        {
            var r = Run("var k = 'x'; var o = { [k]: 1, [k]: 2 }; var r = o.x;").Int;
            Assert.That(r, Is.EqualTo(2));
        }

        // --- uniqueness ---

        [Test]
        public void Symbol_TwoSymbolsAreNotEqual()
        {
            var r = Run("var s1 = Symbol(); var s2 = Symbol(); var r = s1 === s2 ? 1 : 0;").Int;
            Assert.That(r, Is.EqualTo(0));
        }

        [Test]
        public void Symbol_SameSymbolIsIdentical()
        {
            var r = Run("var s = Symbol('x'); var r = s === s ? 1 : 0;").Int;
            Assert.That(r, Is.EqualTo(1));
        }

        [Test]
        public void Symbol_WithSameDescriptionAreNotEqual()
        {
            var r = Run("var s1 = Symbol('x'); var s2 = Symbol('x'); var r = s1 === s2 ? 1 : 0;").Int;
            Assert.That(r, Is.EqualTo(0));
        }

        // --- typeof ---

        [Test]
        public void Symbol_TypeofIsSymbol()
        {
            var r = RunStr("var s = Symbol('t'); var r = typeof s;");
            Assert.That(r, Is.EqualTo("symbol"));
        }

        [Test]
        public void Symbol_TypeofAnonymousIsSymbol()
        {
            var r = RunStr("var r = typeof Symbol();");
            Assert.That(r, Is.EqualTo("symbol"));
        }

        // --- property keys ---

        [Test]
        public void Symbol_UsedAsPropertyKey()
        {
            var r = Run("var s = Symbol('key'); var obj = {}; obj[s] = 42; var r = obj[s];").Int;
            Assert.That(r, Is.EqualTo(42));
        }

        [Test]
        public void Symbol_DifferentSymbolsDontCollide()
        {
            var r = Run(@"
var s1 = Symbol('k');
var s2 = Symbol('k');
var obj = {};
obj[s1] = 1;
obj[s2] = 2;
var r = obj[s1] + obj[s2];
").Int;
            Assert.That(r, Is.EqualTo(3));
        }

        // --- Symbol.for / Symbol.keyFor ---

        [Test]
        public void SymbolFor_ReturnsSameSymbolForSameKey()
        {
            var r = Run("var s1 = Symbol.for('app'); var s2 = Symbol.for('app'); var r = s1 === s2 ? 1 : 0;").Int;
            Assert.That(r, Is.EqualTo(1));
        }

        [Test]
        public void SymbolFor_DifferentKeysReturnDifferentSymbols()
        {
            var r = Run("var s1 = Symbol.for('a'); var s2 = Symbol.for('b'); var r = s1 === s2 ? 1 : 0;").Int;
            Assert.That(r, Is.EqualTo(0));
        }

        [Test]
        public void SymbolKeyFor_ReturnsKey()
        {
            var r = RunStr("var s = Symbol.for('myKey'); var r = Symbol.keyFor(s);");
            Assert.That(r, Is.EqualTo("myKey"));
        }

        [Test]
        public void SymbolKeyFor_NonRegisteredSymbolReturnsUndefined()
        {
            var r = Run("var s = Symbol('x'); var r = Symbol.keyFor(s);");
            Assert.That(r.IsUndefined, Is.True);
        }

        // --- Symbol.iterator on custom object ---

        [Test]
        public void SymbolIterator_CustomIterableWorksWithForOf()
        {
            var r = Run(@"
var sum = 0;
var range = {};
range[Symbol.iterator] = function() {
    var i = 0;
    return {
        next: function() {
            if (i < 3) {
                var v = i;
                i = i + 1;
                return { value: v, done: false };
            }
            return { value: undefined, done: true };
        }
    };
};
for (var x of range) {
    sum = sum + x;
}
var r = sum;
").Int;
            Assert.That(r, Is.EqualTo(3)); // 0 + 1 + 2
        }

        // --- well-known symbols accessible ---

        [Test]
        public void Symbol_IteratorIsASymbol()
        {
            var r = RunStr("var r = typeof Symbol.iterator;");
            Assert.That(r, Is.EqualTo("symbol"));
        }

        [Test]
        public void Symbol_HasInstanceIsASymbol()
        {
            var r = RunStr("var r = typeof Symbol.hasInstance;");
            Assert.That(r, Is.EqualTo("symbol"));
        }

        [Test]
        public void Symbol_WellKnownSymbolsAreUnique()
        {
            var r = Run("var r = Symbol.iterator === Symbol.hasInstance ? 1 : 0;").Int;
            Assert.That(r, Is.EqualTo(0));
        }

        // --- Symbol.hasInstance ---

        [Test]
        public void SymbolHasInstance_CustomInstanceofCheck()
        {
            var r = Run(@"
var Even = {};
Even[Symbol.hasInstance] = function(n) {
    return n % 2 === 0;
};
var r = (4 instanceof Even) ? 1 : 0;
").Int;
            Assert.That(r, Is.EqualTo(1));
        }

        [Test]
        public void SymbolHasInstance_CustomInstanceofCheckOdd()
        {
            var r = Run(@"
var Even = {};
Even[Symbol.hasInstance] = function(n) {
    return n % 2 === 0;
};
var r = (3 instanceof Even) ? 1 : 0;
").Int;
            Assert.That(r, Is.EqualTo(0));
        }

        // --- Symbol.prototype.description ---

        [Test]
        public void SymbolDescription_ReturnsDescriptionString()
        {
            var r = RunStr("var s = Symbol('hello'); var r = s.description;");
            Assert.That(r, Is.EqualTo("hello"));
        }

        [Test]
        public void SymbolDescription_AnonymousSymbolIsUndefined()
        {
            var r = Run("var s = Symbol(); var r = s.description;");
            Assert.That(r.IsUndefined, Is.True);
        }

        [Test]
        public void SymbolDescription_EmptyStringDescription()
        {
            var r = RunStr("var s = Symbol(''); var r = s.description;");
            Assert.That(r, Is.EqualTo(""));
        }

        [Test]
        public void SymbolDescription_DoesNotEqualAnotherSymbolsDescription()
        {
            var r = RunStr("var s1 = Symbol('a'); var s2 = Symbol('b'); var r = s1.description;");
            Assert.That(r, Is.EqualTo("a"));
        }
    }
}
