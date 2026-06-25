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
    public class ArrayExtrasTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        // ── join ───────────────────────────────────────────────────────────────

        [Test]
        public void Join_NoSeparator_DefaultsToComma()
        {
            // join() with no argument must use "," — not the literal "undefined".
            Assert.That(RunScript("var __result__ = [1, 2, 3].join();").String, Is.EqualTo("1,2,3"));
        }

        [Test]
        public void Join_ExplicitSeparator()
        {
            Assert.That(RunScript("var __result__ = [1, 2, 3].join('-');").String, Is.EqualTo("1-2-3"));
            Assert.That(RunScript("var __result__ = [1, 2, 3].join('');").String, Is.EqualTo("123"));
        }

        [Test]
        public void Join_UndefinedAndNullElements_AreEmpty()
        {
            Assert.That(RunScript("var __result__ = [1, undefined, null, 3].join();").String, Is.EqualTo("1,,,3"));
        }

        // ── Array.from (arrays, strings, array-like objects) ───────────────────

        [Test]
        public void From_ArrayLikeWithLengthAndMapFn()
        {
            // Array.from({length:n}, (_, i) => ...) is the idiomatic range builder.
            var result = RunScript(
                "var a = Array.from({ length: 5 }, function(_, i){ return i * i; });" +
                "var __result__ = a.length * 1000 + a[2] * 10 + a[4];"); // len5, a[2]=4, a[4]=16
            Assert.That(result.Int, Is.EqualTo(5 * 1000 + 4 * 10 + 16));
        }

        [Test]
        public void From_String()
        {
            var result = RunScript("var a = Array.from('abc', function(c, i){ return c + i; }); var __result__ = a[0] + a[2];");
            Assert.That(result.String, Is.EqualTo("a0c2"));
        }

        [Test]
        public void From_Array_ShallowMaps()
        {
            var result = RunScript("var a = Array.from([10, 20, 30], function(x){ return x * 2; }); var __result__ = a[0] + a[2];");
            Assert.That(result.Int, Is.EqualTo(20 + 60));
        }

        [Test]
        public void From_FilterMapReduceChain()
        {
            // The reported array-processing pattern (smaller n).
            var result = RunScript(
                "var __result__ = Array.from({ length: 1000 }, function(_, i){ return i; })" +
                ".filter(function(x){ return x % 3 === 0; })" +
                ".map(function(x){ return x * 2; })" +
                ".reduce(function(s, x){ return s + x; }, 0);");
            Assert.That(result.Int, Is.EqualTo(333666));
        }

        // ── push / length cache (kept valid across appends) ────────────────────

        [Test]
        public void Push_BuildsArrayWithCorrectLengthAndElements()
        {
            var result = RunScript(
                "var a = []; for (var i = 0; i < 100; i = i + 1) a.push(i * 2);" +
                "var __result__ = a.length * 1000000 + a[0] * 1000 + a[99];");
            Assert.That(result.Int, Is.EqualTo(100 * 1000000 + 0 + 198));
        }

        [Test]
        public void Push_ReturnsNewLength()
        {
            var result = RunScript("var a = [1, 2]; var __result__ = a.push(3);");
            Assert.That(result.Int, Is.EqualTo(3));
        }

        [Test]
        public void Push_AfterSparseAssignment_ExtendsBeyondHighestIndex()
        {
            // a[5] makes length 6; push then lands at index 6 (length 7).
            var result = RunScript(
                "var a = [1, 2]; a[5] = 99; a.push(7);" +
                "var __result__ = a.length * 100 + a[6];");
            Assert.That(result.Int, Is.EqualTo(7 * 100 + 7));
        }

        [Test]
        public void Length_StableAcrossInteriorFill()
        {
            // Filling an interior hole must not change length.
            var result = RunScript(
                "var a = []; a[4] = 1; var before = a.length; a[2] = 9;" +
                "var __result__ = before * 100 + a.length;");
            Assert.That(result.Int, Is.EqualTo(5 * 100 + 5));
        }

        // ── nested callbacks: reentrant CallFunction (VM pooling correctness) ───

        [Test]
        public void NestedCallbacks_MapOfFilterReduce()
        {
            // A map callback that itself calls filter + reduce — each callback rents a
            // distinct pooled VM; results must be correct under that nesting.
            var result = RunScript(
                "var data = [[1,2,3],[4,5,6],[7,8,9]];" +
                "var r = data.map(function(row){" +
                "  return row.filter(function(x){ return x % 2 === 1; })" +
                "            .reduce(function(a,b){ return a + b; }, 0); });" +
                "var __result__ = r[0] * 10000 + r[1] * 100 + r[2];"); // 4, 5, 16
            Assert.That(result.Int, Is.EqualTo(40516));
        }

        [Test]
        public void NestedCallbacks_SortInsideMap()
        {
            var result = RunScript(
                "var r = [[3,1,2],[6,4,5]].map(function(a){" +
                "  var c = a.slice(); c.sort(function(x,y){ return x - y; }); return c[0]; });" +
                "var __result__ = r[0] * 10 + r[1];"); // 1, 4
            Assert.That(result.Int, Is.EqualTo(14));
        }

        // ── sort: reference identity (elements are reordered, not copied) ───────

        [Test]
        public void Sort_PreservesElementReferenceIdentity()
        {
            // After sorting, the array must hold the SAME object references (reordered),
            // not deep copies — `sorted[i] === original` must hold.
            var result = RunScript(
                "var o = { x: 1 }; var a = [o, { x: 0 }];" +
                "a.sort(function(p, q){ return p.x - q.x; });" +
                "var __result__ = (a[1] === o);");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void Sort_MutationThroughSortedElementAffectsOriginal()
        {
            var result = RunScript(
                "var o = { x: 5 }; var a = [o, { x: 1 }];" +
                "a.sort(function(p, q){ return p.x - q.x; });" +
                "a[1].x = 99;" +              // a[1] is o
                "var __result__ = o.x;");
            Assert.That(result.Int, Is.EqualTo(99));
        }

        [Test]
        public void Sort_OrdersByComparator()
        {
            var result = RunScript(
                "var a = [3, 1, 2]; a.sort(function(x, y){ return x - y; });" +
                "var __result__ = a[0] * 100 + a[1] * 10 + a[2];");
            Assert.That(result.Int, Is.EqualTo(123));
        }

        [Test]
        public void Sort_FractionalComparatorResult_OrdersCorrectly()
        {
            // (x,y)=>x-y on doubles returns a fractional value; truncating it to int
            // made every comparison 0, leaving the array unsorted. The sign must be used.
            var result = RunScript(
                "var a = [0.3, 0.1, 0.2]; a.sort(function(x, y){ return x - y; });" +
                "var __result__ = a[0] + \",\" + a[1] + \",\" + a[2];");
            Assert.That(result.String, Is.EqualTo("0.1,0.2,0.3"));
        }

        [Test]
        public void Sort_FractionalComparatorDescending()
        {
            var result = RunScript(
                "var a = [0.1, 0.3, 0.2]; a.sort(function(x, y){ return y - x; });" +
                "var __result__ = a[0] + \",\" + a[1] + \",\" + a[2];");
            Assert.That(result.String, Is.EqualTo("0.3,0.2,0.1"));
        }

        [Test]
        public void ToSorted_FractionalComparatorResult_OrdersCorrectly()
        {
            var result = RunScript(
                "var a = [0.3, 0.1, 0.2]; var b = a.toSorted(function(x, y){ return x - y; });" +
                "var __result__ = b[0] + \",\" + b[1] + \",\" + b[2];");
            Assert.That(result.String, Is.EqualTo("0.1,0.2,0.3"));
        }

        // ── find ──────────────────────────────────────────────────────────────

        [Test]
        public void Find_ReturnsFirstMatchingElement()
        {
            var result = RunScript("var a = [1, 2, 3, 4]; var __result__ = a.find(function(x) { return x > 2; });");
            Assert.That(result.Int, Is.EqualTo(3));
        }

        [Test]
        public void Find_ReturnsUndefinedWhenNoMatch()
        {
            var result = RunScript("var a = [1, 2, 3]; var __result__ = a.find(function(x) { return x > 10; });");
            Assert.That(result.IsUndefined, Is.True);
        }

        [Test]
        public void Find_EmptyArray_ReturnsUndefined()
        {
            var result = RunScript("var a = []; var __result__ = a.find(function(x) { return x > 0; });");
            Assert.That(result.IsUndefined, Is.True);
        }

        // ── findIndex ─────────────────────────────────────────────────────────

        [Test]
        public void FindIndex_ReturnsIndexOfFirstMatch()
        {
            var result = RunScript("var a = [5, 12, 8, 130]; var __result__ = a.findIndex(function(x) { return x > 10; });");
            Assert.That(result.Int, Is.EqualTo(1));
        }

        [Test]
        public void FindIndex_ReturnsMinusOneWhenNoMatch()
        {
            var result = RunScript("var a = [1, 2, 3]; var __result__ = a.findIndex(function(x) { return x > 100; });");
            Assert.That(result.Int, Is.EqualTo(-1));
        }

        [Test]
        public void FindIndex_EmptyArray_ReturnsMinusOne()
        {
            var result = RunScript("var a = []; var __result__ = a.findIndex(function(x) { return true; });");
            Assert.That(result.Int, Is.EqualTo(-1));
        }

        // ── findLast ──────────────────────────────────────────────────────────

        [Test]
        public void FindLast_ReturnsLastMatchingElement()
        {
            var result = RunScript("var a = [1, 4, 2, 5, 3]; var __result__ = a.findLast(function(x) { return x > 3; });");
            Assert.That(result.Int, Is.EqualTo(5));
        }

        [Test]
        public void FindLast_ReturnsUndefinedWhenNoMatch()
        {
            var result = RunScript("var a = [1, 2, 3]; var __result__ = a.findLast(function(x) { return x > 10; });");
            Assert.That(result.IsUndefined, Is.True);
        }

        [Test]
        public void FindLast_EmptyArray_ReturnsUndefined()
        {
            var result = RunScript("var a = []; var __result__ = a.findLast(function(x) { return true; });");
            Assert.That(result.IsUndefined, Is.True);
        }

        // ── findLastIndex ─────────────────────────────────────────────────────

        [Test]
        public void FindLastIndex_ReturnsIndexOfLastMatch()
        {
            var result = RunScript("var a = [5, 12, 8, 130, 44]; var __result__ = a.findLastIndex(function(x) { return x > 10; });");
            Assert.That(result.Int, Is.EqualTo(4));
        }

        [Test]
        public void FindLastIndex_ReturnsMinusOneWhenNoMatch()
        {
            var result = RunScript("var a = [1, 2, 3]; var __result__ = a.findLastIndex(function(x) { return x > 100; });");
            Assert.That(result.Int, Is.EqualTo(-1));
        }

        [Test]
        public void FindLastIndex_EmptyArray_ReturnsMinusOne()
        {
            var result = RunScript("var a = []; var __result__ = a.findLastIndex(function(x) { return true; });");
            Assert.That(result.Int, Is.EqualTo(-1));
        }

        // ── some ──────────────────────────────────────────────────────────────

        [Test]
        public void Some_ReturnsTrueWhenAtLeastOneElementMatches()
        {
            var result = RunScript("var a = [1, 2, 3, 4]; var __result__ = a.some(function(x) { return x > 3; });");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void Some_ReturnsFalseWhenNoElementMatches()
        {
            var result = RunScript("var a = [1, 2, 3]; var __result__ = a.some(function(x) { return x > 10; });");
            Assert.That(result.Bool, Is.False);
        }

        [Test]
        public void Some_EmptyArray_ReturnsFalse()
        {
            var result = RunScript("var a = []; var __result__ = a.some(function(x) { return true; });");
            Assert.That(result.Bool, Is.False);
        }

        // ── every ─────────────────────────────────────────────────────────────

        [Test]
        public void Every_ReturnsTrueWhenAllElementsMatch()
        {
            var result = RunScript("var a = [2, 4, 6]; var __result__ = a.every(function(x) { return x % 2 == 0; });");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void Every_ReturnsFalseWhenAnyElementFails()
        {
            var result = RunScript("var a = [2, 3, 6]; var __result__ = a.every(function(x) { return x % 2 == 0; });");
            Assert.That(result.Bool, Is.False);
        }

        [Test]
        public void Every_EmptyArray_ReturnsTrue()
        {
            var result = RunScript("var a = []; var __result__ = a.every(function(x) { return false; });");
            Assert.That(result.Bool, Is.True);
        }

        // ── includes ──────────────────────────────────────────────────────────

        [Test]
        public void Includes_ReturnsTrueWhenValuePresent()
        {
            var result = RunScript("var a = [1, 2, 3]; var __result__ = a.includes(2);");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void Includes_ReturnsFalseWhenValueAbsent()
        {
            var result = RunScript("var a = [1, 2, 3]; var __result__ = a.includes(99);");
            Assert.That(result.Bool, Is.False);
        }

        [Test]
        public void Includes_EmptyArray_ReturnsFalse()
        {
            var result = RunScript("var a = []; var __result__ = a.includes(1);");
            Assert.That(result.Bool, Is.False);
        }

        // ── flat ──────────────────────────────────────────────────────────────

        [Test]
        public void Flat_DefaultDepthFlattensOneLevel()
        {
            var result = RunScript("var a = [1, [2, 3], [4]]; var __result__ = a.flat();");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(4));
            Assert.That(result.GetArrayIndex(0).Int, Is.EqualTo(1));
            Assert.That(result.GetArrayIndex(1).Int, Is.EqualTo(2));
            Assert.That(result.GetArrayIndex(2).Int, Is.EqualTo(3));
            Assert.That(result.GetArrayIndex(3).Int, Is.EqualTo(4));
        }

        [Test]
        public void Flat_Depth2FlattensNestedArrays()
        {
            var result = RunScript("var a = [1, [2, [3, 4]]]; var __result__ = a.flat(2);");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(4));
            Assert.That(result.GetArrayIndex(2).Int, Is.EqualTo(3));
            Assert.That(result.GetArrayIndex(3).Int, Is.EqualTo(4));
        }

        [Test]
        public void Flat_EmptyArray_ReturnsEmptyArray()
        {
            var result = RunScript("var a = []; var __result__ = a.flat();");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(0));
        }

        // ── flatMap ───────────────────────────────────────────────────────────

        [Test]
        public void FlatMap_MapsAndFlattensOneLevel()
        {
            var result = RunScript("var a = [1, 2, 3]; var __result__ = a.flatMap(function(x) { return [x, x * 2]; });");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(6));
            Assert.That(result.GetArrayIndex(0).Int, Is.EqualTo(1));
            Assert.That(result.GetArrayIndex(1).Int, Is.EqualTo(2));
            Assert.That(result.GetArrayIndex(2).Int, Is.EqualTo(2));
            Assert.That(result.GetArrayIndex(3).Int, Is.EqualTo(4));
        }

        [Test]
        public void FlatMap_EmptyArray_ReturnsEmptyArray()
        {
            var result = RunScript("var a = []; var __result__ = a.flatMap(function(x) { return [x]; });");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(0));
        }

        // ── fill ──────────────────────────────────────────────────────────────

        [Test]
        public void Fill_FillsEntireArrayWithValue()
        {
            var result = RunScript("var a = [1, 2, 3]; a.fill(7); var __result__ = a;");
            Assert.That(result.GetArrayIndex(0).Int, Is.EqualTo(7));
            Assert.That(result.GetArrayIndex(1).Int, Is.EqualTo(7));
            Assert.That(result.GetArrayIndex(2).Int, Is.EqualTo(7));
        }

        [Test]
        public void Fill_WithStartAndEnd_FillsSubrange()
        {
            var result = RunScript("var a = [1, 2, 3, 4, 5]; a.fill(0, 1, 3); var __result__ = a;");
            Assert.That(result.GetArrayIndex(0).Int, Is.EqualTo(1));
            Assert.That(result.GetArrayIndex(1).Int, Is.EqualTo(0));
            Assert.That(result.GetArrayIndex(2).Int, Is.EqualTo(0));
            Assert.That(result.GetArrayIndex(3).Int, Is.EqualTo(4));
            Assert.That(result.GetArrayIndex(4).Int, Is.EqualTo(5));
        }

        [Test]
        public void Fill_EmptyArray_ReturnsEmptyArray()
        {
            var result = RunScript("var a = []; a.fill(9); var __result__ = a;");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(0));
        }

        // ── concat ────────────────────────────────────────────────────────────

        [Test]
        public void Concat_CombinesTwoArrays()
        {
            var result = RunScript("var a = [1, 2]; var b = [3, 4]; var __result__ = a.concat(b);");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(4));
            Assert.That(result.GetArrayIndex(0).Int, Is.EqualTo(1));
            Assert.That(result.GetArrayIndex(3).Int, Is.EqualTo(4));
        }

        [Test]
        public void Concat_WithEmptyArray_ReturnsCopyOfOriginal()
        {
            var result = RunScript("var a = [1, 2, 3]; var b = []; var __result__ = a.concat(b);");
            Assert.That(result.GetArrayLength(), Is.EqualTo(3));
        }

        [Test]
        public void Concat_EmptyWithNonEmpty_ReturnsCopyOfOther()
        {
            var result = RunScript("var a = []; var b = [10, 20]; var __result__ = a.concat(b);");
            Assert.That(result.GetArrayLength(), Is.EqualTo(2));
            Assert.That(result.GetArrayIndex(0).Int, Is.EqualTo(10));
        }

        // ── splice ────────────────────────────────────────────────────────────

        [Test]
        public void Splice_RemovesElements_ReturnsRemoved()
        {
            var result = RunScript("var a = [1, 2, 3, 4, 5]; var __result__ = a.splice(1, 2);");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(2));
            Assert.That(result.GetArrayIndex(0).Int, Is.EqualTo(2));
            Assert.That(result.GetArrayIndex(1).Int, Is.EqualTo(3));
        }

        [Test]
        public void Splice_RemovesElements_MutatesOriginalArray()
        {
            RunScript("var __result__ = [1, 2, 3, 4, 5]; __result__.splice(1, 2);");
            var result = RunScript("var a = [1, 2, 3, 4, 5]; a.splice(1, 2); var __result__ = a;");
            Assert.That(result.GetArrayLength(), Is.EqualTo(3));
            Assert.That(result.GetArrayIndex(0).Int, Is.EqualTo(1));
            Assert.That(result.GetArrayIndex(1).Int, Is.EqualTo(4));
            Assert.That(result.GetArrayIndex(2).Int, Is.EqualTo(5));
        }

        [Test]
        public void Splice_InsertsSingleItem()
        {
            var result = RunScript("var a = [1, 2, 4, 5]; a.splice(2, 0, 3); var __result__ = a;");
            Assert.That(result.GetArrayLength(), Is.EqualTo(5));
            Assert.That(result.GetArrayIndex(2).Int, Is.EqualTo(3));
            Assert.That(result.GetArrayIndex(3).Int, Is.EqualTo(4));
        }

        [Test]
        public void Splice_EmptyArray_ReturnsEmptyRemovedArray()
        {
            var result = RunScript("var a = []; var __result__ = a.splice(0, 1);");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(0));
        }

        // ── at ────────────────────────────────────────────────────────────────

        [Test]
        public void At_PositiveIndex_ReturnsElement()
        {
            var result = RunScript("var a = [10, 20, 30]; var __result__ = a.at(1);");
            Assert.That(result.Int, Is.EqualTo(20));
        }

        [Test]
        public void At_NegativeIndex_ReturnsFromEnd()
        {
            var result = RunScript("var a = [10, 20, 30]; var __result__ = a.at(-1);");
            Assert.That(result.Int, Is.EqualTo(30));
        }

        [Test]
        public void At_NegativeIndexBeyondStart_ReturnsUndefined()
        {
            var result = RunScript("var a = [10, 20, 30]; var __result__ = a.at(-10);");
            Assert.That(result.IsUndefined, Is.True);
        }

        [Test]
        public void At_IndexOutOfBounds_ReturnsUndefined()
        {
            var result = RunScript("var a = [1, 2, 3]; var __result__ = a.at(99);");
            Assert.That(result.IsUndefined, Is.True);
        }

        // ── entries ───────────────────────────────────────────────────────────

        [Test]
        public void Entries_ReturnsArrayOfIndexValuePairs()
        {
            var result = RunScript("var a = ['x', 'y', 'z']; var __result__ = a.entries();");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(3));

            var first = result.GetArrayIndex(0);
            Assert.That(first.IsArray, Is.True);
            Assert.That(first.GetArrayIndex(0).Int, Is.EqualTo(0));
            Assert.That(first.GetArrayIndex(1).String, Is.EqualTo("x"));

            var second = result.GetArrayIndex(1);
            Assert.That(second.GetArrayIndex(0).Int, Is.EqualTo(1));
            Assert.That(second.GetArrayIndex(1).String, Is.EqualTo("y"));
        }

        [Test]
        public void Entries_EmptyArray_ReturnsEmptyArray()
        {
            var result = RunScript("var a = []; var __result__ = a.entries();");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(0));
        }

        // ── keys ──────────────────────────────────────────────────────────────

        [Test]
        public void Keys_ReturnsArrayOfIndices()
        {
            var result = RunScript("var a = ['a', 'b', 'c']; var __result__ = a.keys();");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(3));
            Assert.That(result.GetArrayIndex(0).Int, Is.EqualTo(0));
            Assert.That(result.GetArrayIndex(1).Int, Is.EqualTo(1));
            Assert.That(result.GetArrayIndex(2).Int, Is.EqualTo(2));
        }

        [Test]
        public void Keys_EmptyArray_ReturnsEmptyArray()
        {
            var result = RunScript("var a = []; var __result__ = a.keys();");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(0));
        }

        // ── values ────────────────────────────────────────────────────────────

        [Test]
        public void Values_ReturnsArrayOfValues()
        {
            var result = RunScript("var a = [10, 20, 30]; var __result__ = a.values();");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(3));
            Assert.That(result.GetArrayIndex(0).Int, Is.EqualTo(10));
            Assert.That(result.GetArrayIndex(1).Int, Is.EqualTo(20));
            Assert.That(result.GetArrayIndex(2).Int, Is.EqualTo(30));
        }

        [Test]
        public void Values_EmptyArray_ReturnsEmptyArray()
        {
            var result = RunScript("var a = []; var __result__ = a.values();");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(0));
        }

        // ── Array.isArray ─────────────────────────────────────────────────────

        [Test]
        public void IsArray_ReturnsTrueForArray()
        {
            var result = RunScript("var __result__ = Array.isArray([1, 2, 3]);");
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void IsArray_ReturnsFalseForNonArray()
        {
            var result = RunScript("var __result__ = Array.isArray(42);");
            Assert.That(result.Bool, Is.False);
        }

        [Test]
        public void IsArray_ReturnsTrueForEmptyArray()
        {
            var result = RunScript("var __result__ = Array.isArray([]);");
            Assert.That(result.Bool, Is.True);
        }

        // ── Array.from ────────────────────────────────────────────────────────

        [Test]
        public void From_CopiesAnArray()
        {
            var result = RunScript("var a = [1, 2, 3]; var __result__ = Array.from(a);");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(3));
            Assert.That(result.GetArrayIndex(0).Int, Is.EqualTo(1));
        }

        [Test]
        public void From_WithMapFunction_TransformsElements()
        {
            var result = RunScript("var a = [1, 2, 3]; var __result__ = Array.from(a, function(x) { return x * 10; });");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(3));
            Assert.That(result.GetArrayIndex(0).Int, Is.EqualTo(10));
            Assert.That(result.GetArrayIndex(1).Int, Is.EqualTo(20));
            Assert.That(result.GetArrayIndex(2).Int, Is.EqualTo(30));
        }

        [Test]
        public void From_EmptyArray_ReturnsEmptyArray()
        {
            var result = RunScript("var __result__ = Array.from([]);");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(0));
        }

        // ── Array.of ──────────────────────────────────────────────────────────

        [Test]
        public void Of_ReturnsSingleElementArray()
        {
            var result = RunScript("var __result__ = Array.of(42);");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(1));
            Assert.That(result.GetArrayIndex(0).Int, Is.EqualTo(42));
        }

        [Test]
        public void Of_WithUndefined_ReturnsEmptyArray()
        {
            var result = RunScript("var __result__ = Array.of(undefined);");
            Assert.That(result.IsArray, Is.True);
            Assert.That(result.GetArrayLength(), Is.EqualTo(0));
        }
    }
}
