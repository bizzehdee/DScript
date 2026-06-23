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
    public class StaticInitBlockTests
    {
        private static ScriptVar Run(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r");
        }

        [Test]
        public void StaticBlock_RunsOnceAtClassDefinition()
        {
            var r = Run(@"
var ran = 0;
class Foo {
    static {
        ran = ran + 1;
    }
}
var r = ran;
").Int;
            Assert.That(r, Is.EqualTo(1));
        }

        [Test]
        public void StaticBlock_CanSetStaticPropertyViaThis()
        {
            var r = Run(@"
class Config {
    static {
        this.value = 42;
    }
}
var r = Config.value;
").Int;
            Assert.That(r, Is.EqualTo(42));
        }

        [Test]
        public void StaticBlock_RunsBeforeFirstInstanceCreation()
        {
            var r = Run(@"
var order = 0;
class Counter {
    static {
        order = 1;
    }
    constructor() {
        order = order + 10;
    }
}
var c = new Counter();
var r = order;
").Int;
            Assert.That(r, Is.EqualTo(11));
        }

        [Test]
        public void StaticBlock_CanReferenceStaticMethods()
        {
            var r = Run(@"
class Util {
    static compute() {
        return 7;
    }
    static {
        this.result = this.compute() * 6;
    }
}
var r = Util.result;
").Int;
            Assert.That(r, Is.EqualTo(42));
        }

        [Test]
        public void StaticBlock_MultipleBlocksRunInOrder()
        {
            var r = Run(@"
class Seq {
    static {
        this.v = 1;
    }
    static {
        this.v = this.v + 10;
    }
}
var r = Seq.v;
").Int;
            Assert.That(r, Is.EqualTo(11));
        }
    }
}
