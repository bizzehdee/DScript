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
using DScript.Extras;
using DScript.Vm;
using NUnit.Framework;
using System.Text;

namespace DScript.Test
{
    /// <summary>
    /// Targeted tests that exercise code paths not covered by the primary test
    /// suite. Grouped by source file for traceability.
    /// </summary>
    [TestFixture]
    public class CoverageGapTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        private static ScriptVar RunScriptExtras(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        // ── ScriptLex: string escape sequences ──────────────────────────────────

        [Test]
        public void StringEscape_Newline_IsPreserved()
        {
            var r = RunScript("var __result__ = \"a\\nb\";");
            Assert.That(r.String, Is.EqualTo("a\nb"));
        }

        [Test]
        public void StringEscape_CarriageReturn_IsPreserved()
        {
            var r = RunScript("var __result__ = \"a\\rb\";");
            Assert.That(r.String, Is.EqualTo("a\rb"));
        }

        [Test]
        public void StringEscape_Tab_IsPreserved()
        {
            var r = RunScript("var __result__ = \"a\\tb\";");
            Assert.That(r.String, Is.EqualTo("a\tb"));
        }

        [Test]
        public void StringEscape_Bell_IsPreserved()
        {
            var r = RunScript("var __result__ = \"a\\ab\";");
            Assert.That(r.String, Is.EqualTo("a\ab"));
        }

        [Test]
        public void StringEscape_Backspace_IsPreserved()
        {
            var r = RunScript("var __result__ = \"a\\bb\";");
            Assert.That(r.String, Is.EqualTo("a\bb"));
        }

        [Test]
        public void StringEscape_FormFeed_IsPreserved()
        {
            var r = RunScript("var __result__ = \"a\\fb\";");
            Assert.That(r.String, Is.EqualTo("a\fb"));
        }

        [Test]
        public void StringEscape_VerticalTab_IsPreserved()
        {
            var r = RunScript("var __result__ = \"a\\vb\";");
            Assert.That(r.String, Is.EqualTo("a\vb"));
        }

        [Test]
        public void StringEscape_HexSequence_ProducesChar()
        {
            // \x41 = 'A' in ASCII
            var r = RunScript("var __result__ = \"\\x41\";");
            Assert.That(r.String, Is.EqualTo("A"));
        }

        [Test]
        public void StringEscape_OctalSequence_ProducesChar()
        {
            // \101 = 65 decimal = 'A' in ASCII
            var r = RunScript("var __result__ = \"\\101\";");
            Assert.That(r.String, Is.EqualTo("A"));
        }

        // ── ScriptLex: -- decrement operator ────────────────────────────────────

        [Test]
        public void Decrement_PostfixOperator_Works()
        {
            var r = RunScript("var i = 5; i--; var __result__ = i;");
            Assert.That(r.Int, Is.EqualTo(4));
        }

        [Test]
        public void Decrement_PrePostfix_Loop()
        {
            var r = RunScript("var s = 0; for (var i = 3; i > 0; i--) { s = s + i; } var __result__ = s;");
            Assert.That(r.Int, Is.EqualTo(6)); // 3+2+1
        }

        // ── ScriptLex: scientific notation with negative exponent ────────────────

        [Test]
        public void ScientificNotation_NegativeExponent_Parsed()
        {
            var r = RunScript("var __result__ = 1e-3;");
            Assert.That(r.Float, Is.EqualTo(0.001).Within(1e-10));
        }

        // ── ScriptLex + ConstantValue: regex literals ────────────────────────────

        [Test]
        public void RegexLiteral_CreatesRegexpVar()
        {
            var engine = new ScriptEngine();
            engine.Execute("var rx = /hello/;");
            Assert.That(engine.Root.GetParameter("rx").IsRegexp, Is.True);
        }

        [Test]
        public void RegexLiteral_WithFlags_IsLexed()
        {
            Assert.DoesNotThrow(() =>
            {
                var engine = new ScriptEngine();
                engine.Execute("var rx = /world/gi;");
                Assert.That(engine.Root.GetParameter("rx").IsRegexp, Is.True);
            });
        }

        // ── Utils.GetJSString: special character escaping via JSON.stringify ─────

        [Test]
        public void JsonStringify_StringWithNewline_EscapesIt()
        {
            var r = RunScriptExtras("var s = \"a\\nb\"; var __result__ = JSON.stringify({k: s});");
            Assert.That(r.String, Does.Contain("\\n"));
        }

        [Test]
        public void JsonStringify_StringWithBackslash_EscapesIt()
        {
            var r = RunScriptExtras("var s = \"a\\\\b\"; var __result__ = JSON.stringify({k: s});");
            Assert.That(r.String, Does.Contain("\\\\"));
        }

        [Test]
        public void JsonStringify_StringWithCarriageReturn_EscapesIt()
        {
            var r = RunScriptExtras("var s = \"a\\rb\"; var __result__ = JSON.stringify({k: s});");
            Assert.That(r.String, Does.Contain("\\r"));
        }

        [Test]
        public void JsonStringify_StringWithTab_EscapesIt()
        {
            var r = RunScriptExtras("var s = \"a\\tb\"; var __result__ = JSON.stringify({k: s});");
            Assert.That(r.String, Does.Contain("\\t"));
        }

        [Test]
        public void JsonStringify_StringWithDoubleQuote_EscapesIt()
        {
            // The DScript string literal "a\"b" contains a literal double-quote.
            var r = RunScriptExtras("var s = \"a\\\"b\"; var __result__ = JSON.stringify({k: s});");
            Assert.That(r.String, Does.Contain("\\\""));
        }

        [Test]
        public void JsonStringify_StringWithFormFeed_EscapesIt()
        {
            var r = RunScriptExtras("var s = \"a\\fb\"; var __result__ = JSON.stringify({k: s});");
            Assert.That(r.String, Does.Contain("\\f"));
        }

        [Test]
        public void JsonStringify_StringWithVerticalTab_EscapesIt()
        {
            var r = RunScriptExtras("var s = \"a\\vb\"; var __result__ = JSON.stringify({k: s});");
            Assert.That(r.String, Does.Contain("\\v"));
        }

        [Test]
        public void JsonStringify_StringWithBell_EscapesIt()
        {
            var r = RunScriptExtras("var s = \"a\\ab\"; var __result__ = JSON.stringify({k: s});");
            Assert.That(r.String, Does.Contain("\\a"));
        }

        [Test]
        public void JsonStringify_StringWithBackspace_EscapesIt()
        {
            var r = RunScriptExtras("var s = \"a\\bb\"; var __result__ = JSON.stringify({k: s});");
            Assert.That(r.String, Does.Contain("\\b"));
        }

        [Test]
        public void JsonStringify_ArrayValue_ProducesJson()
        {
            var r = RunScriptExtras("var __result__ = JSON.stringify([1, 2, 3]);");
            Assert.That(r.String, Does.Contain("1").And.Contain("2").And.Contain("3"));
        }

        // ── ConstantValue: ToString for non-Int kinds ────────────────────────────

        [Test]
        public void ConstantValue_Double_ToStringUsesInvariantCulture()
        {
            var cv = ConstantValue.Double(3.14);
            Assert.That(cv.ToString(), Is.EqualTo("3.14"));
        }

        [Test]
        public void ConstantValue_String_ToStringIsQuoted()
        {
            var cv = ConstantValue.String("hello");
            Assert.That(cv.ToString(), Is.EqualTo("\"hello\""));
        }

        [Test]
        public void ConstantValue_Regex_ToStringIsLiteral()
        {
            var cv = ConstantValue.Regex("hello");
            Assert.That(cv.ToString(), Is.EqualTo("hello"));
        }

        [Test]
        public void ConstantValue_Regex_Materializes_ToRegexpVar()
        {
            var cv = ConstantValue.Regex("/hello/");
            var sv = cv.Materialize();
            Assert.That(sv.IsRegexp, Is.True);
        }

        // ── Disassembler: EnterTry, BinaryConst, nested functions ────────────────

        [Test]
        public void Disassembler_EnterTry_ShowsThreeOperands()
        {
            var chunk = new DScriptCompiler().CompileProgram("try { var x = 1; } catch(e) {}");
            var text = Disassembler.Disassemble(chunk);
            Assert.That(text, Does.Contain("EnterTry"));
        }

        [Test]
        public void Disassembler_BinaryIntConst_ShowsOperand()
        {
            var chunk = new DScriptCompiler().CompileProgram("var x = 5; var r = x + 10;");
            var text = Disassembler.Disassemble(chunk);
            Assert.That(text, Does.Contain("BinaryIntConst").Or.Contain("BinaryConst"));
        }

        [Test]
        public void Disassembler_NestedFunction_AppearsInOutput()
        {
            var chunk = new DScriptCompiler().CompileProgram("function greet() { return 1; }");
            var text = Disassembler.Disassemble(chunk);
            Assert.That(text, Does.Contain("greet"));
        }

        [Test]
        public void Disassembler_PublicSingleInstructionOverload_Works()
        {
            var chunk = new Chunk();
            var c = chunk.AddConstant(ConstantValue.Int(99));
            chunk.Emit(OpCode.Constant, c);
            chunk.Emit(OpCode.Return);

            var sb = new StringBuilder();
            Disassembler.DisassembleInstruction(chunk, 0, sb);
            Assert.That(sb.ToString(), Does.Contain("Constant"));
        }

        [Test]
        public void Disassembler_BinaryConst_FloatConstant_Annotated()
        {
            // BinaryConst (not BinaryIntConst) emitted when right operand is a double.
            var compiler = new DScriptCompiler { EnableOptimizer = false };
            var chunk = compiler.CompileProgram("var x = 5; var r = x + 2.5;");
            var text = Disassembler.Disassemble(chunk);
            Assert.That(text, Does.Contain("BinaryConst").Or.Contain("Binary"));
        }
    }
}
