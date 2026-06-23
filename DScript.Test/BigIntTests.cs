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
    public class BigIntTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            var compiler = new DScriptCompiler();
            var chunk = compiler.CompileProgram(code);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r");
        }

        private static string RunScriptString(string code)
        {
            var engine = new ScriptEngine();
            var compiler = new DScriptCompiler();
            var chunk = compiler.CompileProgram(code);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r").String;
        }

        // --- Literal syntax ---

        [Test]
        public void BigIntLiteral_Decimal()
        {
            var r = RunScript("var r = 42n;");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(new System.Numerics.BigInteger(42)));
        }

        [Test]
        public void BigIntLiteral_Zero()
        {
            var r = RunScript("var r = 0n;");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(System.Numerics.BigInteger.Zero));
        }

        [Test]
        public void BigIntLiteral_Hex()
        {
            var r = RunScript("var r = 0xFFn;");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(new System.Numerics.BigInteger(255)));
        }

        [Test]
        public void BigIntLiteral_Binary()
        {
            var r = RunScript("var r = 0b1010n;");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(new System.Numerics.BigInteger(10)));
        }

        [Test]
        public void BigIntLiteral_Octal()
        {
            var r = RunScript("var r = 0o77n;");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(new System.Numerics.BigInteger(63)));
        }

        // --- Arithmetic ---

        [Test]
        public void BigInt_Addition()
        {
            var r = RunScript("var r = 10n + 20n;");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(new System.Numerics.BigInteger(30)));
        }

        [Test]
        public void BigInt_Subtraction()
        {
            var r = RunScript("var r = 50n - 8n;");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(new System.Numerics.BigInteger(42)));
        }

        [Test]
        public void BigInt_Multiplication()
        {
            var r = RunScript("var r = 6n * 7n;");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(new System.Numerics.BigInteger(42)));
        }

        [Test]
        public void BigInt_Division()
        {
            var r = RunScript("var r = 100n / 3n;");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(new System.Numerics.BigInteger(33)));
        }

        [Test]
        public void BigInt_Modulo()
        {
            var r = RunScript("var r = 100n % 7n;");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(new System.Numerics.BigInteger(2)));
        }

        [Test]
        public void BigInt_Negate()
        {
            var r = RunScript("var r = -42n;");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(new System.Numerics.BigInteger(-42)));
        }

        // --- Bitwise ---

        [Test]
        public void BigInt_BitwiseAnd()
        {
            var r = RunScript("var r = 0b1100n & 0b1010n;");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(new System.Numerics.BigInteger(0b1000)));
        }

        [Test]
        public void BigInt_BitwiseOr()
        {
            var r = RunScript("var r = 0b1100n | 0b1010n;");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(new System.Numerics.BigInteger(0b1110)));
        }

        [Test]
        public void BigInt_BitwiseXor()
        {
            var r = RunScript("var r = 0b1100n ^ 0b1010n;");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(new System.Numerics.BigInteger(0b0110)));
        }

        [Test]
        public void BigInt_BitwiseNot()
        {
            // ~5n in two's complement BigInteger = -6
            var r = RunScript("var r = ~5n;");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(new System.Numerics.BigInteger(-6)));
        }

        // --- Comparisons ---

        [Test]
        public void BigInt_EqualityTrue()
        {
            var r = RunScript("var r = 42n == 42n;");
            Assert.That(r.Bool, Is.True);
        }

        [Test]
        public void BigInt_EqualityFalse()
        {
            var r = RunScript("var r = 42n == 43n;");
            Assert.That(r.Bool, Is.False);
        }

        [Test]
        public void BigInt_StrictEquality()
        {
            var r = RunScript("var r = 42n === 42n;");
            Assert.That(r.Bool, Is.True);
        }

        [Test]
        public void BigInt_LessThan()
        {
            var r = RunScript("var r = 10n < 20n;");
            Assert.That(r.Bool, Is.True);
        }

        [Test]
        public void BigInt_GreaterThan()
        {
            var r = RunScript("var r = 100n > 99n;");
            Assert.That(r.Bool, Is.True);
        }

        // --- typeof ---

        [Test]
        public void BigInt_Typeof()
        {
            var r = RunScriptString("var r = typeof 42n;");
            Assert.That(r, Is.EqualTo("bigint"));
        }

        // --- String conversion ---

        [Test]
        public void BigInt_StringRepresentation()
        {
            // BigInt.String returns its decimal representation
            var r = RunScript("var r = 42n;");
            Assert.That(r.String, Is.EqualTo("42"));
        }

        // --- BigInt() factory ---

        [Test]
        public void BigInt_FactoryFromNumber()
        {
            var r = RunScript("var r = BigInt(42);");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(new System.Numerics.BigInteger(42)));
        }

        [Test]
        public void BigInt_FactoryFromString()
        {
            var r = RunScript("var r = BigInt('12345');");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(new System.Numerics.BigInteger(12345)));
        }

        [Test]
        public void BigInt_FactoryFromBigInt()
        {
            var r = RunScript("var r = BigInt(42n);");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(new System.Numerics.BigInteger(42)));
        }

        // --- Mixed-type error ---

        [Test]
        public void BigInt_MixedTypeThrows()
        {
            Assert.Throws<ScriptException>(() => RunScript("var r = 1n + 1;"));
        }

        // --- Large values ---

        [Test]
        public void BigInt_LargeValue()
        {
            var r = RunScript("var r = 999999999999999999999n + 1n;");
            Assert.That(r.IsBigInt, Is.True);
            Assert.That(r.BigIntData, Is.EqualTo(System.Numerics.BigInteger.Parse("1000000000000000000000")));
        }
    }
}
