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

        // Contiguous copy of Code for fast indexed access during execution.
        // Cached lazily and invalidated by any emit/patch, so it is always
        // rebuilt once after compilation completes and reused across runs.
        private byte[] codeArray;

        /// <summary>The instruction bytes as a contiguous array (cached).</summary>
        public byte[] CodeBytes => codeArray ??= Code.ToArray();

        /// <summary>
        /// Per-call-site inline cache for variable resolution. One slot per byte of
        /// code (indexed by a name-opcode's operand offset, which is unique per
        /// site). Each slot remembers the environment a GetVar/SetVar last resolved
        /// against, that environment's binding version, and the resolved link — so a
        /// repeat hit in a stable scope (e.g. a loop body) skips the scope-chain
        /// walk entirely. Validated by environment identity + version, so a new
        /// binding that could shadow the cached result forces a re-resolve.
        /// </summary>
        public struct InlineCacheEntry
        {
            public Environment Env;
            public ScriptVarLink Link;
            public int Version;
        }

        private InlineCacheEntry[] inlineCache;

        /// <summary>Lazily-allocated inline cache, one slot per code byte.</summary>
        public InlineCacheEntry[] InlineCache => inlineCache ??= new InlineCacheEntry[CodeBytes.Length];

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

        /// <summary>
        /// Original source text of a function body (set for function chunks).
        /// Retained so a function value can be rendered back to source by
        /// JSON.stringify / GetParsableString and round-tripped through eval.
        /// </summary>
        public string Source { get; set; } = string.Empty;

        public int Count => Code.Count;

        // --- emit helpers (distinct by arity to avoid overload ambiguity) ---

        /// <summary>Emit a bare opcode. Returns the offset it was written at.</summary>
        public int Emit(OpCode op)
        {
            var at = Code.Count;
            Code.Add((byte)op);
            codeArray = null;
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
            codeArray = null;
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
            codeArray = null;
        }

        // --- reading (used by the VM and disassembler) ----------------------

        /// <summary>Read a 4-byte little-endian int at <paramref name="offset"/>.</summary>
        public int ReadInt(int offset)
        {
            var code = CodeBytes;
            return code[offset]
                   | (code[offset + 1] << 8)
                   | (code[offset + 2] << 16)
                   | (code[offset + 3] << 24);
        }

        // --- pool interning -------------------------------------------------

        public int AddConstant(ConstantValue value)
        {
            Constants.Add(value);
            return Constants.Count - 1;
        }

        // Accelerates AddName interning: maps an already-seen name to its index in
        // Names so repeated identifiers don't trigger an O(n) List.IndexOf scan
        // (which made interning O(n^2) over a name-heavy script's compilation).
        private Dictionary<string, int> nameToIndex;

        /// <summary>Intern an identifier/property name, returning its index.</summary>
        public int AddName(string name)
        {
            if (nameToIndex == null)
            {
                // Seed from any names already present (e.g. populated directly by
                // deserialization) so the index stays consistent with Names.
                nameToIndex = new Dictionary<string, int>(Names.Count);
                for (var i = 0; i < Names.Count; i++)
                {
                    nameToIndex[Names[i]] = i;
                }
            }

            if (nameToIndex.TryGetValue(name, out var existing))
            {
                return existing;
            }

            var index = Names.Count;
            Names.Add(name);
            nameToIndex[name] = index;
            return index;
        }

        public int AddFunction(Chunk function)
        {
            Functions.Add(function);
            return Functions.Count - 1;
        }

        // --- peephole fusion -------------------------------------------------

        /// <summary>
        /// If the just-compiled binary right operand (everything emitted since
        /// <paramref name="operandStart"/>) is exactly one <see cref="OpCode.Constant"/>
        /// instruction, replace the pending <c>Constant; Binary</c> pair with a
        /// single fused <see cref="OpCode.BinaryConst"/>, and return true.
        ///
        /// Fusing is only attempted for a lone Constant: a larger operand (e.g. a
        /// ternary whose arm happens to end in a literal) emits more than one
        /// instruction and contains internal jumps, so it is left alone. Because a
        /// single-literal operand contains no control flow and is never the target
        /// of a jump, rewriting it here cannot invalidate any jump offset.
        /// </summary>
        public bool TryFuseConstantBinary(int operandStart, int op)
        {
            // operand must be exactly one Constant instruction: opcode + 4-byte index
            if (Code.Count - operandStart != 5) return false;
            if (Code[operandStart] != (byte)OpCode.Constant) return false;

            var constIndex = Code[operandStart + 1]
                             | (Code[operandStart + 2] << 8)
                             | (Code[operandStart + 3] << 16)
                             | (Code[operandStart + 4] << 24);

            Code.RemoveRange(operandStart, 5);
            codeArray = null;
            Emit(OpCode.BinaryConst, op, constIndex);
            return true;
        }
    }
}
