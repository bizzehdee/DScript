using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;
using Environment = DScript.Vm.Environment;

namespace DScript.Test
{
    // Automatic Semicolon Insertion (ASI): a statement may be terminated without an
    // explicit ';' when the next token is '}' or end-of-input, or when a line
    // terminator precedes it. A missing ';' with the next token on the SAME line
    // (and not '}'/EOF) is still a syntax error.
    public class AsiTests
    {
        private static ScriptVar RunProgram(string source)
        {
            var chunk = new DScriptCompiler().CompileProgram(source);
            var globals = ScriptVar.CreateObject();
            new VirtualMachine().Run(chunk, new Environment(globals, null));
            return globals;
        }

        private static int IntOf(string source, string varName)
            => RunProgram(source).GetParameter(varName).Int;

        [Test]
        public void NoSemicolonBeforeCloseBrace()
        {
            // The reported col-729 shape: last statement in a block, no ';' before '}'.
            Assert.That(IntOf(
                "function f(){ var t = 0; t += 5 }" +
                "var r = f(); var x = 5;",
                "x"), Is.EqualTo(5));
        }

        [Test]
        public void NoSemicolonBeforeCloseBrace_ReturnsValue()
        {
            Assert.That(IntOf(
                "function f(){ var t = 1; t = t + 41; return t }" +
                "var r = f();",
                "r"), Is.EqualTo(42));
        }

        [Test]
        public void NewlineTerminatesStatement()
        {
            Assert.That(IntOf("var a = 1\nvar b = 2\na = a + b\n", "a"), Is.EqualTo(3));
        }

        [Test]
        public void ReturnWithoutSemicolonAtNewline()
        {
            Assert.That(IntOf(
                "function f(){ var v = 9\n return v }\n var r = f()\n",
                "r"), Is.EqualTo(9));
        }

        [Test]
        public void VarDeclarationNoSemicolonBeforeBrace()
        {
            Assert.That(IntOf("function f(){ var a = 3, b = 4 }\n var x = 1", "x"), Is.EqualTo(1));
        }

        [Test]
        public void BreakAndContinueWithoutSemicolon()
        {
            Assert.That(IntOf(
                "var s = 0; for (var i = 0; i < 10; i = i + 1) { if (i === 5) { break }\n s = s + 1 }",
                "s"), Is.EqualTo(5));
        }

        [Test]
        public void EofWithoutSemicolon()
        {
            // No trailing ';' at end of program.
            Assert.That(IntOf("var done = 7", "done"), Is.EqualTo(7));
        }

        [Test]
        public void NestedForOfBodyWithoutSemicolon()
        {
            // Mirrors the user's `for(...)for(...)expr` shape (expr ends before '}').
            Assert.That(IntOf(
                "var sum = 0; var rows = [[1,2],[3,4]];" +
                "for (const row of rows) for (const v of row) { sum += v }",
                "sum"), Is.EqualTo(10));
        }

        [Test]
        public void MissingSemicolonSameLineStillErrors()
        {
            // Two statements jammed on one line with no ';' and no '}'/EOF/newline
            // between them is NOT valid: `var a = 1 var b = 2`.
            Assert.Throws<ScriptException>(() =>
                new DScriptCompiler().CompileProgram("var a = 1 var b = 2;"));
        }
    }
}
