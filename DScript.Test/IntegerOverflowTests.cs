using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;
using Environment = DScript.Vm.Environment;

namespace DScript.Test
{
    // JS numbers are doubles, so integer arithmetic that exceeds 32 bits must promote
    // to a double rather than wrapping. Covers runtime arithmetic (IntBinary) and
    // compile-time constant folding (MathsOp).
    public class IntegerOverflowTests
    {
        private static double Eval(string expr)
            => new VirtualMachine().Run(new DScriptCompiler().CompileExpression(expr)).Float;

        private static double RunProgram(string code, string varName)
        {
            var chunk = new DScriptCompiler().CompileProgram(code);
            var globals = ScriptVar.CreateObject();
            new VirtualMachine().Run(chunk, new Environment(globals, null));
            return globals.GetParameter(varName).Float;
        }

        [Test]
        public void ConstantMultiply_Overflow_PromotesToDouble()
            => Assert.That(Eval("1000000 * 1000000"), Is.EqualTo(1_000_000_000_000.0));

        [Test]
        public void ConstantAdd_Overflow_PromotesToDouble()
            => Assert.That(Eval("2000000000 + 2000000000"), Is.EqualTo(4_000_000_000.0));

        [Test]
        public void ConstantSubtract_Overflow_PromotesToDouble()
            => Assert.That(Eval("-2000000000 - 2000000000"), Is.EqualTo(-4_000_000_000.0));

        [Test]
        public void RuntimeSumLoop_DoesNotWrap()
        {
            // sum 0..999999 = 499999500000 (exceeds int32)
            var r = RunProgram("var s = 0; for (var i = 0; i < 1000000; i = i + 1) s = s + i; result = s;", "result");
            Assert.That(r, Is.EqualTo(499999500000.0));
        }

        [Test]
        public void RuntimeMultiply_DoesNotWrap()
        {
            var r = RunProgram("var a = 1000000; var b = 1000000; result = a * b;", "result");
            Assert.That(r, Is.EqualTo(1_000_000_000_000.0));
        }

        [Test]
        public void NonOverflowing_StaysInteger()
        {
            // Values within int range stay integers (no spurious promotion).
            var chunk = new DScriptCompiler().CompileExpression("2 + 3 * 4");
            var v = new VirtualMachine().Run(chunk);
            Assert.That(v.IsInt, Is.True);
            Assert.That(v.Int, Is.EqualTo(14));
        }

        [Test]
        public void BitwiseOps_StayInt32()
        {
            // Bitwise operators are defined to produce 32-bit results.
            Assert.That(Eval("1073741824 | 0"), Is.EqualTo(1073741824.0)); // 2^30
            Assert.That(Eval("255 & 15"), Is.EqualTo(15.0));
        }
    }
}
