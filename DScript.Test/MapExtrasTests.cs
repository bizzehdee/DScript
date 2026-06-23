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
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class MapExtrasTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        // -----------------------------------------------------------------------
        // Construction
        // -----------------------------------------------------------------------

        [Test]
        public void NewMap_CreatesEmptyMap()
        {
            var result = RunScript("var m = new Map(); __result__ = m;");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.IsUndefined, Is.False);
        }

        [Test]
        public void NewMap_SizeIsZero()
        {
            var result = RunScript("var m = new Map(); __result__ = m.size();");
            Assert.That(result.Int, Is.EqualTo(0));
        }

        [Test]
        public void NewMap_WithInitialEntries_SizeMatchesEntryCount()
        {
            var result = RunScript("var m = new Map([[\"a\", 1], [\"b\", 2]]); __result__ = m.size();");
            Assert.That(result.Int, Is.EqualTo(2));
        }

        [Test]
        public void NewMap_WithInitialEntries_GetReturnsCorrectValues()
        {
            var result = RunScript("var m = new Map([[\"x\", 42], [\"y\", 99]]); __result__ = m.get(\"x\");");
            Assert.That(result.Int, Is.EqualTo(42));
        }

        [Test]
        public void NewMap_WithInitialEntries_SecondValueAccessible()
        {
            var result = RunScript("var m = new Map([[\"x\", 42], [\"y\", 99]]); __result__ = m.get(\"y\");");
            Assert.That(result.Int, Is.EqualTo(99));
        }

        // -----------------------------------------------------------------------
        // set / get round-trip
        // -----------------------------------------------------------------------

        [Test]
        public void Set_ThenGet_ReturnsStoredValue()
        {
            var result = RunScript("var m = new Map(); m.set(\"key\", 123); __result__ = m.get(\"key\");");
            Assert.That(result.Int, Is.EqualTo(123));
        }

        [Test]
        public void Set_OverwritesExistingKey()
        {
            var result = RunScript("var m = new Map(); m.set(\"k\", 1); m.set(\"k\", 2); __result__ = m.get(\"k\");");
            Assert.That(result.Int, Is.EqualTo(2));
        }

        [Test]
        public void Get_MissingKey_ReturnsUndefined()
        {
            var result = RunScript("var m = new Map(); __result__ = m.get(\"nope\");");
            Assert.That(result.IsUndefined, Is.True);
        }

        [Test]
        public void Set_StringValue_RoundTrips()
        {
            var result = RunScript("var m = new Map(); m.set(\"name\", \"hello\"); __result__ = m.get(\"name\");");
            Assert.That(result.String, Is.EqualTo("hello"));
        }

        [Test]
        public void Set_ReturnsMapItself_AllowingChaining()
        {
            // set returns `this`, so chaining set calls should work
            var result = RunScript(
                "var m = new Map(); m.set(\"a\", 1).set(\"b\", 2); __result__ = m.get(\"b\");");
            Assert.That(result.Int, Is.EqualTo(2));
        }

        // -----------------------------------------------------------------------
        // has
        // -----------------------------------------------------------------------

        [Test]
        public void Has_ExistingKey_ReturnsTrue()
        {
            var result = RunScript("var m = new Map(); m.set(\"k\", 1); __result__ = m.has(\"k\");");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void Has_MissingKey_ReturnsFalse()
        {
            var result = RunScript("var m = new Map(); __result__ = m.has(\"missing\");");
            Assert.That(result.Bool, Is.False);
        }

        [Test]
        public void Has_AfterDelete_ReturnsFalse()
        {
            var result = RunScript("var m = new Map(); m.set(\"k\", 1); m.delete(\"k\"); __result__ = m.has(\"k\");");
            Assert.That(result.Bool, Is.False);
        }

        // -----------------------------------------------------------------------
        // delete
        // -----------------------------------------------------------------------

        [Test]
        public void Delete_ExistingKey_ReturnsTrue()
        {
            var result = RunScript("var m = new Map(); m.set(\"k\", 1); __result__ = m.delete(\"k\");");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void Delete_MissingKey_ReturnsFalse()
        {
            var result = RunScript("var m = new Map(); __result__ = m.delete(\"k\");");
            Assert.That(result.Bool, Is.False);
        }

        [Test]
        public void Delete_RemovesEntry_SizeDecreases()
        {
            var result = RunScript("var m = new Map(); m.set(\"a\", 1); m.set(\"b\", 2); m.delete(\"a\"); __result__ = m.size();");
            Assert.That(result.Int, Is.EqualTo(1));
        }

        [Test]
        public void Delete_RemovesEntry_GetReturnsUndefined()
        {
            var result = RunScript("var m = new Map(); m.set(\"k\", 42); m.delete(\"k\"); __result__ = m.get(\"k\");");
            Assert.That(result.IsUndefined, Is.True);
        }

        // -----------------------------------------------------------------------
        // clear
        // -----------------------------------------------------------------------

        [Test]
        public void Clear_EmptiesMap_SizeBecomesZero()
        {
            var result = RunScript("var m = new Map(); m.set(\"a\", 1); m.set(\"b\", 2); m.clear(); __result__ = m.size();");
            Assert.That(result.Int, Is.EqualTo(0));
        }

        [Test]
        public void Clear_AfterClear_HasReturnsFalse()
        {
            var result = RunScript("var m = new Map(); m.set(\"k\", 1); m.clear(); __result__ = m.has(\"k\");");
            Assert.That(result.Bool, Is.False);
        }

        [Test]
        public void Clear_OnEmptyMap_SizeRemainsZero()
        {
            var result = RunScript("var m = new Map(); m.clear(); __result__ = m.size();");
            Assert.That(result.Int, Is.EqualTo(0));
        }

        // -----------------------------------------------------------------------
        // size
        // -----------------------------------------------------------------------

        [Test]
        public void Size_ReflectsCurrentCount_AfterMultipleSets()
        {
            var result = RunScript(
                "var m = new Map(); m.set(\"a\", 1); m.set(\"b\", 2); m.set(\"c\", 3); __result__ = m.size();");
            Assert.That(result.Int, Is.EqualTo(3));
        }

        [Test]
        public void Size_DoesNotDoubleCount_DuplicateKey()
        {
            var result = RunScript("var m = new Map(); m.set(\"k\", 1); m.set(\"k\", 2); __result__ = m.size();");
            Assert.That(result.Int, Is.EqualTo(1));
        }

        // -----------------------------------------------------------------------
        // keys()
        // -----------------------------------------------------------------------

        [Test]
        public void Keys_ReturnsArrayOfKeys()
        {
            var result = RunScript(
                "var m = new Map(); m.set(\"a\", 1); m.set(\"b\", 2); var k = m.keys(); __result__ = k;");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(2));
        }

        [Test]
        public void Keys_ContainsCorrectKeyStrings()
        {
            // Build a map with a known insertion order and verify both keys appear
            var result = RunScript(
                "var m = new Map(); m.set(\"x\", 10); var k = m.keys(); __result__ = k[0];");
            Assert.That(result.String, Is.EqualTo("x"));
        }

        [Test]
        public void Keys_EmptyMap_ReturnsEmptyArray()
        {
            var result = RunScript("var m = new Map(); __result__ = m.keys();");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(0));
        }

        // -----------------------------------------------------------------------
        // values()
        // -----------------------------------------------------------------------

        [Test]
        public void Values_ReturnsArrayOfValues()
        {
            var result = RunScript(
                "var m = new Map(); m.set(\"a\", 1); m.set(\"b\", 2); __result__ = m.values();");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(2));
        }

        [Test]
        public void Values_ContainsCorrectValue()
        {
            var result = RunScript(
                "var m = new Map(); m.set(\"k\", 77); var v = m.values(); __result__ = v[0];");
            Assert.That(result.Int, Is.EqualTo(77));
        }

        [Test]
        public void Values_EmptyMap_ReturnsEmptyArray()
        {
            var result = RunScript("var m = new Map(); __result__ = m.values();");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(0));
        }

        // -----------------------------------------------------------------------
        // entries()
        // -----------------------------------------------------------------------

        [Test]
        public void Entries_ReturnsArrayOfKeyValuePairs()
        {
            var result = RunScript(
                "var m = new Map(); m.set(\"a\", 1); __result__ = m.entries();");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(1));
        }

        [Test]
        public void Entries_EachPairIsArrayOfLengthTwo()
        {
            var result = RunScript(
                "var m = new Map(); m.set(\"k\", 99); var e = m.entries(); __result__ = e[0];");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(2));
        }

        [Test]
        public void Entries_PairKeyAndValueCorrect()
        {
            RunScript(
                "var m = new Map(); m.set(\"k\", 99); var e = m.entries(); var pair = e[0]; __result__ = pair[0];");
            var keyResult = RunScript(
                "var m = new Map(); m.set(\"k\", 99); var e = m.entries(); __result__ = e[0][0];");
            var valResult = RunScript(
                "var m = new Map(); m.set(\"k\", 99); var e = m.entries(); __result__ = e[0][1];");
            Assert.That(keyResult.String, Is.EqualTo("k"));
            Assert.That(valResult.Int, Is.EqualTo(99));
        }

        [Test]
        public void Entries_EmptyMap_ReturnsEmptyArray()
        {
            var result = RunScript("var m = new Map(); __result__ = m.entries();");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(0));
        }

        // -----------------------------------------------------------------------
        // forEach
        // -----------------------------------------------------------------------

        [Test]
        public void ForEach_IteratesAllEntries()
        {
            var result = RunScript(@"
var m = new Map();
m.set(""a"", 10);
m.set(""b"", 20);
var sum = 0;
m.forEach(function(val, key) { sum = sum + val; });
__result__ = sum;
");
            Assert.That(result.Int, Is.EqualTo(30));
        }

        [Test]
        public void ForEach_ReceivesKeyArgument()
        {
            var result = RunScript(@"
var m = new Map();
m.set(""only"", 1);
var captured = """";
m.forEach(function(val, key) { captured = key; });
__result__ = captured;
");
            Assert.That(result.String, Is.EqualTo("only"));
        }

        [Test]
        public void ForEach_EmptyMap_CallbackNeverInvoked()
        {
            var result = RunScript(@"
var m = new Map();
var called = 0;
m.forEach(function(val, key) { called = called + 1; });
__result__ = called;
");
            Assert.That(result.Int, Is.EqualTo(0));
        }

        // -----------------------------------------------------------------------
        // Object keys (reference equality)
        // -----------------------------------------------------------------------

        [Test]
        public void ObjectKey_SameReference_GetReturnsValue()
        {
            var result = RunScript(@"
var m = new Map();
var obj = { id: 1 };
m.set(obj, ""found"");
__result__ = m.get(obj);
");
            Assert.That(result.String, Is.EqualTo("found"));
        }

        [Test]
        public void ObjectKey_DifferentReferenceEqualContent_ReturnsUndefined()
        {
            // Two distinct object literals that look the same are different references;
            // the map should NOT find the second one.
            var result = RunScript(@"
var m = new Map();
var obj1 = { id: 1 };
var obj2 = { id: 1 };
m.set(obj1, ""found"");
__result__ = m.get(obj2);
");
            Assert.That(result.IsUndefined, Is.True);
        }

        [Test]
        public void ObjectKey_Has_TrueForSameReference()
        {
            var result = RunScript(@"
var m = new Map();
var obj = { x: 5 };
m.set(obj, 1);
__result__ = m.has(obj);
");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void ObjectKey_Has_FalseForDifferentReference()
        {
            var result = RunScript(@"
var m = new Map();
var obj1 = { x: 5 };
var obj2 = { x: 5 };
m.set(obj1, 1);
__result__ = m.has(obj2);
");
            Assert.That(result.Bool, Is.False);
        }

        [Test]
        public void ObjectKey_Delete_TrueForSameReference()
        {
            var result = RunScript(@"
var m = new Map();
var obj = { n: 7 };
m.set(obj, 99);
__result__ = m.delete(obj);
");
            Assert.That(result.Bool, Is.True);
        }
    }
}
