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

using System.Numerics;
using DScript.Extras;
using DScript.Extras.FunctionProviders;
using NUnit.Framework;

namespace DScript.Test
{
    // BigInt prototype methods (toString, toString(radix), valueOf) and their
    // dispatch through FindInParentClasses for bare BigInt primitives.
    [TestFixture]
    public class BigIntExtrasTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        // Builds a minimal call-frame mirroring what the VM passes for a BigInt
        // instance method: "this" plus any named positional parameters.
        private static ScriptVar MakeCallFrame(BigInteger thisValue, params (string name, ScriptVar value)[] namedArgs)
        {
            var frame = ScriptVar.CreateFunction();
            frame.AddChildNoDup("this", ScriptVar.CreateBigInt(thisValue));
            frame.AddChildNoDup(ScriptVar.ReturnVarName, ScriptVar.CreateUndefined());
            foreach (var (name, value) in namedArgs)
                frame.AddChildNoDup(name, value);
            return frame;
        }

        // ── toString (direct) ─────────────────────────────────────────────────

        [Test]
        public void ToString_NoRadix_ProducesDecimalString()
        {
            var frame = MakeCallFrame(BigInteger.Parse("123456789012345678901234567890"));
            BigIntFunctionProvider.BigIntToStringImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("123456789012345678901234567890"));
        }

        [Test]
        public void ToString_Radix16_ProducesHexString()
        {
            var frame = MakeCallFrame(new BigInteger(255), ("radix", ScriptVar.FromInt(16)));
            BigIntFunctionProvider.BigIntToStringImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("ff"));
        }

        [Test]
        public void ToString_Radix36_ProducesBase36String()
        {
            var frame = MakeCallFrame(new BigInteger(35), ("radix", ScriptVar.FromInt(36)));
            BigIntFunctionProvider.BigIntToStringImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("z"));
        }

        [Test]
        public void ToString_Zero_ProducesZero()
        {
            var frame = MakeCallFrame(BigInteger.Zero, ("radix", ScriptVar.FromInt(2)));
            BigIntFunctionProvider.BigIntToStringImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("0"));
        }

        [Test]
        public void ToString_Negative_KeepsSign()
        {
            var frame = MakeCallFrame(new BigInteger(-255), ("radix", ScriptVar.FromInt(16)));
            BigIntFunctionProvider.BigIntToStringImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("-ff"));
        }

        [Test]
        public void ToString_InvalidRadix_ReturnsNaN()
        {
            var frame = MakeCallFrame(new BigInteger(10), ("radix", ScriptVar.FromInt(1)));
            BigIntFunctionProvider.BigIntToStringImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("NaN"));
        }

        [Test]
        public void ValueOf_ReturnsBigInt()
        {
            var frame = MakeCallFrame(new BigInteger(42));
            BigIntFunctionProvider.BigIntValueOfImpl(frame, null);
            Assert.That(frame.ReturnVar.IsBigInt, Is.True);
            Assert.That(frame.ReturnVar.BigIntData, Is.EqualTo(new BigInteger(42)));
        }

        // ── end-to-end dispatch (the benchmark path) ──────────────────────────

        [Test]
        public void ToString_DispatchesOnBigIntPrimitive()
        {
            // (15n).toString() previously raised "Value is not a function" because
            // FindInParentClasses had no BigInt branch.
            Assert.That(RunScript("var __result__ = (15n).toString();").String, Is.EqualTo("15"));
        }

        [Test]
        public void ToString_Radix_DispatchesOnBigIntPrimitive()
        {
            Assert.That(RunScript("var __result__ = (255n).toString(16);").String, Is.EqualTo("ff"));
        }

        [Test]
        public void ToString_LengthOfLargePower_Matches()
        {
            // Mirrors the benchmark: repeated *3n+1n, then measure digit count.
            var code = "var x = 1n; for (var i = 0; i < 100; i++) x = x * 3n + 1n;" +
                       "var __result__ = x.toString().length;";
            Assert.That(RunScript(code).Int, Is.EqualTo(48));
        }

        [Test]
        public void ConsoleParsableForm_AppendsN()
        {
            // GetParsableString (used by console.log) must render a BigInt, not
            // "undefined" — NumericMask excludes BigInt.
            var v = ScriptVar.CreateBigInt(new BigInteger(10));
            Assert.That(v.GetParsableString(), Is.EqualTo("10n"));
        }
    }
}
