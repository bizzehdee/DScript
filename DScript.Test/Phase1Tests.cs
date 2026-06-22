using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    /// <summary>Smoke tests for the Phase 1 feature set.</summary>
    public class Phase1Tests
    {
        private static ScriptVar Run(string source, ScriptEngine engine = null)
        {
            engine ??= new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            var vm = new VirtualMachine(engine);
            vm.Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root;
        }

        // ---- 1. Shorthand object properties --------------------------------

        [Test]
        public void ShorthandObjectProperty_SingleKey()
        {
            var src = "var x = 42; var obj = {x}; var r = obj.x;";
            Assert.That(Run(src).GetParameter("r").Int, Is.EqualTo(42));
        }

        [Test]
        public void ShorthandObjectProperty_MultipleKeys()
        {
            var src = "var a = 1; var b = 2; var obj = {a, b}; var r = obj.a + obj.b;";
            Assert.That(Run(src).GetParameter("r").Int, Is.EqualTo(3));
        }

        [Test]
        public void ShorthandObjectProperty_Mixed()
        {
            var src = "var x = 10; var obj = {x, y: 20}; var r = obj.x + obj.y;";
            Assert.That(Run(src).GetParameter("r").Int, Is.EqualTo(30));
        }

        // ---- 2. String constant folding ------------------------------------

        [Test]
        public void StringConstantFolding_TwoLiterals()
        {
            var src = "var r = \"hello\" + \" world\";";
            Assert.That(Run(src).GetParameter("r").String, Is.EqualTo("hello world"));
        }

        [Test]
        public void StringConstantFolding_ThreeLiterals()
        {
            var src = "var r = \"a\" + \"b\" + \"c\";";
            Assert.That(Run(src).GetParameter("r").String, Is.EqualTo("abc"));
        }

        // ---- 3. Default parameter values -----------------------------------

        [Test]
        public void DefaultParam_UsedWhenNotProvided()
        {
            var src = "function greet(name, greeting = 'Hello') { return greeting + ' ' + name; } var r = greet('World');";
            Assert.That(Run(src).GetParameter("r").String, Is.EqualTo("Hello World"));
        }

        [Test]
        public void DefaultParam_OverriddenWhenProvided()
        {
            var src = "function greet(name, greeting = 'Hello') { return greeting + ' ' + name; } var r = greet('World', 'Hi');";
            Assert.That(Run(src).GetParameter("r").String, Is.EqualTo("Hi World"));
        }

        [Test]
        public void DefaultParam_ArrowFunction()
        {
            var src = "var add = (a, b = 10) => a + b; var r = add(5);";
            Assert.That(Run(src).GetParameter("r").Int, Is.EqualTo(15));
        }

        [Test]
        public void DefaultParam_ArrowFunctionOverridden()
        {
            var src = "var add = (a, b = 10) => a + b; var r = add(5, 3);";
            Assert.That(Run(src).GetParameter("r").Int, Is.EqualTo(8));
        }

        // ---- 4. let keyword ------------------------------------------------

        [Test]
        public void LetDeclaration_WorksLikeVar()
        {
            var src = "let x = 42; var r = x;";
            Assert.That(Run(src).GetParameter("r").Int, Is.EqualTo(42));
        }

        [Test]
        public void LetDeclaration_MultipleBindings()
        {
            var src = "let a = 1, b = 2; var r = a + b;";
            Assert.That(Run(src).GetParameter("r").Int, Is.EqualTo(3));
        }

        // ---- 5. class syntax -----------------------------------------------

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
            Assert.That(Run(src).GetParameter("r").String, Is.EqualTo("Dog speaks"));
        }

        [Test]
        public void Class_StaticMethod()
        {
            var src = @"
class MathHelper {
    static double(x) { return x * 2; }
}
var r = MathHelper.double(21);";
            Assert.That(Run(src).GetParameter("r").Int, Is.EqualTo(42));
        }

        [Test]
        public void Class_Inheritance()
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
            Assert.That(Run(src).GetParameter("r").String, Is.EqualTo("Rex barks"));
        }
    }
}
