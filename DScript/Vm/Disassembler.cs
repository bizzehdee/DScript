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

using System.Text;

namespace DScript.Vm
{
    /// <summary>
    /// Renders a <see cref="Chunk"/> as human-readable assembly. Used for
    /// debugging the compiler and in tests; it is also the canonical reference
    /// for each opcode's operand layout.
    /// </summary>
    public static class Disassembler
    {
        /// <summary>Number of 4-byte int operands an opcode carries.</summary>
        public static int OperandCount(OpCode op)
        {
            switch (op)
            {
                case OpCode.Try:
                    return 4;

                case OpCode.BinaryConst:
                case OpCode.BinaryIntConst:
                    return 2;

                case OpCode.Constant:
                case OpCode.GetVar:
                case OpCode.SetVar:
                case OpCode.DeclareVar:
                case OpCode.DeclareConst:
                case OpCode.GetProp:
                case OpCode.SetProp:
                case OpCode.DeleteProp:
                case OpCode.Binary:
                case OpCode.Shift:
                case OpCode.Jump:
                case OpCode.JumpIfFalse:
                case OpCode.JumpIfTrue:
                case OpCode.JumpIfFalseOrPop:
                case OpCode.JumpIfTrueOrPop:
                case OpCode.MakeClosure:
                case OpCode.Call:
                case OpCode.CallMethod:
                case OpCode.New:
                case OpCode.InitProp:
                case OpCode.InitElem:
                    return 1;

                default:
                    return 0;
            }
        }

        public static string Disassemble(Chunk chunk)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"== {chunk.Name} ==");

            var offset = 0;
            while (offset < chunk.Count)
            {
                offset = DisassembleInstruction(chunk, offset, sb);
            }

            // recurse into nested function chunks for completeness
            foreach (var fn in chunk.Functions)
            {
                sb.AppendLine();
                sb.Append(Disassemble(fn));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Disassemble a single instruction at <paramref name="offset"/> into
        /// <paramref name="sb"/>; returns the offset of the next instruction.
        /// </summary>
        public static int DisassembleInstruction(Chunk chunk, int offset, StringBuilder sb)
        {
            var op = (OpCode)chunk.Code[offset];
            sb.Append($"{offset:0000} {op}");

            var operands = OperandCount(op);
            var next = offset + 1;
            for (var i = 0; i < operands; i++)
            {
                var value = chunk.ReadInt(next);
                sb.Append(' ').Append(value);
                sb.Append(Annotate(chunk, op, i, value));
                next += 4;
            }

            sb.AppendLine();
            return next;
        }

        private static string Annotate(Chunk chunk, OpCode op, int operandIndex, int value)
        {
            switch (op)
            {
                case OpCode.Constant when value >= 0 && value < chunk.Constants.Count:
                    return $" ({chunk.Constants[value]})";
                case OpCode.GetVar:
                case OpCode.SetVar:
                case OpCode.DeclareVar:
                case OpCode.DeclareConst:
                case OpCode.GetProp:
                case OpCode.SetProp:
                case OpCode.DeleteProp:
                case OpCode.InitProp:
                    if (value >= 0 && value < chunk.Names.Count) return $" ({chunk.Names[value]})";
                    break;
                case OpCode.Binary:
                case OpCode.Shift:
                    return $" ({ScriptLex.LexTypesToString((ScriptLex.LexTypes)value)})";
                case OpCode.BinaryConst when operandIndex == 0:
                case OpCode.BinaryIntConst when operandIndex == 0:
                    return $" ({ScriptLex.LexTypesToString((ScriptLex.LexTypes)value)})";
                case OpCode.BinaryConst when operandIndex == 1 && value >= 0 && value < chunk.Constants.Count:
                    return $" ({chunk.Constants[value]})";
                // BinaryIntConst operand 1 is a raw int — the value shown is already the int literal
            }

            return string.Empty;
        }
    }
}
