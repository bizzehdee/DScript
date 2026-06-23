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
    public class ArgumentsTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        [Test]
        public void Arguments_SumViaLoop_ReturnsCorrectTotal()
        {
            var r = RunScript(@"
function sum() {
    var total = 0;
    for (var i = 0; i < arguments.length; i++) {
        total += arguments[i];
    }
    return total;
}
__result__ = sum(1, 2, 3);
");
            Assert.That(r.Int, Is.EqualTo(6));
        }

        [Test]
        public void Arguments_NoArgs_LengthIsZero()
        {
            var r = RunScript(@"
function f() { return arguments.length; }
__result__ = f();
");
            Assert.That(r.Int, Is.EqualTo(0));
        }

        [Test]
        public void Arguments_ExtraArgBeyondDeclaredParams_Accessible()
        {
            var r = RunScript(@"
function f(a, b) { return arguments[2]; }
__result__ = f(1, 2, 99);
");
            Assert.That(r.Int, Is.EqualTo(99));
        }

        [Test]
        public void Arguments_ArrayFrom_ReturnsFirstArgument()
        {
            var r = RunScript(@"
function f() { return Array.from(arguments)[0]; }
__result__ = f(42, 99);
");
            Assert.That(r.Int, Is.EqualTo(42));
        }

        [Test]
        public void Arguments_ArrowFunction_HasNoArgumentsBinding()
        {
            var r = RunScript(@"
var f = () => typeof arguments;
__result__ = f();
");
            Assert.That(r.String, Is.EqualTo("undefined"));
        }
    }
}
