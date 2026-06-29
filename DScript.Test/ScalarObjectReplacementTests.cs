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
using DScript.Jit;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    /// <summary>
    /// Tests for Lever 2e scalar object replacement: non-escaping object literals
    /// inside hot loops are replaced with raw-int registers, eliminating the
    /// allocation. Each test verifies correct results under both scalar replacement
    /// and the conservative fallback (via the kill switch).
    /// NonParallelizable: JitRegistry is process-global.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class ScalarObjectReplacementTests
    {
        [SetUp]
        public void Setup()
        {
            JitRegistry.Clear();
            JitRegistry.Register(new ReflectionEmitJitCompiler());
            ReflectionEmitJitCompiler.DisableScalarObjectReplacement = false;
        }

        [TearDown]
        public void Teardown()
        {
            ReflectionEmitJitCompiler.DisableScalarObjectReplacement = false;
            JitRegistry.Clear();
        }

        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__result__");
        }

        // Run the code with scalar replacement (JIT registered in SetUp), then with the
        // kill switch enabled (which forces the conservative tier), and assert both produce
        // the same result.
        private static ScriptVar RunBoth(string code, out ScriptVar withKillSwitch)
        {
            var before = ReflectionEmitJitCompiler.ScalarObjectReplacements;
            var r1 = RunScript(code);
            var didReplace = ReflectionEmitJitCompiler.ScalarObjectReplacements > before;

            ReflectionEmitJitCompiler.DisableScalarObjectReplacement = true;
            withKillSwitch = RunScript(code);
            ReflectionEmitJitCompiler.DisableScalarObjectReplacement = false;

            // Confirm scalar replacement actually fired (or was expected to fire)
            Assert.That(didReplace, Is.True, "Scalar replacement should have engaged for this pattern");
            return r1;
        }

        // ── Happy path ───────────────────────────────────────────────────────────

        [Test]
        public void SinglePropertyObject_ScalarReplaced()
        {
            // Minimal case: 1 property object literal in a loop.
            const string code = @"
function f() {
    var s = 0;
    for (var i = 0; i < 1000; i++) {
        var o = { x: i };
        s = s + o.x;
    }
    return s;
}
var r = 0; var k = 0; while(k < 600){ r = f(); k = k + 1; }
__result__ = r;";
            var r1 = RunBoth(code, out var r2);
            Assert.That(r1.Int, Is.EqualTo(r2.Int));
            Assert.That(r1.Int, Is.EqualTo(499500)); // 0+1+…+999
        }

        [Test]
        public void FourPropertyObject_ScalarReplaced_MatchesInterpreter()
        {
            // Matches the Objects benchmark pattern exactly.
            const string code = @"
function f() {
    var s = 0;
    for (var i = 0; i < 1000; i++) {
        var o = { a: i, b: i+1, c: i+2, d: i+3 };
        s = s + o.a + o.b + o.c + o.d;
    }
    return s;
}
var r = 0; var k = 0; while(k < 600){ r = f(); k = k + 1; }
__result__ = r;";
            var r1 = RunBoth(code, out var r2);
            Assert.That(r1.Int, Is.EqualTo(r2.Int));
            // sum of 4i+6 for i=0..999 = 4*499500 + 6000 = 2004000
            Assert.That(r1.Int, Is.EqualTo(2004000));
        }

        [Test]
        public void ExactIntegerDoubleLoopBound_CorrectResult()
        {
            // 1e3-style loop bound: ConstantKind.Double profiles as Int+Double, so the
            // speculative-int tier is declined (profile check). The function falls to the
            // conservative tier, but the result must still be correct.
            const string code = @"
function f() {
    var s = 0;
    for (var i = 0; i < 1e3; i++) {
        var o = { v: i * 2 };
        s = s + o.v;
    }
    return s;
}
var r = 0; var k = 0; while(k < 600){ r = f(); k = k + 1; }
__result__ = r;";
            var result = RunScript(code);
            Assert.That(result.Int, Is.EqualTo(999000)); // sum of 2*i for i=0..999
        }

        [Test]
        public void BlockScopeObjectLiteral_ScalarReplaced()
        {
            // const o inside a block (EnterBlock/LeaveBlock) — the main target.
            const string code = @"
function f() {
    var s = 0;
    for (let i = 0; i < 1000; i++) {
        const o = { a: i, b: i + 1 };
        s = s + o.a + o.b;
    }
    return s;
}
var r = 0; var k = 0; while(k < 600){ r = f(); k = k + 1; }
__result__ = r;";
            var r1 = RunBoth(code, out var r2);
            Assert.That(r1.Int, Is.EqualTo(r2.Int));
        }

        // ── Kill switch ──────────────────────────────────────────────────────────

        [Test]
        public void KillSwitch_DisablesScalarReplacement()
        {
            const string code = @"
function f() {
    var s = 0;
    for (var i = 0; i < 1000; i++) {
        var o = { x: i, y: i + 1 };
        s = s + o.x + o.y;
    }
    return s;
}
var r = 0; var k = 0; while(k < 600){ r = f(); k = k + 1; }
__result__ = r;";
            ReflectionEmitJitCompiler.DisableScalarObjectReplacement = true;
            try
            {
                var r = RunScript(code);
                Assert.That(r.Int, Is.EqualTo(1000 * 999 / 2 + 1000 * 1001 / 2)); // sum i + sum (i+1)
            }
            finally
            {
                ReflectionEmitJitCompiler.DisableScalarObjectReplacement = false;
            }
        }

        // ── Escape routes — each must force the conservative tier ────────────────
        // In all cases the result must match the interpreter even if scalar replacement
        // is skipped (these patterns must NOT be scalar-replaced).

        [Test]
        public void EscapeViaStoreToOuterScope_NotScalarReplaced()
        {
            // Object assigned to outer variable → escapes. Must use conservative tier.
            const string code = @"
function f() {
    var escaped = null;
    var s = 0;
    for (var i = 0; i < 100; i++) {
        var o = { x: i };
        escaped = o;     // o escapes into outer scope
        s = s + escaped.x;
    }
    return s;
}
var r = f();
__result__ = r;";
            var before = ReflectionEmitJitCompiler.ScalarObjectReplacements;
            var result = RunScript(code);
            Assert.That(ReflectionEmitJitCompiler.ScalarObjectReplacements, Is.EqualTo(before),
                "Object that escapes to outer scope must not be scalar-replaced");
            Assert.That(result.Int, Is.EqualTo(4950)); // 0+1+…+99
        }

        [Test]
        public void EscapeViaReturn_NotScalarReplaced()
        {
            // Object returned → escapes. Not replaced.
            const string code = @"
function makeObj(i) {
    return { x: i, y: i + 1 };
}
var s = 0;
for (var i = 0; i < 100; i++) {
    var o = makeObj(i);
    s = s + o.x + o.y;
}
__result__ = s;";
            var result = RunScript(code);
            Assert.That(result.Int, Is.EqualTo(100 * 99 / 2 + 100 * 101 / 2));
        }

        [Test]
        public void EscapeViaPassToCallee_NotScalarReplaced()
        {
            // Object passed to function → escapes.
            const string code = @"
function sum(o) { return o.a + o.b; }
function f() {
    var s = 0;
    for (var i = 0; i < 100; i++) {
        var o = { a: i, b: i + 1 };
        s = s + sum(o);   // o escapes into sum()
    }
    return s;
}
var r = f();
__result__ = r;";
            var before = ReflectionEmitJitCompiler.ScalarObjectReplacements;
            var result = RunScript(code);
            Assert.That(ReflectionEmitJitCompiler.ScalarObjectReplacements, Is.EqualTo(before),
                "Object passed to a callee must not be scalar-replaced");
            Assert.That(result.Int, Is.EqualTo(100 * 99 / 2 + 100 * 101 / 2));
        }

        [Test]
        public void EscapeViaIdentityComparison_NotScalarReplaced()
        {
            // Reading the object variable without GetProp (here: strict equality) → escape.
            const string code = @"
function f() {
    var count = 0;
    var prev = null;
    for (var i = 0; i < 10; i++) {
        var o = { v: i };
        if (o === prev) count = count + 1;
        prev = o;
    }
    return count;
}
var r = f();
__result__ = r;";
            var before = ReflectionEmitJitCompiler.ScalarObjectReplacements;
            var result = RunScript(code);
            Assert.That(ReflectionEmitJitCompiler.ScalarObjectReplacements, Is.EqualTo(before),
                "Object used in identity compare must not be scalar-replaced");
            Assert.That(result.Int, Is.EqualTo(0));
        }

        [Test]
        public void EscapeViaMixedReadAndNonPropUse_NotScalarReplaced()
        {
            // Variable is used both as property receiver and passed to a function.
            const string code = @"
function id(o) { return o; }
function f() {
    var s = 0;
    for (var i = 0; i < 10; i++) {
        var o = { x: i };
        var copy = id(o);   // escape via callee
        s = s + copy.x;
    }
    return s;
}
var r = f();
__result__ = r;";
            var before = ReflectionEmitJitCompiler.ScalarObjectReplacements;
            RunScript(code);
            Assert.That(ReflectionEmitJitCompiler.ScalarObjectReplacements, Is.EqualTo(before),
                "Object used in non-GetProp context must not be scalar-replaced");
        }

        // ── Correct results under scalar replacement for edge cases ──────────────

        [Test]
        public void NestedArithmeticPropertyValues_CorrectResult()
        {
            // Property values are non-trivial arithmetic expressions.
            const string code = @"
function f() {
    var s = 0;
    for (var i = 0; i < 100; i++) {
        var o = { p: i * i, q: (i + 1) * (i + 1) };
        s = s + o.p + o.q;
    }
    return s;
}
var r = 0; var k = 0; while(k < 600){ r = f(); k = k + 1; }
__result__ = r;";
            var r1 = RunBoth(code, out var r2);
            Assert.That(r1.Int, Is.EqualTo(r2.Int));
        }

        [Test]
        public void SamePropertyReadMultipleTimes_CorrectResult()
        {
            // Same property read more than once within an iteration.
            const string code = @"
function f() {
    var s = 0;
    for (var i = 0; i < 100; i++) {
        var o = { v: i };
        s = s + o.v + o.v;   // read o.v twice
    }
    return s;
}
var r = 0; var k = 0; while(k < 600){ r = f(); k = k + 1; }
__result__ = r;";
            var r1 = RunBoth(code, out var r2);
            Assert.That(r1.Int, Is.EqualTo(r2.Int));
            Assert.That(r1.Int, Is.EqualTo(9900)); // 2 * (0+1+…+99)
        }

        [Test]
        public void ZeroIterationsLoop_ReturnsInitialValue()
        {
            // Loop body never runs, so binary-op profiles stay None — the int-loop tier
            // never engages and scalar replacement can't fire. Result must still be correct.
            const string code = @"
function f() {
    var s = 42;
    for (var i = 0; i < 0; i++) {
        var o = { x: i };
        s = s + o.x;
    }
    return s;
}
var r = f();
__result__ = r;";
            var result = RunScript(code);
            Assert.That(result.Int, Is.EqualTo(42));
        }

        [Test]
        public void DiagnosticCounter_IncreasesOnReplacement()
        {
            const string code = @"
function f() {
    var s = 0;
    for (var i = 0; i < 100; i++) {
        var o = { x: i };
        s = s + o.x;
    }
    return s;
}
var r = 0; var k = 0; while(k < 600){ r = f(); k = k + 1; }
__result__ = r;";
            var before = ReflectionEmitJitCompiler.ScalarObjectReplacements;
            RunScript(code);
            Assert.That(ReflectionEmitJitCompiler.ScalarObjectReplacements,
                Is.GreaterThan(before), "ScalarObjectReplacements counter must increment");
        }
    }
}
