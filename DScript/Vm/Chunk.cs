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

        /// <summary>
        /// Source-line number for each byte in <see cref="Code"/>, indexed in
        /// parallel. <c>Lines[i]</c> is the 1-based line in the original source
        /// that produced the byte at <c>Code[i]</c>. 0 means "unknown".
        /// </summary>
        public List<int> Lines { get; } = [];

        // Current source line; set by the compiler before emitting each statement.
        private int currentLine;

        /// <summary>
        /// Record the source line that the compiler is currently emitting for.
        /// All subsequent <c>Emit</c> calls will tag their bytes with this line.
        /// </summary>
        public void SetCurrentLine(int line) => currentLine = line;

        /// <summary>
        /// Return the 1-based source line for the instruction starting at
        /// <paramref name="offset"/>, or 0 if no line information is available.
        /// </summary>
        public int GetLineForOffset(int offset) =>
            offset >= 0 && offset < Lines.Count ? Lines[offset] : 0;

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

        /// <summary>
        /// True if this chunk (or a same-environment sub-block such as a try/catch
        /// body) creates a closure that captures the current call environment. When
        /// false, the call frame cannot escape and may be recycled after the call.
        /// </summary>
        public bool MakesClosure { get; set; }

        /// <summary>A call frame for this function may be pooled/reused.</summary>
        public bool RecyclableFrame => !MakesClosure;

        public int Count => Code.Count;

        // --- emit helpers (distinct by arity to avoid overload ambiguity) ---

        /// <summary>Emit a bare opcode. Returns the offset it was written at.</summary>
        public int Emit(OpCode op)
        {
            var at = Code.Count;
            Code.Add((byte)op);
            Lines.Add(currentLine);
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
            Lines.Add(currentLine);
            Lines.Add(currentLine);
            Lines.Add(currentLine);
            Lines.Add(currentLine);
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

        // --- peephole fusion and folding ------------------------------------

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

            var savedLine = currentLine;
            currentLine = Lines[operandStart];
            Code.RemoveRange(operandStart, 5);
            Lines.RemoveRange(operandStart, 5);
            codeArray = null;
            Emit(OpCode.BinaryConst, op, constIndex);
            currentLine = savedLine;
            return true;
        }

        /// <summary>
        /// After a successful <see cref="TryFuseConstantBinary"/>, check whether the
        /// instruction immediately before the fused <see cref="OpCode.BinaryConst"/> is
        /// also a <see cref="OpCode.Constant"/>. If so, compute the result at compile
        /// time and replace both with a single <see cref="OpCode.Constant"/>.
        ///
        /// Only numeric results (int, double) are folded; string concatenation is left
        /// for the runtime. Returns true if folding happened.
        /// </summary>
        public bool TryFoldBinaryConst()
        {
            // Code ends with BinaryConst(op:4, constIdx:4) = 9 bytes
            if (Code.Count < 14) return false;
            var bcAt = Code.Count - 9;
            if ((OpCode)Code[bcAt] != OpCode.BinaryConst) return false;

            // The instruction before BinaryConst must be Constant(leftIdx) = 5 bytes.
            var cAt = bcAt - 5;
            if (cAt < 0 || (OpCode)Code[cAt] != OpCode.Constant) return false;

            // Guard against a false positive: if a 9-byte instruction (BinaryConst or
            // BinaryIntConst) ends at bcAt-1, its opcode is at bcAt-9 = cAt-4, and byte
            // cAt falls inside its first operand. Operator values are always < 65536, so
            // the high byte at cAt is 0x00 — identical to OpCode.Constant. Reject if the
            // 9-byte instruction's opcode is at cAt-4.
            if (cAt >= 4)
            {
                var prevOp = (OpCode)Code[cAt - 4];
                if (prevOp is OpCode.BinaryConst or OpCode.BinaryIntConst) return false;
            }

            var op = (ScriptLex.LexTypes)ReadIntFromCode(bcAt + 1);
            var leftIdx  = ReadIntFromCode(cAt + 1);
            var rightIdx = ReadIntFromCode(bcAt + 5);

            try
            {
                var lv = Constants[leftIdx].Materialize();
                var rv = Constants[rightIdx].Materialize();
                var result = lv.MathsOp(rv, op);
                var folded = ScriptVarToConstant(result);
                if (folded == null) return false;

                var savedLine = currentLine;
                currentLine = Lines[cAt];
                Code.RemoveRange(cAt, 14); // Constant(left):5 + BinaryConst:9
                Lines.RemoveRange(cAt, 14);
                codeArray = null;
                Emit(OpCode.Constant, AddConstant(folded));
                currentLine = savedLine;
                return true;
            }
            catch { return false; }
        }

        // Read a 4-byte little-endian int directly from the mutable Code list
        // (used by peephole passes before the codeArray cache is rebuilt).
        private int ReadIntFromCode(int offset) =>
            Code[offset]
            | (Code[offset + 1] << 8)
            | (Code[offset + 2] << 16)
            | (Code[offset + 3] << 24);

        // Convert a ScriptVar result to a storable ConstantValue.
        // Returns null for types we don't fold (strings, objects, …).
        private static ConstantValue ScriptVarToConstant(ScriptVar v)
        {
            if (v.IsInt)    return ConstantValue.Int(v.Int);
            if (v.IsDouble) return ConstantValue.Double(v.Float);
            return null;
        }

        // --- post-compilation passes ----------------------------------------

        // Total byte size of an instruction (opcode byte + 4 bytes per operand).
        internal static int InstructionSize(OpCode op) => op switch
        {
            OpCode.Try             => 17, // 1 + 4*4
            OpCode.BinaryConst     =>  9, // 1 + 4*2
            OpCode.BinaryIntConst  =>  9, // 1 + 4*2
            OpCode.Constant     or OpCode.GetVar      or OpCode.SetVar    or
            OpCode.DeclareVar   or OpCode.DeclareConst or
            OpCode.GetProp      or OpCode.SetProp      or OpCode.DeleteProp or
            OpCode.Binary       or OpCode.Shift        or
            OpCode.Jump         or OpCode.JumpIfFalse  or OpCode.JumpIfTrue or
            OpCode.JumpIfFalseOrPop or OpCode.JumpIfTrueOrPop             or
            OpCode.MakeClosure  or OpCode.Call         or OpCode.CallMethod or
            OpCode.New          or OpCode.InitProp      or OpCode.InitElem  =>  5, // 1 + 4*1
            _                   =>  1, // no operands
        };

        /// <summary>
        /// If the last emitted instruction is <see cref="OpCode.BinaryConst"/> and its
        /// constant is an integer, replace it with <see cref="OpCode.BinaryIntConst"/>
        /// that stores the integer value directly in the instruction stream, eliminating
        /// the constant-pool lookup on every execution of that instruction.
        /// </summary>
        public bool TryUpgradeBinaryConstToInt()
        {
            if (Code.Count < 9) return false;
            var at = Code.Count - 9;
            if ((OpCode)Code[at] != OpCode.BinaryConst) return false;

            var op       = ReadIntFromCode(at + 1);
            var constIdx = ReadIntFromCode(at + 5);
            if (Constants[constIdx].Kind != ConstantKind.Int) return false;

            var intValue = Constants[constIdx].IntValue;
            var savedLine = currentLine;
            currentLine = Lines[at];
            Code.RemoveRange(at, 9);
            Lines.RemoveRange(at, 9);
            codeArray = null;
            Emit(OpCode.BinaryIntConst, op, intValue);
            currentLine = savedLine;
            return true;
        }

        /// <summary>
        /// Walk every jump in this chunk (and all nested function chunks) and
        /// collapse any chain of unconditional <see cref="OpCode.Jump"/>s so that
        /// each jump points directly at the final destination.
        ///
        /// Example: <c>JumpIfFalse → A; [A] Jump → B</c> becomes
        /// <c>JumpIfFalse → B</c>, saving one extra dispatch per branch.
        /// Only unconditional <see cref="OpCode.Jump"/> targets are chased: they
        /// cannot change stack depth, so the collapse is always semantics-preserving.
        /// </summary>
        public void CollapseJumpChains()
        {
            var ip = 0;
            while (ip < Code.Count)
            {
                var op = (OpCode)Code[ip];
                if (op is OpCode.Jump or OpCode.JumpIfFalse or OpCode.JumpIfTrue
                         or OpCode.JumpIfFalseOrPop or OpCode.JumpIfTrueOrPop)
                {
                    var operandAt = ip + 1;
                    var target = ReadIntFromCode(operandAt);
                    var resolved = ChaseUnconditionalJumps(target);
                    if (resolved != target)
                        PatchJumpTo(operandAt, resolved);
                }
                ip += InstructionSize(op);
            }

            foreach (var fn in Functions)
                fn.CollapseJumpChains();
        }

        // Follow a chain of unconditional Jump instructions to their final target.
        // The seen set prevents infinite loops on self-referencing bytecode.
        private int ChaseUnconditionalJumps(int target)
        {
            var seen = new HashSet<int>();
            while (target < Code.Count && seen.Add(target))
            {
                if ((OpCode)Code[target] != OpCode.Jump) break;
                target = ReadIntFromCode(target + 1);
            }
            return target;
        }

        /// <summary>
        /// Remove bytecode that can never be reached: instructions that follow an
        /// unconditional exit (<see cref="OpCode.Jump"/>, <see cref="OpCode.Return"/>,
        /// <see cref="OpCode.Halt"/>, <see cref="OpCode.Throw"/>) and are not the
        /// target of any jump elsewhere in the same chunk.
        ///
        /// Operates in one linear pass: dead bytes are identified, an old→new offset
        /// remap table is built, and the live bytes are copied to a new stream with
        /// all jump targets adjusted. Recurses into nested function chunks.
        /// </summary>
        public void EliminateDeadCode()
        {
            // Pass 1: collect all jump targets (always reachable entry points).
            var jumpTargets = new HashSet<int>();
            {
                var ip = 0;
                while (ip < Code.Count)
                {
                    var op = (OpCode)Code[ip];
                    if (op is OpCode.Jump or OpCode.JumpIfFalse or OpCode.JumpIfTrue
                             or OpCode.JumpIfFalseOrPop or OpCode.JumpIfTrueOrPop)
                    {
                        jumpTargets.Add(ReadIntFromCode(ip + 1));
                    }
                    ip += InstructionSize(op);
                }
            }

            // Pass 2: mark dead bytes — those after a terminator that are not a
            // jump target (landing points end the dead region).
            var dead = new bool[Code.Count];
            {
                var ip = 0;
                var inDead = false;
                while (ip < Code.Count)
                {
                    if (jumpTargets.Contains(ip)) inDead = false;

                    var op = (OpCode)Code[ip];
                    var size = InstructionSize(op);

                    if (inDead)
                    {
                        for (var k = ip; k < ip + size; k++) dead[k] = true;
                    }
                    else if (op is OpCode.Jump or OpCode.Return or OpCode.Halt or OpCode.Throw)
                    {
                        inDead = true;
                    }

                    ip += size;
                }
            }

            var deadCount = 0;
            foreach (var d in dead) if (d) deadCount++;

            if (deadCount == 0)
            {
                foreach (var fn in Functions) fn.EliminateDeadCode();
                return;
            }

            // Build an old-byte-offset → new-byte-offset remap. For a live byte at
            // old position i, remap[i] is its position in the rebuilt stream.
            var remap = new int[Code.Count + 1];
            {
                var newOff = 0;
                for (var i = 0; i <= Code.Count; i++)
                {
                    remap[i] = newOff;
                    if (i < Code.Count && !dead[i]) newOff++;
                }
            }

            // Rebuild the code stream: copy live instructions, remapping jump targets.
            // Build the parallel Lines list at the same time by filtering dead bytes.
            var newCode = new List<byte>(Code.Count - deadCount);
            var newLines = new List<int>(Code.Count - deadCount);
            {
                var ip = 0;
                while (ip < Code.Count)
                {
                    if (dead[ip]) { ip++; continue; }

                    var op = (OpCode)Code[ip];
                    var size = InstructionSize(op);
                    var isJump = op is OpCode.Jump or OpCode.JumpIfFalse or OpCode.JumpIfTrue
                                        or OpCode.JumpIfFalseOrPop or OpCode.JumpIfTrueOrPop;

                    newCode.Add(Code[ip]); // opcode byte
                    newLines.Add(Lines[ip]);

                    var src = ip + 1;
                    if (isJump)
                    {
                        AppendInt(newCode, remap[ReadIntFromCode(src)]); // remapped target
                        newLines.Add(Lines[src]);
                        newLines.Add(Lines[src + 1]);
                        newLines.Add(Lines[src + 2]);
                        newLines.Add(Lines[src + 3]);
                        src += 4;
                    }

                    while (src < ip + size) // remaining operands verbatim
                    {
                        newCode.Add(Code[src]);
                        newLines.Add(Lines[src]);
                        src++;
                    }

                    ip += size;
                }
            }

            Code.Clear();
            Code.AddRange(newCode);
            Lines.Clear();
            Lines.AddRange(newLines);
            codeArray = null;
            inlineCache = null; // offsets have shifted

            foreach (var fn in Functions) fn.EliminateDeadCode();
        }

        private static void AppendInt(List<byte> list, int value)
        {
            list.Add((byte)(value & 0xFF));
            list.Add((byte)((value >> 8) & 0xFF));
            list.Add((byte)((value >> 16) & 0xFF));
            list.Add((byte)((value >> 24) & 0xFF));
        }
    }
}
