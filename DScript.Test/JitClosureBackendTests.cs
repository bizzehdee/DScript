using NUnit.Framework;
using DScript;
using DScript.Vm;
using DScript.Jit;

namespace DScript.Test
{
    /// <summary>
    /// Exercises the closure-threaded JIT back-end across the value-producing
    /// opcodes it lowers (object/array literals, spread, <c>new</c>, index/property
    /// get/set, unary ops, shifts, ternary control flow). Each function is driven
    /// past the tier threshold so its body is compiled by the closure back-end, and
    /// the result must match the interpreter.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class JitClosureBackendTests
    {
        [TearDown] public void Clear() => JitRegistry.Clear();

        private static string Run(string src, IJitCompiler c)
        {
            if (c != null) JitRegistry.Register(c); else JitRegistry.Clear();
            var chunk = ScriptEngine.Compile(src);
            var engine = new ScriptEngine();
            engine.Run(chunk);
            return engine.Root.GetParameter("__result__").String;
        }

        // Wrap `body` in a function `f(x)` driven past the invocation threshold so the
        // closure back-end compiles it; assert the compiled result matches the interpreter.
        private static void Matches(string body, string arg = "i % 50")
        {
            var src = "function f(x){ " + body + " }\n" +
                      "var r = 0; var i = 0; while (i < 1300) { r = f(" + arg + "); i = i + 1; }\n__result__ = r;";
            var interp = Run(src, null);
            var closure = Run(src, new ClosureThreadedJitCompiler());
            Assert.That(closure, Is.EqualTo(interp));
        }

        [Test] public void ObjectLiteral() => Matches("var o = { a: x, b: x + 1 }; return o.a + o.b;");

        [Test] public void ArrayLiteral() => Matches("var a = [x, x + 1, x + 2]; return a[0] + a[1] + a[2];");

        [Test] public void ObjectSpread() => Matches("var base = { a: 1, b: 2 }; var o = { ...base, b: x, c: x + 1 }; return o.a + o.b + o.c;");

        [Test] public void ArraySpread() => Matches("var src = [1, 2]; var a = [...src, x, x + 1]; return a[0] + a[1] + a[2] + a[3];");

        [Test] public void IndexGetSet() => Matches("var a = [0, 0, 0]; a[0] = x; a[1] = x + 1; a[2] = a[0] + a[1]; return a[2];");

        [Test] public void PropertyGetSet() => Matches("var o = {}; o.k = x; o.k = o.k + 1; return o.k;");

        [Test] public void NewExpression()
            => Matches("function Box(v){ this.v = v; } var o = new Box(x); return o.v + 1;");

        [Test] public void UnaryOps() => Matches("return (-x) + (~x) + (typeof x).length;");

        [Test] public void ShiftOps() => Matches("return (x << 2) + (x >> 1) + (x >>> 1);");

        [Test] public void Ternary() => Matches("return x < 25 ? x * 2 : x - 5;");

        [Test] public void NestedExpression()
            => Matches("var o = { xs: [x, x + 1] }; return o.xs[0] + o.xs[1] + (x % 3);");

        [Test] public void AssignmentAsExpression()
            // `var y = (z = ...)` keeps the assignment result on the stack (SetVar
            // expression form), distinct from a statement-level SetVarPop.
            => Matches("var z = 0; var y = (z = x + 1); return y + z;");

        [Test] public void PropertyAssignmentAsExpression()
            // `o.k = ...` whose result is used (SetProp expression form).
            => Matches("var o = {}; var y = (o.k = x + 2); return y + o.k;");

        [Test] public void NestedBlockScope()
            // A nested block with a `let` exercises EnterBlock/LeaveBlock in the driver.
            => Matches("var s = 0; { let t = x * 2; s = t + 1; } return s;");
    }
}
