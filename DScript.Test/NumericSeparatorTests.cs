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
    public class NumericSeparatorTests
    {
        private static ScriptVar Run(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r");
        }

        private static void ThrowsLex(string source)
        {
            Assert.Throws<ScriptException>(() =>
            {
                var engine = new ScriptEngine();
                new DScriptCompiler().CompileProgram(source);
            });
        }

        // --- decimal separators ---

        [Test]
        public void DecimalSeparator_ThousandsSeparator()
        {
            var r = Run("var r = 1_000;").Int;
            Assert.That(r, Is.EqualTo(1000));
        }

        [Test]
        public void DecimalSeparator_MillionSeparators()
        {
            var r = Run("var r = 1_000_000;").Int;
            Assert.That(r, Is.EqualTo(1_000_000));
        }

        [Test]
        public void DecimalSeparator_FloatIntegerPart()
        {
            var r = Run("var r = 1_000.5;").Float;
            Assert.That(r, Is.EqualTo(1000.5).Within(0.0001));
        }

        [Test]
        public void DecimalSeparator_FloatFractionalPart()
        {
            var r = Run("var r = 1.000_5;").Float;
            Assert.That(r, Is.EqualTo(1.0005).Within(0.00001));
        }

        [Test]
        public void DecimalSeparator_FloatBothParts()
        {
            var r = Run("var r = 1_000.000_5;").Float;
            Assert.That(r, Is.EqualTo(1000.0005).Within(0.00001));
        }

        [Test]
        public void DecimalSeparator_ExponentForm()
        {
            var r = Run("var r = 1_000e2;").Float;
            Assert.That(r, Is.EqualTo(100000.0).Within(1.0));
        }

        // --- hex separators ---

        [Test]
        public void HexSeparator_Basic()
        {
            var r = Run("var r = 0xFF_FF;").Int;
            Assert.That(r, Is.EqualTo(0xFFFF));
        }

        [Test]
        public void HexSeparator_Byte()
        {
            var r = Run("var r = 0xDE_AD;").Int;
            Assert.That(r, Is.EqualTo(0xDEAD));
        }

        // --- binary literals ---

        [Test]
        public void BinaryLiteral_Basic()
        {
            var r = Run("var r = 0b1010;").Int;
            Assert.That(r, Is.EqualTo(10));
        }

        [Test]
        public void BinaryLiteral_WithSeparator()
        {
            var r = Run("var r = 0b1010_0001;").Int;
            Assert.That(r, Is.EqualTo(0b10100001));
        }

        [Test]
        public void BinaryLiteral_AllOnes()
        {
            var r = Run("var r = 0b1111;").Int;
            Assert.That(r, Is.EqualTo(15));
        }

        // --- octal literals ---

        [Test]
        public void OctalLiteral_Basic()
        {
            var r = Run("var r = 0o77;").Int;
            Assert.That(r, Is.EqualTo(63));
        }

        [Test]
        public void OctalLiteral_WithSeparator()
        {
            var r = Run("var r = 0o7_7;").Int;
            Assert.That(r, Is.EqualTo(63));
        }

        [Test]
        public void OctalLiteral_LargerValue()
        {
            var r = Run("var r = 0o777;").Int;
            Assert.That(r, Is.EqualTo(511));
        }

        // --- invalid positions ---

        [Test]
        public void InvalidSeparator_AfterHexPrefix()
        {
            ThrowsLex("var r = 0x_FF;");
        }

        [Test]
        public void InvalidSeparator_AfterBinaryPrefix()
        {
            ThrowsLex("var r = 0b_101;");
        }

        [Test]
        public void InvalidSeparator_AfterOctalPrefix()
        {
            ThrowsLex("var r = 0o_7;");
        }

        [Test]
        public void InvalidSeparator_AtEnd()
        {
            ThrowsLex("var r = 100_;");
        }

        [Test]
        public void InvalidSeparator_Consecutive()
        {
            ThrowsLex("var r = 1__000;");
        }

        [Test]
        public void InvalidSeparator_AfterDecimalPoint()
        {
            ThrowsLex("var r = 1._5;");
        }

        [Test]
        public void InvalidSeparator_AfterExponentMarker()
        {
            ThrowsLex("var r = 1e_2;");
        }
    }
}
