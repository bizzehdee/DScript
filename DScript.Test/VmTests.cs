using DScript;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    // Phase 1: exercises the bytecode container, the VM execution loop, and the
    // disassembler against hand-assembled chunks (no compiler yet).
    public class VmTests
    {
        private static int Op(char c) => (int)(ScriptLex.LexTypes)c;

        [Test]
        public void Vm_EvaluatesArithmeticExpression()
        {
            // (2 + 3) * 4 == 20
            var chunk = new Chunk();
            var two = chunk.AddConstant(ConstantValue.Int(2));
            var three = chunk.AddConstant(ConstantValue.Int(3));
            var four = chunk.AddConstant(ConstantValue.Int(4));

            chunk.Emit(OpCode.Constant, two);
            chunk.Emit(OpCode.Constant, three);
            chunk.Emit(OpCode.Binary, Op('+'));
            chunk.Emit(OpCode.Constant, four);
            chunk.Emit(OpCode.Binary, Op('*'));
            chunk.Emit(OpCode.Return);

            var result = new VirtualMachine().Run(chunk);

            Assert.That(result.Int, Is.EqualTo(20));
        }

        [Test]
        public void Vm_HandlesUnaryAndShift()
        {
            // (1 << 4) == 16, then negate -> -16, then ~(-16) == 15
            var chunk = new Chunk();
            var one = chunk.AddConstant(ConstantValue.Int(1));
            var fourC = chunk.AddConstant(ConstantValue.Int(4));

            chunk.Emit(OpCode.Constant, one);
            chunk.Emit(OpCode.Constant, fourC);
            chunk.Emit(OpCode.Shift, (int)ScriptLex.LexTypes.LShift);
            chunk.Emit(OpCode.Negate);
            chunk.Emit(OpCode.BitNot);
            chunk.Emit(OpCode.Return);

            var result = new VirtualMachine().Run(chunk);

            Assert.That(result.Int, Is.EqualTo(15));
        }

        [Test]
        public void Vm_ConditionalJumpSelectsBranch()
        {
            // if (false) 111 else 222  -> 222
            var chunk = new Chunk();
            var t111 = chunk.AddConstant(ConstantValue.Int(111));
            var t222 = chunk.AddConstant(ConstantValue.Int(222));

            chunk.Emit(OpCode.PushFalse);
            var toElse = chunk.EmitJump(OpCode.JumpIfFalse);
            chunk.Emit(OpCode.Constant, t111);
            var toEnd = chunk.EmitJump(OpCode.Jump);
            chunk.PatchJump(toElse);
            chunk.Emit(OpCode.Constant, t222);
            chunk.PatchJump(toEnd);
            chunk.Emit(OpCode.Return);

            var result = new VirtualMachine().Run(chunk);

            Assert.That(result.Int, Is.EqualTo(222));
        }

        [Test]
        public void Disassembler_RendersOpcodesAndOperands()
        {
            var chunk = new Chunk();
            var c = chunk.AddConstant(ConstantValue.Int(7));
            chunk.Emit(OpCode.Constant, c);
            chunk.Emit(OpCode.Binary, Op('+'));
            chunk.Emit(OpCode.Return);

            var text = Disassembler.Disassemble(chunk);

            Assert.That(text, Does.Contain("Constant"));
            Assert.That(text, Does.Contain("(7)"));
            Assert.That(text, Does.Contain("Binary"));
            Assert.That(text, Does.Contain("Return"));
        }
    }
}
