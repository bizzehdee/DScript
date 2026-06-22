using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;
using Environment = DScript.Vm.Environment;

namespace DScript.Test
{
    public class CompilerTemplateLiteralTests
    {
        private static ScriptVar Run(string source, ScriptEngine engine = null)
        {
            engine ??= new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Environment(engine.Root, null));
            return engine.Root;
        }

        [Test]
        public void PlainStringTemplate()
        {
            var root = Run("var r = `hello world`;");
            Assert.That(root.GetParameter("r").String, Is.EqualTo("hello world"));
        }

        [Test]
        public void EmptyTemplate()
        {
            var root = Run("var r = ``;");
            Assert.That(root.GetParameter("r").String, Is.EqualTo(""));
        }

        [Test]
        public void SingleInterpolation()
        {
            var root = Run("var name = \"World\"; var r = `Hello, ${name}!`;");
            Assert.That(root.GetParameter("r").String, Is.EqualTo("Hello, World!"));
        }

        [Test]
        public void InterpolationAtStart()
        {
            var root = Run("var x = 42; var r = `${x} is the answer`;");
            Assert.That(root.GetParameter("r").String, Is.EqualTo("42 is the answer"));
        }

        [Test]
        public void InterpolationAtEnd()
        {
            var root = Run("var x = 7; var r = `value: ${x}`;");
            Assert.That(root.GetParameter("r").String, Is.EqualTo("value: 7"));
        }

        [Test]
        public void MultipleInterpolations()
        {
            var root = Run("var a = 3; var b = 4; var r = `${a} + ${b} = ${a + b}`;");
            Assert.That(root.GetParameter("r").String, Is.EqualTo("3 + 4 = 7"));
        }

        [Test]
        public void InterpolationWithExpression()
        {
            var root = Run("var x = 5; var r = `square: ${x * x}`;");
            Assert.That(root.GetParameter("r").String, Is.EqualTo("square: 25"));
        }

        [Test]
        public void EscapeNewline()
        {
            var root = Run("var r = `line1\\nline2`;");
            Assert.That(root.GetParameter("r").String, Is.EqualTo("line1\nline2"));
        }

        [Test]
        public void EscapeBacktick()
        {
            var root = Run("var r = `back\\`tick`;");
            Assert.That(root.GetParameter("r").String, Is.EqualTo("back`tick"));
        }

        [Test]
        public void EscapedDollarNotInterpolated()
        {
            var root = Run("var r = `price: \\$${42}`;");
            Assert.That(root.GetParameter("r").String, Is.EqualTo("price: $42"));
        }

        [Test]
        public void TemplateWithFunctionCall()
        {
            var root = Run(
                "function greet(n) { return `Hello, ${n}!`; }" +
                "var r = greet(\"Alice\");");
            Assert.That(root.GetParameter("r").String, Is.EqualTo("Hello, Alice!"));
        }

        [Test]
        public void OnlyExpression()
        {
            var root = Run("var n = 99; var r = `${n}`;");
            Assert.That(root.GetParameter("r").String, Is.EqualTo("99"));
        }
    }
}
