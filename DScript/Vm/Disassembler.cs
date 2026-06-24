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
                case OpCode.EnterTry:
                    return 3;

                case OpCode.BinaryConst:
                case OpCode.BinaryIntConst:
                case OpCode.TaggedTemplate:
                    return 2;

                case OpCode.GetVarGetProp:
                    return 2;

                case OpCode.Constant:
                case OpCode.GetVar:
                case OpCode.SetVar:
                case OpCode.DeclareVar:
                case OpCode.DeclareConst:
                case OpCode.DeclareLocal:
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
                case OpCode.LeaveTry:
                case OpCode.LeaveCatch:
                case OpCode.DefineGetter:
                case OpCode.DefineSetter:
                case OpCode.SetVarPop:
                case OpCode.SetPropPop:
                case OpCode.GetPropMethod:
                case OpCode.GetPropCall0:
                    return 1;

                case OpCode.GetVarGetVarBinary:
                    return 3;

                default:
                    return 0;
            }
        }

        public static string Disassemble(Chunk chunk)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"== {chunk.Name} ==");

            var lastLine = -1;
            var offset = 0;
            while (offset < chunk.Count)
            {
                offset = DisassembleInstruction(chunk, offset, sb, ref lastLine);
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
            var lastLine = -1;
            return DisassembleInstruction(chunk, offset, sb, ref lastLine);
        }

        // Returns true for opcodes whose single operand is stored as 1 byte (narrow form).
        // GetVarGetPropN has two 1-byte operands and is handled separately.
        private static bool IsNarrow(OpCode op) =>
            op is OpCode.GetVarN or OpCode.SetVarN or OpCode.ConstantN or
                  OpCode.GetPropN or OpCode.SetPropN or OpCode.DeclareVarN or
                  OpCode.DeclareConstN or OpCode.DeclareLocalN or OpCode.InitPropN or
                  OpCode.SetVarPopN or OpCode.SetPropPopN or
                  OpCode.GetPropMethodN or OpCode.GetPropCall0N;

        private static int DisassembleInstruction(Chunk chunk, int offset, StringBuilder sb, ref int lastLine)
        {
            var line = chunk.GetLineForOffset(offset);
            if (line != lastLine)
            {
                sb.Append($"{offset:0000} {line,4} ");
                lastLine = line;
            }
            else
            {
                sb.Append($"{offset:0000}    | ");
            }

            var op = (OpCode)chunk.Code[offset];
            sb.Append(op);

            var next = offset + 1;

            if (op == OpCode.GetVarGetPropN)
            {
                // Two 1-byte operands: variable index then property index
                var varIdx  = chunk.Code[next++];
                var propIdx = chunk.Code[next++];
                sb.Append(' ').Append(varIdx);
                if (varIdx  < chunk.Names.Count) sb.Append($" ({chunk.Names[varIdx]})");
                sb.Append(' ').Append(propIdx);
                if (propIdx < chunk.Names.Count) sb.Append($".{chunk.Names[propIdx]}");
            }
            else if (op == OpCode.GetVarGetVarBinaryN)
            {
                // Three 1-byte operands: op, var1, var2
                var opCode = chunk.Code[next++];
                var var1   = chunk.Code[next++];
                var var2   = chunk.Code[next++];
                sb.Append($" op={opCode}");
                sb.Append(' ').Append(var1);
                if (var1 < chunk.Names.Count) sb.Append($" ({chunk.Names[var1]})");
                sb.Append(' ').Append(var2);
                if (var2 < chunk.Names.Count) sb.Append($" ({chunk.Names[var2]})");
            }
            else if (IsNarrow(op))
            {
                // Narrow form: single 1-byte operand
                var value = chunk.Code[next];
                sb.Append(' ').Append(value);
                sb.Append(AnnotateNarrow(chunk, op, value));
                next++;
            }
            else
            {
                var operands = OperandCount(op);
                for (var i = 0; i < operands; i++)
                {
                    var value = chunk.ReadInt(next);
                    sb.Append(' ').Append(value);
                    sb.Append(Annotate(chunk, op, i, value));
                    next += 4;
                }
            }

            sb.AppendLine();
            return next;
        }

        private static string AnnotateNarrow(Chunk chunk, OpCode op, int value)
        {
            switch (op)
            {
                case OpCode.ConstantN when value >= 0 && value < chunk.Constants.Count:
                    return $" ({chunk.Constants[value]})";
                case OpCode.GetVarN:
                case OpCode.SetVarN:
                case OpCode.DeclareVarN:
                case OpCode.DeclareConstN:
                case OpCode.DeclareLocalN:
                case OpCode.GetPropN:
                case OpCode.SetPropN:
                case OpCode.InitPropN:
                case OpCode.SetVarPopN:
                case OpCode.SetPropPopN:
                case OpCode.GetPropMethodN:
                case OpCode.GetPropCall0N:
                    if (value >= 0 && value < chunk.Names.Count) return $" ({chunk.Names[value]})";
                    break;
            }
            return string.Empty;
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
                case OpCode.DeclareLocal:
                case OpCode.GetProp:
                case OpCode.SetProp:
                case OpCode.DeleteProp:
                case OpCode.InitProp:
                case OpCode.SetVarPop:
                case OpCode.SetPropPop:
                case OpCode.GetPropMethod:
                case OpCode.GetPropCall0:
                    if (value >= 0 && value < chunk.Names.Count) return $" ({chunk.Names[value]})";
                    break;
                case OpCode.GetVarGetProp when operandIndex == 0:
                case OpCode.GetVarGetProp when operandIndex == 1:
                    if (value >= 0 && value < chunk.Names.Count) return $" ({chunk.Names[value]})";
                    break;
                case OpCode.GetVarGetVarBinary when operandIndex == 0:
                    return $" ({ScriptLex.LexTypesToString((ScriptLex.LexTypes)value)})";
                case OpCode.GetVarGetVarBinary when operandIndex == 1:
                case OpCode.GetVarGetVarBinary when operandIndex == 2:
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
