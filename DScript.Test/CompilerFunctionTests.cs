using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;
using Environment = DScript.Vm.Environment;

namespace DScript.Test
{
    // Phase 4: functions, lexical closures, methods/this, new/prototype, natives.
    public class CompilerFunctionTests
    {
        private static ScriptVar Run(string source, ScriptEngine engine = null)
        {
            engine ??= new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            var vm = new VirtualMachine(engine);
            vm.Run(chunk, new Environment(engine.Root, null));
            return engine.Root;
        }

        [Test]
        public void FunctionCallAndReturn()
        {
            Assert.That(Run("function add(a, b) { return a + b; } var r = add(2, 3);").GetParameter("r").Int, Is.EqualTo(5));
        }

        [Test]
        public void MissingArgumentsAreUndefined()
        {
            var src = "function pick(a, b) { if (b == undefined) { return a; } return b; } var r = pick(10);";
            Assert.That(Run(src).GetParameter("r").Int, Is.EqualTo(10));
        }

        [Test]
        public void Recursion()
        {
            var src = "function fact(n) { if (n <= 1) { return 1; } return n * fact(n - 1); } var r = fact(5);";
            Assert.That(Run(src).GetParameter("r").Int, Is.EqualTo(120));
        }

        [Test]
        public void LexicalClosureCapturesDefiningScope()
        {
            var src =
                "function makeCounter() { var n = 0; function inc() { n = n + 1; return n; } return inc; }" +
                "var c = makeCounter(); var a = c(); var b = c();";
            var root = Run(src);
            Assert.That(root.GetParameter("a").Int, Is.EqualTo(1));
            Assert.That(root.GetParameter("b").Int, Is.EqualTo(2));
        }

        [Test]
        public void TwoClosuresHaveIndependentState()
        {
            var src =
                "function makeCounter() { var n = 0; return function() { n = n + 1; return n; }; }" +
                "var c1 = makeCounter(); var c2 = makeCounter();" +
                "var a = c1(); var b = c1(); var d = c2();";
            var root = Run(src);
            Assert.That(root.GetParameter("a").Int, Is.EqualTo(1));
            Assert.That(root.GetParameter("b").Int, Is.EqualTo(2));
            Assert.That(root.GetParameter("d").Int, Is.EqualTo(1)); // independent counter
        }

        [Test]
        public void MethodCallBindsThis()
        {
            var src =
                "function Counter() { this.count = 0; }" +
                "Counter.inc = function() { this.count = this.count + 1; return this.count; };" +
                "var c = new Counter(); var a = c.inc(); var b = c.inc();";
            var root = Run(src);
            Assert.That(root.GetParameter("a").Int, Is.EqualTo(1));
            Assert.That(root.GetParameter("b").Int, Is.EqualTo(2));
            Assert.That(root.GetParameter("c").GetParameter("count").Int, Is.EqualTo(2));
        }

        [Test]
        public void ConstructorFieldsArePerInstance()
        {
            var src =
                "function Point(x, y) { this.x = x; this.y = y; }" +
                "var a = new Point(1, 2); var b = new Point(3, 4);";
            var root = Run(src);
            Assert.That(root.GetParameter("a").GetParameter("x").Int, Is.EqualTo(1));
            Assert.That(root.GetParameter("b").GetParameter("x").Int, Is.EqualTo(3));
        }

        [Test]
        public void InstanceOf()
        {
            var src =
                "function Animal() {} function Dog() {} Dog.prototype = new Animal();" +
                "var d = new Dog();" +
                "var r1 = d instanceof Dog; var r2 = d instanceof Animal; var r3 = d instanceof Object;";
            var root = Run(src);
            Assert.That(root.GetParameter("r1").Bool, Is.True);
            Assert.That(root.GetParameter("r2").Bool, Is.True);  // chain walk
            Assert.That(root.GetParameter("r3").Bool, Is.False);
        }

        [Test]
        public void NativeFunctionDispatch()
        {
            var engine = new ScriptEngine();
            engine.AddNative("function triple(x)", (v, _) => { v.ReturnVar.Int = v.GetParameter("x").Int * 3; }, null);

            var root = Run("var r = triple(7);", engine);
            Assert.That(root.GetParameter("r").Int, Is.EqualTo(21));
        }
    }
}
