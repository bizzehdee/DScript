using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;
using Environment = DScript.Vm.Environment;

namespace DScript.Test
{
    // Phase 3: compile and run full programs (statements + variables) on the VM.
    public class CompilerStatementTests
    {
        // Runs a program and returns the global scope so tests can read variables.
        private static ScriptVar RunProgram(string source)
        {
            var chunk = new DScriptCompiler().CompileProgram(source);
            var globals = new ScriptVar(null, ScriptVar.Flags.Object);
            new VirtualMachine().Run(chunk, new Environment(globals, null));
            return globals;
        }

        private static int IntOf(string source, string varName)
        {
            return RunProgram(source).GetParameter(varName).Int;
        }

        [Test]
        public void VarDeclarationAndAssignment()
        {
            Assert.That(IntOf("var x = 5; x = x + 3;", "x"), Is.EqualTo(8));
        }

        [Test]
        public void CompoundAssignment()
        {
            Assert.That(IntOf("var a = 10; a += 5; a *= 2; a -= 3;", "a"), Is.EqualTo(27));
        }

        [Test]
        public void PrefixAndPostfixIncrement()
        {
            Assert.That(IntOf("var p = 5; var r = ++p;", "p"), Is.EqualTo(6));
            Assert.That(IntOf("var p = 5; var r = ++p;", "r"), Is.EqualTo(6));
            Assert.That(IntOf("var q = 5; var s = q++;", "q"), Is.EqualTo(6));
            Assert.That(IntOf("var q = 5; var s = q++;", "s"), Is.EqualTo(5));
        }

        [Test]
        public void IfElse()
        {
            Assert.That(IntOf("var r; if (1 < 2) { r = 10; } else { r = 20; }", "r"), Is.EqualTo(10));
            Assert.That(IntOf("var r; if (1 > 2) { r = 10; } else { r = 20; }", "r"), Is.EqualTo(20));
        }

        [Test]
        public void ForLoopAccumulates()
        {
            Assert.That(IntOf("var sum = 0; for (var i = 1; i <= 5; i = i + 1) { sum = sum + i; }", "sum"), Is.EqualTo(15));
        }

        [Test]
        public void WhileLoop()
        {
            Assert.That(IntOf("var p = 1; var n = 0; while (n < 4) { p = p * 2; n = n + 1; }", "p"), Is.EqualTo(16));
        }

        [Test]
        public void DoWhileRunsAtLeastOnce()
        {
            Assert.That(IntOf("var c = 0; do { c = c + 1; } while (c < 0);", "c"), Is.EqualTo(1));
            Assert.That(IntOf("var c = 0; var k = 0; do { c = c + 1; k = k + 1; } while (k < 3);", "c"), Is.EqualTo(3));
        }

        [Test]
        public void BreakAndContinue()
        {
            Assert.That(IntOf("var cnt = 0; for (var x = 0; x < 10; x = x + 1) { if (x == 5) break; cnt = cnt + 1; }", "cnt"), Is.EqualTo(5));
            Assert.That(IntOf("var e = 0; for (var y = 0; y < 6; y = y + 1) { if (y % 2 == 1) continue; e = e + y; }", "e"), Is.EqualTo(6));
        }

        [Test]
        public void ObjectMemberAccessAndAssignment()
        {
            var g = RunProgram("var obj = { a: 10, b: 20 }; obj.c = 30; obj.a = obj.a + 5;");
            var obj = g.GetParameter("obj");
            Assert.That(obj.GetParameter("a").Int, Is.EqualTo(15));
            Assert.That(obj.GetParameter("b").Int, Is.EqualTo(20));
            Assert.That(obj.GetParameter("c").Int, Is.EqualTo(30));
        }

        [Test]
        public void ArrayIndexAccessAndAssignment()
        {
            var g = RunProgram("var arr = [1, 2, 3]; arr[1] = 99; arr[0] = arr[0] + arr[2];");
            var arr = g.GetParameter("arr");
            Assert.That(arr.GetArrayIndex(0).Int, Is.EqualTo(4));
            Assert.That(arr.GetArrayIndex(1).Int, Is.EqualTo(99));
        }

        [Test]
        public void ForInIteratesObjectValues()
        {
            Assert.That(IntOf("var s = 0; var o = { x: 1, y: 2, z: 3 }; for (var k in o) { s = s + o[k]; }", "s"), Is.EqualTo(6));
        }

        [Test]
        public void Switch()
        {
            Assert.That(IntOf("var s; switch (2) { case 1: s = 10; break; case 2: s = 20; break; default: s = 30; break; }", "s"), Is.EqualTo(20));
            Assert.That(IntOf("var s; switch (9) { case 1: s = 10; break; default: s = 30; break; }", "s"), Is.EqualTo(30));
        }

        [Test]
        public void ConstReassignmentThrows()
        {
            Assert.That(() => RunProgram("const c = 1; c = 2;"), Throws.TypeOf<JITException>());
        }

        [Test]
        public void NestedLoops()
        {
            Assert.That(IntOf("var t = 0; for (var i = 0; i < 3; i = i + 1) { for (var j = 0; j < 3; j = j + 1) { t = t + 1; } }", "t"), Is.EqualTo(9));
        }

        // --- variable-resolution inline-cache correctness -----------------------
        // These exercise scenarios where a per-site resolution cache could serve a
        // stale binding if it were not invalidated when a scope gains a binding.

        [Test]
        public void ReassignmentInLoopIsObservedThroughCache()
        {
            // The same GetVar/SetVar sites run every iteration; the cache must read
            // the variable's current value (it caches the link, not the value).
            Assert.That(IntOf("var s = 0; for (var i = 1; i <= 5; i = i + 1) { s = s + i; }", "s"), Is.EqualTo(15));
        }

        [Test]
        public void RedeclarationLaterInScopeShadowsCachedOuterValue()
        {
            // 'x' is read (resolving to the outer x) before a local 'x' is declared
            // in the function scope. After redeclaration, reads must see the local
            // value, not the cached outer resolution.
            const string src =
                "var x = 1; var first; var second; " +
                "function f() { first = x; var x = 99; second = x; } f();";
            Assert.That(IntOf(src, "first"), Is.EqualTo(1));
            Assert.That(IntOf(src, "second"), Is.EqualTo(99));
        }

        [Test]
        public void RecursionResolvesPerFrameNotFromCache()
        {
            // Each recursive frame has its own 'n'; a per-site cache keyed on the
            // wrong frame would return a sibling frame's value.
            const string src =
                "function fib(n) { if (n < 2) { return n; } return fib(n - 1) + fib(n - 2); } " +
                "var r = fib(10);";
            Assert.That(IntOf(src, "r"), Is.EqualTo(55));
        }

        [Test]
        public void ClosuresCaptureDistinctEnvironments()
        {
            // Two calls to make() must produce counters over independent bindings;
            // a resolution cache keyed only by site (ignoring environment identity)
            // would let them share state.
            const string src =
                "function make() { var c = 0; function inc() { c = c + 1; return c; } return inc; } " +
                "var a = make(); var b = make(); " +
                "a(); a(); var ra = a(); var rb = b();";
            Assert.That(IntOf(src, "ra"), Is.EqualTo(3));
            Assert.That(IntOf(src, "rb"), Is.EqualTo(1));
        }
    }
}
