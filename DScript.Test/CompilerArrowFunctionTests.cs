using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;
using Environment = DScript.Vm.Environment;

namespace DScript.Test
{
    public class CompilerArrowFunctionTests
    {
        private static ScriptVar Run(string source, ScriptEngine engine = null)
        {
            engine ??= new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Environment(engine.Root, null));
            return engine.Root;
        }

        [Test]
        public void NoParamExpressionBody()
        {
            var root = Run("var f = () => 42; var r = f();");
            Assert.That(root.GetParameter("r").Int, Is.EqualTo(42));
        }

        [Test]
        public void SingleParamExpressionBody()
        {
            var root = Run("var double = x => x * 2; var r = double(7);");
            Assert.That(root.GetParameter("r").Int, Is.EqualTo(14));
        }

        [Test]
        public void MultiParamExpressionBody()
        {
            var root = Run("var add = (a, b) => a + b; var r = add(3, 4);");
            Assert.That(root.GetParameter("r").Int, Is.EqualTo(7));
        }

        [Test]
        public void BlockBodyWithReturn()
        {
            var root = Run("var f = (x) => { var y = x + 1; return y * 2; }; var r = f(5);");
            Assert.That(root.GetParameter("r").Int, Is.EqualTo(12));
        }

        [Test]
        public void BlockBodyWithoutReturnYieldsUndefined()
        {
            var root = Run("var f = () => { var x = 1; }; var r = f();");
            Assert.That(root.GetParameter("r").IsUndefined, Is.True);
        }

        [Test]
        public void ArrowAsCallback()
        {
            // Pass an arrow to a helper function and call it there
            var src =
                "function apply(fn, x) { return fn(x); }" +
                "var r = apply(x => x * x, 5);";
            var root = Run(src);
            Assert.That(root.GetParameter("r").Int, Is.EqualTo(25));
        }

        [Test]
        public void ArrowClosesOverOuterVariable()
        {
            var root = Run("var n = 10; var f = x => x + n; var r = f(5);");
            Assert.That(root.GetParameter("r").Int, Is.EqualTo(15));
        }

        [Test]
        public void ArrowInsideLoop()
        {
            // Arrow created inside a loop, last one stored, then called
            var src =
                "var fn = null;" +
                "for (var i = 0; i < 3; i++) { fn = n => n * 2; }" +
                "var r = fn(10);";
            var root = Run(src);
            Assert.That(root.GetParameter("r").Int, Is.EqualTo(20));
        }

        [Test]
        public void NestedArrowFunctions()
        {
            var root = Run("var add = a => b => a + b; var r = add(3)(4);");
            Assert.That(root.GetParameter("r").Int, Is.EqualTo(7));
        }
    }
}
