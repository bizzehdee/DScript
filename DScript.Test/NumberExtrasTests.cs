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

using DScript.Extras;
using DScript.Extras.FunctionProviders;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class NumberExtrasTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        // ── Number.isInteger ──────────────────────────────────────────────────

        [TestCase("Number.isInteger(1)", true)]
        [TestCase("Number.isInteger(0)", true)]
        [TestCase("Number.isInteger(-42)", true)]
        [TestCase("Number.isInteger(1.0)", true)]
        [TestCase("Number.isInteger(1.5)", false)]
        [TestCase("Number.isInteger(Number.POSITIVE_INFINITY)", false)]
        [TestCase("Number.isInteger(Number.NEGATIVE_INFINITY)", false)]
        [TestCase("Number.isInteger(Number.NaN)", false)]
        [TestCase("Number.isInteger(\"3\")", false)]
        public void IsInteger_VariousInputs(string expr, bool expected)
        {
            var result = RunScript($"var __result__ = {expr};");
            Assert.That(result.Bool, Is.EqualTo(expected));
        }

        // ── Number.isFinite ───────────────────────────────────────────────────

        [TestCase("Number.isFinite(0)", true)]
        [TestCase("Number.isFinite(42)", true)]
        [TestCase("Number.isFinite(-1.5)", true)]
        [TestCase("Number.isFinite(Number.POSITIVE_INFINITY)", false)]
        [TestCase("Number.isFinite(Number.NEGATIVE_INFINITY)", false)]
        [TestCase("Number.isFinite(Number.NaN)", false)]
        [TestCase("Number.isFinite(\"10\")", false)]
        public void IsFinite_VariousInputs(string expr, bool expected)
        {
            var result = RunScript($"var __result__ = {expr};");
            Assert.That(result.Bool, Is.EqualTo(expected));
        }

        // ── Number.isNaN ──────────────────────────────────────────────────────

        [TestCase("Number.isNaN(Number.NaN)", true)]
        [TestCase("Number.isNaN(0)", false)]
        [TestCase("Number.isNaN(42)", false)]
        [TestCase("Number.isNaN(1.5)", false)]
        [TestCase("Number.isNaN(Number.POSITIVE_INFINITY)", false)]
        [TestCase("Number.isNaN(\"NaN\")", false)]
        public void IsNaN_VariousInputs(string expr, bool expected)
        {
            var result = RunScript($"var __result__ = {expr};");
            Assert.That(result.Bool, Is.EqualTo(expected));
        }

        // ── Number.isSafeInteger ──────────────────────────────────────────────

        [TestCase("Number.isSafeInteger(0)", true)]
        [TestCase("Number.isSafeInteger(42)", true)]
        [TestCase("Number.isSafeInteger(-42)", true)]
        [TestCase("Number.isSafeInteger(Number.MAX_SAFE_INTEGER)", true)]
        [TestCase("Number.isSafeInteger(Number.MIN_SAFE_INTEGER)", true)]
        [TestCase("Number.isSafeInteger(1.5)", false)]
        [TestCase("Number.isSafeInteger(Number.POSITIVE_INFINITY)", false)]
        [TestCase("Number.isSafeInteger(Number.NaN)", false)]
        [TestCase("Number.isSafeInteger(\"5\")", false)]
        public void IsSafeInteger_VariousInputs(string expr, bool expected)
        {
            var result = RunScript($"var __result__ = {expr};");
            Assert.That(result.Bool, Is.EqualTo(expected));
        }

        // ── Constants ─────────────────────────────────────────────────────────

        [Test]
        public void Constant_MaxSafeInteger()
        {
            var result = RunScript("var __result__ = Number.MAX_SAFE_INTEGER;");
            Assert.That(result.Float, Is.EqualTo(9007199254740991.0));
        }

        [Test]
        public void Constant_MinSafeInteger()
        {
            var result = RunScript("var __result__ = Number.MIN_SAFE_INTEGER;");
            Assert.That(result.Float, Is.EqualTo(-9007199254740991.0));
        }

        [Test]
        public void Constant_MaxValue()
        {
            var result = RunScript("var __result__ = Number.MAX_VALUE;");
            Assert.That(result.Float, Is.EqualTo(double.MaxValue));
        }

        [Test]
        public void Constant_MinValue()
        {
            // MIN_VALUE maps to double.Epsilon (smallest positive double)
            var result = RunScript("var __result__ = Number.MIN_VALUE;");
            Assert.That(result.Float, Is.EqualTo(double.Epsilon));
        }

        [Test]
        public void Constant_Epsilon()
        {
            var result = RunScript("var __result__ = Number.EPSILON;");
            Assert.That(result.Float, Is.EqualTo(2.220446049250313e-16).Within(1e-30));
        }

        [Test]
        public void Constant_PositiveInfinity()
        {
            var result = RunScript("var __result__ = Number.POSITIVE_INFINITY;");
            Assert.That(double.IsPositiveInfinity(result.Float), Is.True);
        }

        [Test]
        public void Constant_NegativeInfinity()
        {
            var result = RunScript("var __result__ = Number.NEGATIVE_INFINITY;");
            Assert.That(double.IsNegativeInfinity(result.Float), Is.True);
        }

        [Test]
        public void Constant_NaN()
        {
            var result = RunScript("var __result__ = Number.NaN;");
            Assert.That(double.IsNaN(result.Float), Is.True);
        }

        // ── Instance method helpers ───────────────────────────────────────────
        // Number.toFixed / toString / toExponential are registered as named
        // functions on the Number namespace object with "this" as an implicit
        // parameter set by the VM when a dot-call is made (e.g. n.toFixed(2)).
        // Because the DScript engine dispatches method look-ups through
        // FindInParentClasses only for IsString and IsArray — not for number
        // primitives — n.toFixed(2) raises "Value is not a function" at runtime.
        // The C# implementations are therefore exercised directly here by
        // building a minimal ScriptVar call-frame that mirrors what the VM would
        // pass, and invoking the static method directly.

        // Builds a minimal call-frame ScriptVar suitable for a Number instance method.
        // thisValue  — the number the method is called on ("this")
        // namedArgs  — (paramName, ScriptVar) pairs for positional parameters
        private static ScriptVar MakeCallFrame(double thisValue, params (string name, ScriptVar value)[] namedArgs)
        {
            var frame = ScriptVar.CreateFunction();
            frame.AddChildNoDup("this", ScriptVar.FromDouble(thisValue));
            frame.AddChildNoDup(ScriptVar.ReturnVarName, ScriptVar.CreateUndefined());
            foreach (var (name, value) in namedArgs)
                frame.AddChildNoDup(name, value);
            return frame;
        }

        // ── toFixed ───────────────────────────────────────────────────────────

        [Test]
        public void ToFixed_ZeroDigits_ProducesIntegerString()
        {
            var frame = MakeCallFrame(3.7, ("digits", ScriptVar.FromInt(0)));
            NumberFunctionProvider.NumberToFixedImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("4"));
        }

        [Test]
        public void ToFixed_TwoDigits_ProducesTwoDecimalPlaces()
        {
            var frame = MakeCallFrame(3.14159, ("digits", ScriptVar.FromInt(2)));
            NumberFunctionProvider.NumberToFixedImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("3.14"));
        }

        [Test]
        public void ToFixed_FiveDigits()
        {
            var frame = MakeCallFrame(1.0, ("digits", ScriptVar.FromInt(5)));
            NumberFunctionProvider.NumberToFixedImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("1.00000"));
        }

        [Test]
        public void ToFixed_NoDigitsArgument_DefaultsToZero()
        {
            // Omit "digits" so the parameter is undefined — default is 0.
            var frame = MakeCallFrame(2.9);
            NumberFunctionProvider.NumberToFixedImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("3"));
        }

        [Test]
        public void ToFixed_NegativeNumber()
        {
            var frame = MakeCallFrame(-1.5, ("digits", ScriptVar.FromInt(1)));
            NumberFunctionProvider.NumberToFixedImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("-1.5"));
        }

        // ── toString(radix) ───────────────────────────────────────────────────

        [Test]
        public void ToString_Radix10_ProducesDecimalString()
        {
            var frame = MakeCallFrame(255.0, ("radix", ScriptVar.FromInt(10)));
            NumberFunctionProvider.NumberToStringImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("255"));
        }

        [Test]
        public void ToString_Radix16_ProducesHexString()
        {
            var frame = MakeCallFrame(255.0, ("radix", ScriptVar.FromInt(16)));
            NumberFunctionProvider.NumberToStringImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("ff"));
        }

        [Test]
        public void ToString_Radix2_ProducesBinaryString()
        {
            var frame = MakeCallFrame(10.0, ("radix", ScriptVar.FromInt(2)));
            NumberFunctionProvider.NumberToStringImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("1010"));
        }

        [Test]
        public void ToString_Radix8_ProducesOctalString()
        {
            var frame = MakeCallFrame(8.0, ("radix", ScriptVar.FromInt(8)));
            NumberFunctionProvider.NumberToStringImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("10"));
        }

        [Test]
        public void ToString_NoArgument_DefaultsToBase10()
        {
            // Omit "radix" so the parameter is undefined — default is 10.
            var frame = MakeCallFrame(42.0);
            NumberFunctionProvider.NumberToStringImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("42"));
        }

        [Test]
        public void ToString_InvalidRadix_ReturnsNaN()
        {
            var frame = MakeCallFrame(10.0, ("radix", ScriptVar.FromInt(1)));
            NumberFunctionProvider.NumberToStringImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("NaN"));
        }

        [Test]
        public void ToString_RadixAbove36_ReturnsNaN()
        {
            var frame = MakeCallFrame(10.0, ("radix", ScriptVar.FromInt(37)));
            NumberFunctionProvider.NumberToStringImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("NaN"));
        }

        // ── toExponential ─────────────────────────────────────────────────────

        [Test]
        public void ToExponential_DefaultDigits_SixDecimalPlaces()
        {
            var frame = MakeCallFrame(123456.0);
            NumberFunctionProvider.NumberToExponentialImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("1.234560e+005"));
        }

        [Test]
        public void ToExponential_ZeroDigits()
        {
            var frame = MakeCallFrame(123456.0, ("digits", ScriptVar.FromInt(0)));
            NumberFunctionProvider.NumberToExponentialImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("1e+005"));
        }

        [Test]
        public void ToExponential_TwoDigits()
        {
            var frame = MakeCallFrame(0.00123, ("digits", ScriptVar.FromInt(2)));
            NumberFunctionProvider.NumberToExponentialImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("1.23e-003"));
        }

        [Test]
        public void ToExponential_NegativeNumber()
        {
            var frame = MakeCallFrame(-5000.0, ("digits", ScriptVar.FromInt(1)));
            NumberFunctionProvider.NumberToExponentialImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("-5.0e+003"));
        }
    }
}
