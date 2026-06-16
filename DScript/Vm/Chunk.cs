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

using System.Collections.Generic;

namespace DScript.Vm
{
    /// <summary>
    /// A unit of compiled bytecode: the instruction stream plus the pools it
    /// references. The top-level program is one chunk; each function body is a
    /// nested chunk in <see cref="Functions"/>.
    /// </summary>
    public sealed class Chunk
    {
        /// <summary>Raw instruction bytes (opcodes interleaved with operands).</summary>
        public List<byte> Code { get; } = [];

        /// <summary>Literal value constants referenced by <see cref="OpCode.Constant"/>.</summary>
        public List<ConstantValue> Constants { get; } = [];

        /// <summary>Identifier/property names referenced by name-indexed opcodes.</summary>
        public List<string> Names { get; } = [];

        /// <summary>Nested function bodies referenced by <see cref="OpCode.MakeClosure"/>.</summary>
        public List<Chunk> Functions { get; } = [];

        /// <summary>Declared parameter names, when this chunk is a function body.</summary>
        public List<string> Parameters { get; } = [];

        /// <summary>Optional name (function name or "&lt;main&gt;") for diagnostics.</summary>
        public string Name { get; set; } = "<main>";

        public int Count => Code.Count;

        // --- emit helpers (distinct by arity to avoid overload ambiguity) ---

        /// <summary>Emit a bare opcode. Returns the offset it was written at.</summary>
        public int Emit(OpCode op)
        {
            var at = Code.Count;
            Code.Add((byte)op);
            return at;
        }

        /// <summary>Emit an opcode followed by one 4-byte int operand.</summary>
        public int Emit(OpCode op, int operand)
        {
            var at = Emit(op);
            EmitInt(operand);
            return at;
        }

        /// <summary>Emit an opcode followed by two 4-byte int operands.</summary>
        public int Emit(OpCode op, int operand1, int operand2)
        {
            var at = Emit(op);
            EmitInt(operand1);
            EmitInt(operand2);
            return at;
        }

        /// <summary>Emit an opcode followed by four 4-byte int operands.</summary>
        public int Emit(OpCode op, int operand1, int operand2, int operand3, int operand4)
        {
            var at = Emit(op);
            EmitInt(operand1);
            EmitInt(operand2);
            EmitInt(operand3);
            EmitInt(operand4);
            return at;
        }

        /// <summary>Append a raw 4-byte little-endian int to the stream.</summary>
        public void EmitInt(int value)
        {
            Code.Add((byte)(value & 0xFF));
            Code.Add((byte)((value >> 8) & 0xFF));
            Code.Add((byte)((value >> 16) & 0xFF));
            Code.Add((byte)((value >> 24) & 0xFF));
        }

        // --- jump backpatching ----------------------------------------------

        /// <summary>
        /// Emit a jump whose target is not yet known. Returns the offset of the
        /// operand slot, to be resolved later with <see cref="PatchJump"/>.
        /// </summary>
        public int EmitJump(OpCode op)
        {
            Emit(op);
            var operandAt = Code.Count;
            EmitInt(-1); // placeholder
            return operandAt;
        }

        /// <summary>Set a previously-emitted jump operand to the current end of the stream.</summary>
        public void PatchJump(int operandOffset)
        {
            PatchJumpTo(operandOffset, Code.Count);
        }

        /// <summary>Set a previously-emitted jump operand to an explicit target offset.</summary>
        public void PatchJumpTo(int operandOffset, int target)
        {
            Code[operandOffset] = (byte)(target & 0xFF);
            Code[operandOffset + 1] = (byte)((target >> 8) & 0xFF);
            Code[operandOffset + 2] = (byte)((target >> 16) & 0xFF);
            Code[operandOffset + 3] = (byte)((target >> 24) & 0xFF);
        }

        // --- reading (used by the VM and disassembler) ----------------------

        /// <summary>Read a 4-byte little-endian int at <paramref name="offset"/>.</summary>
        public int ReadInt(int offset)
        {
            return Code[offset]
                   | (Code[offset + 1] << 8)
                   | (Code[offset + 2] << 16)
                   | (Code[offset + 3] << 24);
        }

        // --- pool interning -------------------------------------------------

        public int AddConstant(ConstantValue value)
        {
            Constants.Add(value);
            return Constants.Count - 1;
        }

        /// <summary>Intern an identifier/property name, returning its index.</summary>
        public int AddName(string name)
        {
            var existing = Names.IndexOf(name);
            if (existing >= 0) return existing;

            Names.Add(name);
            return Names.Count - 1;
        }

        public int AddFunction(Chunk function)
        {
            Functions.Add(function);
            return Functions.Count - 1;
        }
    }
}
