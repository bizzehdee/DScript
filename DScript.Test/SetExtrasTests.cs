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
    public class SetExtrasTests
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
        public void NewSet_EmptyConstructor_SizeIsZero()
        {
            var result = RunScript("var s = new Set(); __result__ = s.size;");
            Assert.That(result.Int, Is.EqualTo(0));
        }

        [Test]
        public void NewSet_FromArray_SizeEqualsArrayLength()
        {
            var result = RunScript("var s = new Set([1, 2, 3]); __result__ = s.size;");
            Assert.That(result.Int, Is.EqualTo(3));
        }

        [Test]
        public void NewSet_FromArray_ContainsAllValues()
        {
            var result = RunScript(
                "var s = new Set([10, 20, 30]);" +
                "__result__ = s.has(10) && s.has(20) && s.has(30);");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void NewSet_FromArrayWithDuplicates_DeduplicatesOnConstruction()
        {
            var result = RunScript("var s = new Set([1, 1, 2]); __result__ = s.size;");
            Assert.That(result.Int, Is.EqualTo(2));
        }

        // -----------------------------------------------------------------------
        // add / has round-trip
        // -----------------------------------------------------------------------

        [Test]
        public void Add_Integer_HasReturnsTrueForSameValue()
        {
            var result = RunScript("var s = new Set(); s.add(42); __result__ = s.has(42);");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void Add_String_HasReturnsTrueForSameValue()
        {
            var result = RunScript("var s = new Set(); s.add('hello'); __result__ = s.has('hello');");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void Has_ValueNotAdded_ReturnsFalse()
        {
            var result = RunScript("var s = new Set(); s.add(1); __result__ = s.has(99);");
            Assert.That(result.Bool, Is.False);
        }

        // -----------------------------------------------------------------------
        // Duplicate add
        // -----------------------------------------------------------------------

        [Test]
        public void Add_DuplicateValue_DoesNotIncreaseSize()
        {
            var result = RunScript(
                "var s = new Set(); s.add(5); s.add(5); s.add(5); __result__ = s.size;");
            Assert.That(result.Int, Is.EqualTo(1));
        }

        // -----------------------------------------------------------------------
        // delete
        // -----------------------------------------------------------------------

        [Test]
        public void Delete_ExistingValue_ReturnsTrueAndRemovesIt()
        {
            var result = RunScript(
                "var s = new Set(); s.add(7);" +
                "var deleted = s.delete(7);" +
                "__result__ = deleted && !s.has(7);");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void Delete_ExistingValue_DecreasesSize()
        {
            var result = RunScript(
                "var s = new Set(); s.add(1); s.add(2); s.delete(1); __result__ = s.size;");
            Assert.That(result.Int, Is.EqualTo(1));
        }

        [Test]
        public void Delete_AbsentValue_ReturnsFalse()
        {
            var result = RunScript(
                "var s = new Set(); s.add(1); __result__ = s.delete(99);");
            Assert.That(result.Bool, Is.False);
        }

        // -----------------------------------------------------------------------
        // clear
        // -----------------------------------------------------------------------

        [Test]
        public void Clear_PopulatedSet_SizeBecomesZero()
        {
            var result = RunScript(
                "var s = new Set(); s.add(1); s.add(2); s.add(3); s.clear(); __result__ = s.size;");
            Assert.That(result.Int, Is.EqualTo(0));
        }

        [Test]
        public void Clear_PopulatedSet_HasReturnsFalseAfterwards()
        {
            var result = RunScript(
                "var s = new Set(); s.add(1); s.clear(); __result__ = s.has(1);");
            Assert.That(result.Bool, Is.False);
        }

        // -----------------------------------------------------------------------
        // size
        // -----------------------------------------------------------------------

        [Test]
        public void Size_ReflectsCurrentCount()
        {
            var result = RunScript(
                "var s = new Set();" +
                "s.add(1); s.add(2); s.add(3); s.delete(2);" +
                "__result__ = s.size;");
            Assert.That(result.Int, Is.EqualTo(2));
        }

        // -----------------------------------------------------------------------
        // values / keys
        // -----------------------------------------------------------------------

        [Test]
        public void Values_ReturnsArrayWithAllElements()
        {
            var result = RunScript(
                "var s = new Set([10, 20, 30]);" +
                "var v = s.values();" +
                "__result__ = v.length;");
            Assert.That(result.Int, Is.EqualTo(3));
        }

        [Test]
        public void Keys_ReturnsSameAsValues()
        {
            var result = RunScript(
                "var s = new Set([1, 2]);" +
                "var k = s.keys(); var v = s.values();" +
                "__result__ = k.length === v.length;");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void Values_ContainsAddedPrimitives()
        {
            var result = RunScript(
                "var s = new Set(); s.add(5); s.add(6);" +
                "var v = s.values();" +
                "var found = false;" +
                "for (var i = 0; i < v.length; i++) { if (v[i] === 5) found = true; }" +
                "__result__ = found;");
            Assert.That(result.Bool, Is.True);
        }

        // -----------------------------------------------------------------------
        // entries
        // -----------------------------------------------------------------------

        [Test]
        public void Entries_ReturnsArrayOfPairs()
        {
            var result = RunScript(
                "var s = new Set([1, 2]);" +
                "var e = s.entries();" +
                "__result__ = e.length;");
            Assert.That(result.Int, Is.EqualTo(2));
        }

        [Test]
        public void Entries_EachPairHasValueValueStructure()
        {
            var result = RunScript(
                "var s = new Set([42]);" +
                "var e = s.entries();" +
                "__result__ = e[0][0] === e[0][1];");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void Entries_PairFirstElementMatchesValue()
        {
            var result = RunScript(
                "var s = new Set([99]);" +
                "var e = s.entries();" +
                "__result__ = e[0][0];");
            Assert.That(result.Int, Is.EqualTo(99));
        }

        // -----------------------------------------------------------------------
        // forEach
        // -----------------------------------------------------------------------

        [Test]
        public void ForEach_IteratesAllValues()
        {
            var result = RunScript(
                "var s = new Set([1, 2, 3, 4]);" +
                "var sum = 0;" +
                "s.forEach(function(v) { sum += v; });" +
                "__result__ = sum;");
            Assert.That(result.Int, Is.EqualTo(10));
        }

        [Test]
        public void ForEach_EmptySet_CallbackNeverInvoked()
        {
            var result = RunScript(
                "var s = new Set();" +
                "var count = 0;" +
                "s.forEach(function(v) { count++; });" +
                "__result__ = count;");
            Assert.That(result.Int, Is.EqualTo(0));
        }

        // -----------------------------------------------------------------------
        // union
        // -----------------------------------------------------------------------

        [Test]
        public void Union_CombinesDistinctValues()
        {
            var result = RunScript(
                "var a = new Set([1, 2, 3]);" +
                "var b = new Set([3, 4, 5]);" +
                "var u = a.union(b);" +
                "__result__ = u.size;");
            Assert.That(result.Int, Is.EqualTo(5));
        }

        [Test]
        public void Union_ContainsAllValuesFromBothSets()
        {
            var result = RunScript(
                "var a = new Set([1, 2]);" +
                "var b = new Set([3, 4]);" +
                "var u = a.union(b);" +
                "__result__ = u.has(1) && u.has(2) && u.has(3) && u.has(4);");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void Union_WithEmptySet_ReturnsCopyOfOriginal()
        {
            var result = RunScript(
                "var a = new Set([1, 2, 3]);" +
                "var b = new Set();" +
                "var u = a.union(b);" +
                "__result__ = u.size;");
            Assert.That(result.Int, Is.EqualTo(3));
        }

        // -----------------------------------------------------------------------
        // intersection
        // -----------------------------------------------------------------------

        [Test]
        public void Intersection_ReturnsOnlySharedValues()
        {
            var result = RunScript(
                "var a = new Set([1, 2, 3]);" +
                "var b = new Set([2, 3, 4]);" +
                "var i = a.intersection(b);" +
                "__result__ = i.size;");
            Assert.That(result.Int, Is.EqualTo(2));
        }

        [Test]
        public void Intersection_ContainsCorrectValues()
        {
            var result = RunScript(
                "var a = new Set([1, 2, 3]);" +
                "var b = new Set([2, 3, 4]);" +
                "var i = a.intersection(b);" +
                "__result__ = i.has(2) && i.has(3) && !i.has(1) && !i.has(4);");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void Intersection_DisjointSets_ReturnsEmptySet()
        {
            var result = RunScript(
                "var a = new Set([1, 2]);" +
                "var b = new Set([3, 4]);" +
                "var i = a.intersection(b);" +
                "__result__ = i.size;");
            Assert.That(result.Int, Is.EqualTo(0));
        }

        // -----------------------------------------------------------------------
        // difference
        // -----------------------------------------------------------------------

        [Test]
        public void Difference_ReturnsElementsInANotInB()
        {
            var result = RunScript(
                "var a = new Set([1, 2, 3]);" +
                "var b = new Set([2, 3, 4]);" +
                "var d = a.difference(b);" +
                "__result__ = d.size;");
            Assert.That(result.Int, Is.EqualTo(1));
        }

        [Test]
        public void Difference_ContainsCorrectValues()
        {
            var result = RunScript(
                "var a = new Set([1, 2, 3]);" +
                "var b = new Set([2, 3]);" +
                "var d = a.difference(b);" +
                "__result__ = d.has(1) && !d.has(2) && !d.has(3);");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void Difference_EmptyB_ReturnsCopyOfA()
        {
            var result = RunScript(
                "var a = new Set([1, 2, 3]);" +
                "var b = new Set();" +
                "var d = a.difference(b);" +
                "__result__ = d.size;");
            Assert.That(result.Int, Is.EqualTo(3));
        }

        [Test]
        public void Difference_IdenticalSets_ReturnsEmptySet()
        {
            var result = RunScript(
                "var a = new Set([1, 2, 3]);" +
                "var b = new Set([1, 2, 3]);" +
                "var d = a.difference(b);" +
                "__result__ = d.size;");
            Assert.That(result.Int, Is.EqualTo(0));
        }

        // -----------------------------------------------------------------------
        // isSubsetOf
        // -----------------------------------------------------------------------

        [Test]
        public void IsSubsetOf_SubsetReturnsTrue()
        {
            var result = RunScript(
                "var a = new Set([1, 2]);" +
                "var b = new Set([1, 2, 3]);" +
                "__result__ = a.isSubsetOf(b);");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void IsSubsetOf_NotSubsetReturnsFalse()
        {
            var result = RunScript(
                "var a = new Set([1, 2, 4]);" +
                "var b = new Set([1, 2, 3]);" +
                "__result__ = a.isSubsetOf(b);");
            Assert.That(result.Bool, Is.False);
        }

        [Test]
        public void IsSubsetOf_EqualSetsReturnsTrue()
        {
            var result = RunScript(
                "var a = new Set([1, 2, 3]);" +
                "var b = new Set([1, 2, 3]);" +
                "__result__ = a.isSubsetOf(b);");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void IsSubsetOf_EmptySetIsSubsetOfAnything()
        {
            var result = RunScript(
                "var a = new Set();" +
                "var b = new Set([1, 2, 3]);" +
                "__result__ = a.isSubsetOf(b);");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void IsSubsetOf_LargerSetIsNotSubsetOfSmaller()
        {
            var result = RunScript(
                "var a = new Set([1, 2, 3]);" +
                "var b = new Set([1, 2]);" +
                "__result__ = a.isSubsetOf(b);");
            Assert.That(result.Bool, Is.False);
        }

        // -----------------------------------------------------------------------
        // add returns this (chaining)
        // -----------------------------------------------------------------------

        [Test]
        public void Add_ReturnsThisForChaining()
        {
            var result = RunScript(
                "var s = new Set();" +
                "s.add(1).add(2).add(3);" +
                "__result__ = s.size;");
            Assert.That(result.Int, Is.EqualTo(3));
        }

        // -----------------------------------------------------------------------
        // isSupersetOf (ES2025)
        // -----------------------------------------------------------------------

        [Test]
        public void IsSupersetOf_ContainsAllElements_ReturnsTrue()
        {
            var result = RunScript(
                "var a = new Set([1, 2, 3]);" +
                "var b = new Set([1, 2]);" +
                "__result__ = a.isSupersetOf(b);");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void IsSupersetOf_MissingElement_ReturnsFalse()
        {
            var result = RunScript(
                "var a = new Set([1, 2]);" +
                "var b = new Set([1, 2, 3]);" +
                "__result__ = a.isSupersetOf(b);");
            Assert.That(result.Bool, Is.False);
        }

        [Test]
        public void IsSupersetOf_EmptyOther_ReturnsTrue()
        {
            var result = RunScript(
                "var a = new Set([1, 2]);" +
                "var b = new Set();" +
                "__result__ = a.isSupersetOf(b);");
            Assert.That(result.Bool, Is.True);
        }

        // -----------------------------------------------------------------------
        // isDisjointFrom (ES2025)
        // -----------------------------------------------------------------------

        [Test]
        public void IsDisjointFrom_NoOverlap_ReturnsTrue()
        {
            var result = RunScript(
                "var a = new Set([1, 2]);" +
                "var b = new Set([3, 4]);" +
                "__result__ = a.isDisjointFrom(b);");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void IsDisjointFrom_HasOverlap_ReturnsFalse()
        {
            var result = RunScript(
                "var a = new Set([1, 2]);" +
                "var b = new Set([2, 3]);" +
                "__result__ = a.isDisjointFrom(b);");
            Assert.That(result.Bool, Is.False);
        }

        [Test]
        public void IsDisjointFrom_IdenticalSets_ReturnsFalse()
        {
            var result = RunScript(
                "var a = new Set([1, 2]);" +
                "var b = new Set([1, 2]);" +
                "__result__ = a.isDisjointFrom(b);");
            Assert.That(result.Bool, Is.False);
        }

        [Test]
        public void IsDisjointFrom_EmptySets_ReturnsTrue()
        {
            var result = RunScript(
                "var a = new Set();" +
                "var b = new Set();" +
                "__result__ = a.isDisjointFrom(b);");
            Assert.That(result.Bool, Is.True);
        }

        // -----------------------------------------------------------------------
        // symmetricDifference (ES2025)
        // -----------------------------------------------------------------------

        [Test]
        public void SymmetricDifference_ReturnsElementsInEitherButNotBoth()
        {
            var result = RunScript(
                "var a = new Set([1, 2, 3]);" +
                "var b = new Set([2, 3, 4]);" +
                "var s = a.symmetricDifference(b);" +
                "__result__ = s.size;");
            Assert.That(result.Int, Is.EqualTo(2));
        }

        [Test]
        public void SymmetricDifference_IdenticalSets_ReturnsEmptySet()
        {
            var result = RunScript(
                "var a = new Set([1, 2]);" +
                "var b = new Set([1, 2]);" +
                "var s = a.symmetricDifference(b);" +
                "__result__ = s.size;");
            Assert.That(result.Int, Is.EqualTo(0));
        }

        [Test]
        public void SymmetricDifference_NoOverlap_ReturnsUnion()
        {
            var result = RunScript(
                "var a = new Set([1, 2]);" +
                "var b = new Set([3, 4]);" +
                "var s = a.symmetricDifference(b);" +
                "__result__ = s.size;");
            Assert.That(result.Int, Is.EqualTo(4));
        }
    }
}
