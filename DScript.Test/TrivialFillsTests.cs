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
using DScript.Extras;
using DScript.Extras.FunctionProviders;
using NUnit.Framework;

namespace DScript.Test
{
    /// <summary>Tests for Phase 1 "Trivial fills" features.</summary>
    [TestFixture]
    public class TrivialFillsTests
    {
        private static ScriptVar Run(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__r__");
        }

        private static string RunStr(string code) => Run(code).String;
        private static int RunInt(string code) => Run(code).Int;
        private static double RunFloat(string code) => Run(code).Float;
        private static bool RunBool(string code) => Run(code).Bool;

        // ── 1a: core globals ─────────────────────────────────────────────────

        [Test]
        public void EncodeURIComponent_EncodesSpaceAndSpecialChars()
        {
            var r = RunStr("var __r__ = encodeURIComponent('hello world');");
            Assert.That(r, Is.EqualTo("hello%20world"));
        }

        [Test]
        public void DecodeURIComponent_DecodesPercentEncoding()
        {
            var r = RunStr("var __r__ = decodeURIComponent('hello%20world');");
            Assert.That(r, Is.EqualTo("hello world"));
        }

        [Test]
        public void EncodeURI_PreservesSlashesAndColons()
        {
            var r = RunStr("var __r__ = encodeURI('http://example.com/path?q=a b');");
            Assert.That(r, Does.Contain("http://example.com/path"));
            Assert.That(r, Does.Contain("%20"));
        }

        [Test]
        public void DecodeURI_DecodesPercentEncoding()
        {
            var r = RunStr("var __r__ = decodeURI('hello%20world');");
            Assert.That(r, Is.EqualTo("hello world"));
        }

        [Test]
        public void Btoa_EncodesStringToBase64()
        {
            var r = RunStr("var __r__ = btoa('hello');");
            Assert.That(r, Is.EqualTo("aGVsbG8="));
        }

        [Test]
        public void Atob_DecodesBase64ToString()
        {
            var r = RunStr("var __r__ = atob('aGVsbG8=');");
            Assert.That(r, Is.EqualTo("hello"));
        }

        [Test]
        public void Btoa_Atob_RoundTrip()
        {
            var r = RunStr("var __r__ = atob(btoa('DScript'));");
            Assert.That(r, Is.EqualTo("DScript"));
        }

        // ── 1b: path module ──────────────────────────────────────────────────

        [Test]
        public void Path_Join_CombinesParts()
        {
            var r = RunStr("var __r__ = path.join('a', 'b', 'c');");
            Assert.That(r, Is.EqualTo("a/b/c"));
        }

        [Test]
        public void Path_Dirname_ReturnsDirectory()
        {
            var r = RunStr("var __r__ = path.dirname('a/b/c.txt');");
            Assert.That(r, Is.EqualTo("a/b"));
        }

        [Test]
        public void Path_Basename_ReturnsFilename()
        {
            var r = RunStr("var __r__ = path.basename('a/b/c.txt');");
            Assert.That(r, Is.EqualTo("c.txt"));
        }

        [Test]
        public void Path_Basename_WithExt_StripsExtension()
        {
            var r = RunStr("var __r__ = path.basename('a/b/c.txt', '.txt');");
            Assert.That(r, Is.EqualTo("c"));
        }

        [Test]
        public void Path_Extname_ReturnsExtension()
        {
            var r = RunStr("var __r__ = path.extname('file.json');");
            Assert.That(r, Is.EqualTo(".json"));
        }

        [Test]
        public void Path_IsAbsolute_ReturnsFalseForRelative()
        {
            var r = RunInt("var __r__ = path.isAbsolute('a/b');");
            Assert.That(r, Is.EqualTo(0));
        }

        [Test]
        public void Path_Normalize_CollapsesDoubleDots()
        {
            var r = RunStr("var __r__ = path.normalize('a/b/../c');");
            Assert.That(r, Is.EqualTo("a/c"));
        }

        [Test]
        public void Path_Sep_IsForwardSlash()
        {
            var r = RunStr("var __r__ = path.sep;");
            Assert.That(r, Is.EqualTo("/"));
        }

        // ── 1c: os module ────────────────────────────────────────────────────

        [Test]
        public void Os_Hostname_ReturnsNonEmptyString()
        {
            var r = RunStr("var __r__ = os.hostname();");
            Assert.That(r, Is.Not.Empty);
        }

        [Test]
        public void Os_Platform_ReturnsKnownValue()
        {
            var r = RunStr("var __r__ = os.platform();");
            Assert.That(r, Is.AnyOf("win32", "darwin", "linux"));
        }

        [Test]
        public void Os_Arch_ReturnsNonEmptyString()
        {
            var r = RunStr("var __r__ = os.arch();");
            Assert.That(r, Is.Not.Empty);
        }

        [Test]
        public void Os_Homedir_ReturnsNonEmptyString()
        {
            var r = RunStr("var __r__ = os.homedir();");
            Assert.That(r, Is.Not.Empty);
        }

        [Test]
        public void Os_Tmpdir_ReturnsNonEmptyString()
        {
            var r = RunStr("var __r__ = os.tmpdir();");
            Assert.That(r, Is.Not.Empty);
        }

        [Test]
        public void Os_Totalmem_ReturnsPositiveNumber()
        {
            var r = RunFloat("var __r__ = os.totalmem();");
            Assert.That(r, Is.GreaterThan(0));
        }

        [Test]
        public void Os_Freemem_ReturnsPositiveNumber()
        {
            var r = RunFloat("var __r__ = os.freemem();");
            Assert.That(r, Is.GreaterThan(0));
        }

        [Test]
        public void Os_Cpus_ReturnsPositiveInt()
        {
            var r = RunInt("var __r__ = os.cpus();");
            Assert.That(r, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void Os_EOL_ReturnsNonEmptyString()
        {
            var r = RunStr("var __r__ = os.EOL;");
            Assert.That(r, Is.Not.Empty);
        }

        // ── 1f: Array ────────────────────────────────────────────────────────

        [Test]
        public void Array_ReduceRight_SumsRightToLeft()
        {
            var r = RunInt("var __r__ = [1,2,3,4].reduceRight(function(acc, v) { return acc + v; }, 0);");
            Assert.That(r, Is.EqualTo(10));
        }

        [Test]
        public void Array_ReduceRight_WithoutInitial_StartsFromEnd()
        {
            var r = RunStr("var __r__ = ['a','b','c'].reduceRight(function(acc, v) { return acc + v; });");
            Assert.That(r, Is.EqualTo("cba"));
        }

        [Test]
        public void Array_ToSorted_ReturnsSortedCopy()
        {
            var r = RunStr("var a = [3,1,2]; var b = a.toSorted(); var __r__ = b.join(',');");
            Assert.That(r, Is.EqualTo("1,2,3"));
        }

        [Test]
        public void Array_ToSorted_DoesNotMutateOriginal()
        {
            var r = RunStr("var a = [3,1,2]; a.toSorted(); var __r__ = a.join(',');");
            Assert.That(r, Is.EqualTo("3,1,2"));
        }

        [Test]
        public void Array_ToReversed_ReturnsCopy()
        {
            var r = RunStr("var a = [1,2,3]; var __r__ = a.toReversed().join(',');");
            Assert.That(r, Is.EqualTo("3,2,1"));
        }

        [Test]
        public void Array_ToReversed_DoesNotMutateOriginal()
        {
            var r = RunStr("var a = [1,2,3]; a.toReversed(); var __r__ = a.join(',');");
            Assert.That(r, Is.EqualTo("1,2,3"));
        }

        [Test]
        public void Array_ToSpliced_RemovesElement()
        {
            var r = RunStr("var __r__ = [1,2,3,4].toSpliced(1, 1).join(',');");
            Assert.That(r, Is.EqualTo("1,3,4"));
        }

        [Test]
        public void Array_ToSpliced_InsertElement()
        {
            var r = RunStr("var __r__ = [1,2,3].toSpliced(1, 0, 99).join(',');");
            Assert.That(r, Is.EqualTo("1,99,2,3"));
        }

        [Test]
        public void Array_With_ReplacesElement()
        {
            var r = RunStr("var __r__ = [1,2,3].with(1, 99).join(',');");
            Assert.That(r, Is.EqualTo("1,99,3"));
        }

        [Test]
        public void Array_With_NegativeIndex()
        {
            var r = RunStr("var __r__ = [1,2,3].with(-1, 99).join(',');");
            Assert.That(r, Is.EqualTo("1,2,99"));
        }

        // ── 1f: Object ───────────────────────────────────────────────────────

        [Test]
        public void Object_HasOwn_ReturnsTrueForOwnProperty()
        {
            var r = RunInt("var o = {a:1}; var __r__ = Object.hasOwn(o, 'a') ? 1 : 0;");
            Assert.That(r, Is.EqualTo(1));
        }

        [Test]
        public void Object_HasOwn_ReturnsFalseForMissingProperty()
        {
            var r = RunInt("var o = {a:1}; var __r__ = Object.hasOwn(o, 'b') ? 1 : 0;");
            Assert.That(r, Is.EqualTo(0));
        }

        [Test]
        public void Object_Is_SameValue_ReturnsTrue()
        {
            var r = RunInt("var __r__ = Object.is(1, 1) ? 1 : 0;");
            Assert.That(r, Is.EqualTo(1));
        }

        [Test]
        public void Object_Is_NaN_ReturnsTrue()
        {
            var r = RunInt("var __r__ = Object.is(NaN, NaN) ? 1 : 0;");
            Assert.That(r, Is.EqualTo(1));
        }

        [Test]
        public void Object_Seal_MarksObject()
        {
            var r = RunInt("var o = {a:1}; Object.seal(o); var __r__ = Object.isSealed(o) ? 1 : 0;");
            Assert.That(r, Is.EqualTo(1));
        }

        [Test]
        public void Object_IsSealed_ReturnsFalseForUnsealedObject()
        {
            var r = RunInt("var o = {a:1}; var __r__ = Object.isSealed(o) ? 1 : 0;");
            Assert.That(r, Is.EqualTo(0));
        }

        [Test]
        public void Object_GroupBy_GroupsElements()
        {
            var r = RunStr("var a = [1,2,3,4]; var g = Object.groupBy(a, function(x) { return x % 2 === 0 ? 'even' : 'odd'; }); var __r__ = g.even.join(',');");
            Assert.That(r, Is.EqualTo("2,4"));
        }

        // ── 1f: Map.groupBy ──────────────────────────────────────────────────

        [Test]
        public void Map_GroupBy_GroupsElements()
        {
            // Map.groupBy returns a ScriptVar with MapObject data.
            // Verify directly that it produced two groups (keys 0 and 1).
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute("var a = [1,2,3,4]; var m = Map.groupBy(a, function(x) { return x % 2; });");
            var m = engine.Root.GetParameter("m");
            var mapObj = m.GetData() as MapObject;
            Assert.That(mapObj, Is.Not.Null);
            Assert.That(mapObj.Data.Count, Is.EqualTo(2)); // 2 groups: 0 (even) and 1 (odd)
        }

        // ── 1f: AggregateError ────────────────────────────────────────────────

        [Test]
        public void AggregateError_HasNameAndMessage()
        {
            var r = RunStr("var e = AggregateError([1,2], 'oops'); var __r__ = e.name + ':' + e.message;");
            Assert.That(r, Is.EqualTo("AggregateError:oops"));
        }

        [Test]
        public void AggregateError_HasErrorsArray()
        {
            var r = RunInt("var e = AggregateError([1,2,3], 'x'); var __r__ = e.errors.length;");
            Assert.That(r, Is.EqualTo(3));
        }

        // ── 1f: Error.cause ───────────────────────────────────────────────────

        [Test]
        public void Error_Cause_IsSetFromOptions()
        {
            var r = RunStr("var inner = Error('inner'); var e = Error('outer', { cause: inner }); var __r__ = e.cause.message;");
            Assert.That(r, Is.EqualTo("inner"));
        }

        [Test]
        public void Error_WithoutCause_NoCauseProperty()
        {
            var r = Run("var e = Error('msg'); var __r__ = e.cause;");
            Assert.That(r.IsUndefined, Is.True);
        }

        // ── 1f: Number.toPrecision ────────────────────────────────────────────
        // Number instance methods cannot be called on primitives in DScript
        // (FindInParentClasses only works for IsString/IsArray, not doubles).
        // Test the implementation directly via a crafted call frame.

        private static ScriptVar MakeNumberCallFrame(double thisValue, params (string name, ScriptVar value)[] args)
        {
            var frame = new ScriptVar(ScriptVar.Flags.Function);
            frame.AddChildNoDup("this", new ScriptVar(thisValue));
            frame.AddChildNoDup(ScriptVar.ReturnVarName, new ScriptVar(ScriptVar.Flags.Undefined));
            foreach (var (name, value) in args)
                frame.AddChildNoDup(name, value);
            return frame;
        }

        [Test]
        public void Number_ToPrecision_FiveDigits()
        {
            var frame = MakeNumberCallFrame(123.456, ("digits", new ScriptVar(5)));
            NumberFunctionProvider.NumberToPrecisionImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("123.46"));
        }

        [Test]
        public void Number_ToPrecision_NoArg_ReturnsDefaultString()
        {
            var frame = MakeNumberCallFrame(1.5);
            NumberFunctionProvider.NumberToPrecisionImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("1.5"));
        }

        [Test]
        public void Number_ToPrecision_TwoDigits()
        {
            var frame = MakeNumberCallFrame(9.876, ("digits", new ScriptVar(2)));
            NumberFunctionProvider.NumberToPrecisionImpl(frame, null);
            Assert.That(frame.ReturnVar.String, Is.EqualTo("9.9"));
        }

        // ── 1f: String.normalize / codePointAt / fromCodePoint ────────────────

        [Test]
        public void String_Normalize_NFC_ReturnsString()
        {
            // Basic ASCII is already normalized — just ensure no exception and same value
            var r = RunStr("var __r__ = 'hello'.normalize('NFC');");
            Assert.That(r, Is.EqualTo("hello"));
        }

        [Test]
        public void String_Normalize_DefaultIsNFC()
        {
            var r = RunStr("var __r__ = 'hello'.normalize();");
            Assert.That(r, Is.EqualTo("hello"));
        }

        [Test]
        public void String_CodePointAt_BasicAscii()
        {
            var r = RunInt("var __r__ = 'A'.codePointAt(0);");
            Assert.That(r, Is.EqualTo(65));
        }

        [Test]
        public void String_CodePointAt_OutOfBounds_Undefined()
        {
            var r = Run("var __r__ = 'A'.codePointAt(99);");
            Assert.That(r.IsUndefined, Is.True);
        }

        [Test]
        public void String_FromCodePoint_BasicAscii()
        {
            var r = RunStr("var __r__ = String.fromCodePoint(65);");
            Assert.That(r, Is.EqualTo("A"));
        }

        // ── hashbang ─────────────────────────────────────────────────────────

        [Test]
        public void Hashbang_IsSkippedAtStartOfScript()
        {
            var r = RunInt("#!/usr/bin/env node\nvar __r__ = 42;");
            Assert.That(r, Is.EqualTo(42));
        }

        [Test]
        public void Hashbang_ScriptWithOnlyHashbangRunsCleanly()
        {
            // a script containing nothing but a hashbang line should not throw
            Assert.DoesNotThrow(() => Run("#!/usr/bin/env node\n"));
        }
    }
}
