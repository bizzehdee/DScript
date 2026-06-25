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
    /// <summary>Tests for the <c>class</c> syntax: constructors, methods, static members, and inheritance.</summary>
    public class ClassSyntaxTests
    {
        private static string RunStr(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r").String;
        }

        private static int RunInt(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r").Int;
        }

        [Test]
        public void Class_ConstructorAndMethod()
        {
            var src = @"
class Animal {
    constructor(name) { this.name = name; }
    speak() { return this.name + ' speaks'; }
}
var a = new Animal('Dog');
var r = a.speak();";
            Assert.That(RunStr(src), Is.EqualTo("Dog speaks"));
        }

        [Test]
        public void Class_StaticMethod()
        {
            var src = @"
class MathHelper {
    static double(x) { return x * 2; }
}
var r = MathHelper.double(21);";
            Assert.That(RunInt(src), Is.EqualTo(42));
        }

        [Test]
        public void Class_Inheritance_OverridesMethod()
        {
            var src = @"
class Animal {
    constructor(name) { this.name = name; }
    speak() { return this.name + ' makes a sound'; }
}
class Dog extends Animal {
    constructor(name) { super(name); }
    speak() { return this.name + ' barks'; }
}
var d = new Dog('Rex');
var r = d.speak();";
            Assert.That(RunStr(src), Is.EqualTo("Rex barks"));
        }

        [Test]
        public void SuperMethodCall_InvokesParentMethod()
        {
            // super.m() — previously a parse error ("Expected ;, found .").
            var src = "class A { m() { return 1; } } " +
                      "class B extends A { m() { return super.m() + 1; } } " +
                      "var r = new B().m();";
            Assert.That(RunInt(src), Is.EqualTo(2));
        }

        [Test]
        public void SuperMethodCall_PassesThisAndArgs()
        {
            var src = "class A { add(n) { return this.base + n; } } " +
                      "class B extends A { constructor() { super(); this.base = 10; } " +
                      "  add(n) { return super.add(n) * 2; } } " +
                      "var r = new B().add(5);"; // (10+5)*2
            Assert.That(RunInt(src), Is.EqualTo(30));
        }

        [Test]
        public void SuperPropertyRead_ReadsParentProperty()
        {
            var src = "class A { getV() { return 7; } } " +
                      "class B extends A { read() { return super.getV; } } " +
                      "var r = new B().read()();"; // read() returns the fn, then call it
            Assert.That(RunInt(src), Is.EqualTo(7));
        }
    }
}
