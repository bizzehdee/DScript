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
using NUnit.Framework;

namespace DScript.Test
{
    // --- Test host objects used by the tests ---

    public class SampleHost
    {
        [ScriptVisible]
        public int Add(int a, int b) => a + b;

        [ScriptVisible]
        public string Greet(string name) => $"Hello, {name}!";

        [ScriptVisible]
        public bool IsPositive(int n) => n > 0;

        [ScriptVisible]
        public double Multiply(double x, double y) => x * y;

        [ScriptVisible]
        public void SetValue(int v) { LastSetValue = v; }

        [ScriptVisible]
        public ScriptVar PassThrough(ScriptVar sv) => sv;

        public int LastSetValue { get; private set; }

        // Not exposed — should not be callable from script.
        public int HiddenMethod(int x) => x * 99;
    }

    public class PropHost
    {
        private int _counter = 10;

        [ScriptVisible]
        public int Counter
        {
            get => _counter;
            set => _counter = value;
        }

        [ScriptVisible]
        [ScriptWritable]
        public string Label { get; set; } = "default";

        [ScriptVisible]
        public string ReadOnly { get; } = "readonly_value";
    }

    [TestFixture]
    public class HostObjectTests
    {
        private static ScriptEngine MakeEngine(object host, string globalName = "host")
        {
            var engine = new ScriptEngine();
#pragma warning disable IL2026 // test code — reflection is intentional
            engine.SetGlobal(globalName, host);
#pragma warning restore IL2026
            return engine;
        }

        // --- SetGlobal / method dispatch ---

        [Test]
        public void SetGlobal_AddsObjectToRootScope()
        {
            var engine = MakeEngine(new SampleHost());
            Assert.That(engine.Root.FindChild("host"), Is.Not.Null);
        }

        [Test]
        public void SetGlobal_MethodCallReturnsCorrectInt()
        {
            var engine = MakeEngine(new SampleHost());
            engine.Execute("var r = host.Add(3, 4);");
            Assert.That(engine.Root.FindChild("r")?.Var?.Int, Is.EqualTo(7));
        }

        [Test]
        public void SetGlobal_MethodCallReturnsString()
        {
            var engine = MakeEngine(new SampleHost());
            engine.Execute("var r = host.Greet('World');");
            Assert.That(engine.Root.FindChild("r")?.Var?.String, Is.EqualTo("Hello, World!"));
        }

        [Test]
        public void SetGlobal_MethodCallReturnsBool()
        {
            var engine = MakeEngine(new SampleHost());
            engine.Execute("var r = host.IsPositive(5) ? 1 : 0;");
            Assert.That(engine.Root.FindChild("r")?.Var?.Int, Is.EqualTo(1));
        }

        [Test]
        public void SetGlobal_MethodCallReturnsBoolFalse()
        {
            var engine = MakeEngine(new SampleHost());
            engine.Execute("var r = host.IsPositive(-1) ? 1 : 0;");
            Assert.That(engine.Root.FindChild("r")?.Var?.Int, Is.EqualTo(0));
        }

        [Test]
        public void SetGlobal_MethodCallReturnsDouble()
        {
            var engine = MakeEngine(new SampleHost());
            engine.Execute("var r = host.Multiply(2.5, 4.0);");
            Assert.That(engine.Root.FindChild("r")?.Var?.Float, Is.EqualTo(10.0).Within(0.001));
        }

        [Test]
        public void SetGlobal_VoidMethodCallDoesNotThrow()
        {
            var host = new SampleHost();
            var engine = MakeEngine(host);
            Assert.DoesNotThrow(() => engine.Execute("host.SetValue(42);"));
            Assert.That(host.LastSetValue, Is.EqualTo(42));
        }

        [Test]
        public void SetGlobal_ScriptVarPassThrough()
        {
            var engine = MakeEngine(new SampleHost());
            engine.Execute("var r = host.PassThrough(99);");
            Assert.That(engine.Root.FindChild("r")?.Var?.Int, Is.EqualTo(99));
        }

        [Test]
        public void SetGlobal_HiddenMethodNotAccessible()
        {
            var engine = MakeEngine(new SampleHost());
            // HiddenMethod has no [ScriptVisible] — the child should not exist
            var hostVar = engine.Root.FindChild("host")?.Var;
            Assert.That(hostVar?.FindChild("HiddenMethod"), Is.Null);
        }

        [Test]
        public void SetGlobal_NullObjectCreatesEmptyObject()
        {
            var engine = new ScriptEngine();
#pragma warning disable IL2026
            engine.SetGlobal("empty", null);
#pragma warning restore IL2026
            Assert.That(engine.Root.FindChild("empty"), Is.Not.Null);
        }

        // --- Properties ---

        [Test]
        public void SetGlobal_PropertyGetterIsCreated()
        {
            var engine = MakeEngine(new PropHost(), "ph");
            var phVar = engine.Root.FindChild("ph")?.Var;
            Assert.That(phVar?.FindChild("get_Counter"), Is.Not.Null);
        }

        [Test]
        public void SetGlobal_PropertyGetterReturnsValue()
        {
            var engine = MakeEngine(new PropHost(), "ph");
            engine.Execute("var r = ph.get_Counter();");
            Assert.That(engine.Root.FindChild("r")?.Var?.Int, Is.EqualTo(10));
        }

        [Test]
        public void SetGlobal_WritablePropertySetterIsCreated()
        {
            var engine = MakeEngine(new PropHost(), "ph");
            var phVar = engine.Root.FindChild("ph")?.Var;
            Assert.That(phVar?.FindChild("set_Label"), Is.Not.Null);
        }

        [Test]
        public void SetGlobal_WritablePropertySetterUpdatesHostObject()
        {
            var host = new PropHost();
            var engine = MakeEngine(host, "ph");
            engine.Execute("ph.set_Label('hello');");
            Assert.That(host.Label, Is.EqualTo("hello"));
        }

        [Test]
        public void SetGlobal_ReadOnlyPropertyHasNoSetter()
        {
            var engine = MakeEngine(new PropHost(), "ph");
            var phVar = engine.Root.FindChild("ph")?.Var;
            // ReadOnly has no [ScriptWritable], so set_ReadOnly must not exist
            Assert.That(phVar?.FindChild("set_ReadOnly"), Is.Null);
        }

        [Test]
        public void SetGlobal_MultipleGlobals_BothAccessible()
        {
            var engine = new ScriptEngine();
#pragma warning disable IL2026
            engine.SetGlobal("h1", new SampleHost());
            engine.SetGlobal("h2", new SampleHost());
#pragma warning restore IL2026
            engine.Execute("var r = h1.Add(1, 2) + h2.Add(3, 4);");
            Assert.That(engine.Root.FindChild("r")?.Var?.Int, Is.EqualTo(10));
        }

        // --- ScriptVisibleAttribute / ScriptWritableAttribute ---

        [Test]
        public void ScriptVisibleAttribute_CanBeAppliedToMethod()
        {
            var attr = new ScriptVisibleAttribute();
            Assert.That(attr, Is.Not.Null);
        }

        [Test]
        public void ScriptWritableAttribute_CanBeAppliedToProperty()
        {
            var attr = new ScriptWritableAttribute();
            Assert.That(attr, Is.Not.Null);
        }
    }
}
