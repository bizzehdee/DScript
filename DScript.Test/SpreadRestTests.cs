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
    /// <summary>Tests for rest parameters (<c>...args</c>) and the spread operator in arrays, objects, and call sites.</summary>
    public class SpreadRestTests
    {
        private static int RunInt(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r").Int;
        }

        // ── rest parameters ───────────────────────────────────────────────────

        [Test]
        public void RestParam_CollectsExtraArgs()
        {
            Assert.That(RunInt("function f(a, ...rest) { return rest.length; } var r = f(1, 2, 3);"), Is.EqualTo(2));
        }

        [Test]
        public void RestParam_EmptyWhenNoExtraArgs()
        {
            Assert.That(RunInt("function f(a, ...rest) { return rest.length; } var r = f(1);"), Is.EqualTo(0));
        }

        [Test]
        public void RestParam_FirstElement()
        {
            Assert.That(RunInt("function f(...args) { return args[0]; } var r = f(42, 99);"), Is.EqualTo(42));
        }

        [Test]
        public void RestParam_ArrowFunction()
        {
            Assert.That(RunInt("var f = (...args) => args.length; var r = f(1, 2, 3, 4);"), Is.EqualTo(4));
        }

        // ── spread at call sites ──────────────────────────────────────────────

        [Test]
        public void SpreadCall_SpreadArrayIntoArgs()
        {
            Assert.That(RunInt("function add(a, b, c) { return a + b + c; } var arr = [1, 2, 3]; var r = add(...arr);"), Is.EqualTo(6));
        }

        [Test]
        public void SpreadCall_MixedArgsAndSpread()
        {
            Assert.That(RunInt("function f(a, b, c) { return a + b + c; } var arr = [2, 3]; var r = f(1, ...arr);"), Is.EqualTo(6));
        }

        // ── spread in array literals ──────────────────────────────────────────

        [Test]
        public void SpreadArray_CombinesTwoArrays_Length()
        {
            Assert.That(RunInt("var a = [1, 2]; var b = [3, 4]; var c = [...a, ...b]; var r = c.length;"), Is.EqualTo(4));
        }

        [Test]
        public void SpreadString_IntoArray_YieldsCharacters()
        {
            // [..."abc"] iterates the string's code points; previously gave 0 elements.
            Assert.That(RunInt("var a = [...\"abc\"]; var r = a.length;"), Is.EqualTo(3));
            Assert.That(RunInt("var a = [...\"abc\"]; var r = (a[0] == 'a' && a[2] == 'c') ? 1 : 0;"), Is.EqualTo(1));
        }

        [Test]
        public void SpreadString_AstralCharIsSingleElement()
        {
            // A surrogate pair (😀 = U+1F600) is one code point → one element, though
            // the string's .length is 2. Raw emoji avoids Extras/source-escape deps.
            Assert.That(RunInt("var a = [...\"\U0001F600\"]; var r = a.length;"), Is.EqualTo(1));
            Assert.That(RunInt("var r = \"\U0001F600\".length;"), Is.EqualTo(2));
        }

        [Test]
        public void SpreadString_IntoCall()
        {
            Assert.That(RunInt("function f() { return arguments.length; } var r = f(...\"hello\");"), Is.EqualTo(5));
        }

        [Test]
        public void SpreadArray_AppendedElement()
        {
            Assert.That(RunInt("var a = [1, 2]; var b = [...a, 3]; var r = b[2];"), Is.EqualTo(3));
        }

        // ── spread in object literals ─────────────────────────────────────────

        [Test]
        public void SpreadObject_CopiesAllProperties()
        {
            Assert.That(RunInt("var a = {x: 1, y: 2}; var b = {...a, z: 3}; var r = b.x + b.y + b.z;"), Is.EqualTo(6));
        }

        [Test]
        public void SpreadObject_LaterExplicitKeyOverridesSpread()
        {
            // { ...o, d: 9 } — the explicit d after the spread must win. Previously
            // InitProp appended a shadowed duplicate so the merged d (0) was read.
            Assert.That(RunInt("var o = {a: 1, d: 0}; var p = {...o, d: 9}; var r = p.d;"), Is.EqualTo(9));
        }

        [Test]
        public void SpreadObject_EarlierExplicitKeyOverriddenBySpread()
        {
            // { d: 9, ...o } — here the spread is last, so the merged value wins.
            Assert.That(RunInt("var o = {a: 1, d: 0}; var q = {d: 9, ...o}; var r = q.d;"), Is.EqualTo(0));
        }

        [Test]
        public void SpreadObject_MultipleExplicitKeysAfterSpreadOverride()
        {
            Assert.That(RunInt("var o = {a: 1, d: 0}; var s = {...o, a: 5, d: 7}; var r = s.a * 10 + s.d;"), Is.EqualTo(57));
        }

        [Test]
        public void SpreadObject_OverriddenKeyHasNoDuplicateAfterReassign()
        {
            // After overwrite, reassigning the property must still update the single
            // live link (not a shadowed duplicate left behind by the spread).
            Assert.That(RunInt("var o = {d: 0}; var p = {...o, d: 9}; p.d = 3; var r = p.d;"), Is.EqualTo(3));
        }

        [Test]
        public void ObjectLiteral_NoSpread_StillBuildsCorrectly()
        {
            // Guard the common (no-spread) InitProp path still works after the
            // compiler change that switches to InitPropOverwrite only post-spread.
            Assert.That(RunInt("var o = {a: 1, b: 2, c: 3, d: 4}; var r = o.a + o.b + o.c + o.d;"), Is.EqualTo(10));
        }

        // ── AppendElem / O(n) spread correctness ─────────────────────────────

        [Test]
        public void AppendElem_StaticElemAfterSpread_CorrectIndex()
        {
            // [...a, 99] — 99 must land at index a.length (AppendElem path)
            Assert.That(RunInt("var a = [1, 2, 3]; var b = [...a, 99]; var r = b[3];"), Is.EqualTo(99));
        }

        [Test]
        public void AppendElem_MultipleStaticElemsAfterSpread_CorrectOrder()
        {
            // [...a, 10, 20] — each static elem appended in order
            Assert.That(RunInt("var a = [1]; var b = [...a, 10, 20]; var r = b[1] * 100 + b[2];"), Is.EqualTo(1020));
        }

        [Test]
        public void AppendElem_TwoSpreadsWithStaticBetween_CorrectLength()
        {
            // [...a, 5, ...b] — spread, static, spread must all chain correctly
            Assert.That(RunInt("var a = [1, 2]; var b = [3, 4]; var c = [...a, 5, ...b]; var r = c.length;"), Is.EqualTo(5));
        }

        [Test]
        public void AppendElem_TwoSpreadsWithStaticBetween_CorrectValues()
        {
            Assert.That(RunInt("var a = [1, 2]; var b = [4, 5]; var c = [...a, 3, ...b]; var r = c[0]+c[1]+c[2]+c[3]+c[4];"), Is.EqualTo(15));
        }

        [Test]
        public void SpreadArray_LargeSpreadCorrectSum()
        {
            // Exercises the O(n) path with n=100 to catch any O(n²) regression
            const string src =
                "var a = []; for (var i = 0; i < 100; i = i + 1) { a[i] = 1; } " +
                "var b = [...a, 1]; " +
                "var r = b.length;";
            Assert.That(RunInt(src), Is.EqualTo(101));
        }
    }
}
