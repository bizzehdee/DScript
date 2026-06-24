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
using DScript.Vm;

namespace DScript.Jit
{
    /// <summary>
    /// Shared JIT front-end: decides whether a chunk is JIT-eligible and, if so,
    /// lowers its bytecode to a uniform list of <see cref="JitInstruction"/>s. This
    /// is the single place that knows which opcodes are supported, how operands are
    /// encoded (wide vs narrow), how fused super-instructions expand, and how call
    /// sites read their monomorphic profile — so every back-end shares that logic.
    ///
    /// Returns <c>null</c> to decline a chunk (control flow, assignments,
    /// generators/async, or any unsupported opcode), in which case the VM keeps
    /// interpreting it.
    /// </summary>
    internal static class JitDecoder
    {
        public static List<JitInstruction> Decode(Chunk chunk)
        {
            // Suspend/resume execution models are not linearisable here.
            if (chunk.IsGenerator || chunk.IsAsync)
                return null;

            var code = chunk.CodeBytes;
            var instrs = new List<JitInstruction>(code.Length);

            // Parallel to instrs: the source bytecode offset of each instruction's
            // opcode, used to resolve jump targets (which are bytecode offsets) to
            // instruction indices once decoding is done.
            var offsets = new List<int>(code.Length);
            // Indices in instrs of jump instructions; their IntValue currently holds
            // the raw target bytecode offset and is rewritten to an index below.
            var jumpIndices = new List<int>();

            var ip = 0;
            while (ip < code.Length)
            {
                var opOffset = ip;
                var firstIndex = instrs.Count; // first instruction emitted this iteration
                var op = (OpCode)code[ip];
                ip++;

                switch (op)
                {
                    case OpCode.Constant:
                        instrs.Add(JitInstruction.PushConst(chunk.Constants[chunk.ReadInt(ip)]));
                        ip += 4;
                        break;
                    case OpCode.ConstantN:
                        instrs.Add(JitInstruction.PushConst(chunk.Constants[code[ip]]));
                        ip += 1;
                        break;

                    case OpCode.GetVar:
                        instrs.Add(JitInstruction.PushVar(chunk.Names[chunk.ReadInt(ip)]));
                        ip += 4;
                        break;
                    case OpCode.GetVarN:
                        instrs.Add(JitInstruction.PushVar(chunk.Names[code[ip]]));
                        ip += 1;
                        break;

                    case OpCode.GetProp:
                        instrs.Add(JitInstruction.GetProp(chunk.Names[chunk.ReadInt(ip)]));
                        ip += 4;
                        break;
                    case OpCode.GetPropN:
                        instrs.Add(JitInstruction.GetProp(chunk.Names[code[ip]]));
                        ip += 1;
                        break;

                    case OpCode.PushUndefined: instrs.Add(JitInstruction.PushUndefined()); break;
                    case OpCode.PushNull:      instrs.Add(JitInstruction.PushNull()); break;
                    case OpCode.PushTrue:      instrs.Add(JitInstruction.PushIntLiteral(1)); break;
                    case OpCode.PushFalse:     instrs.Add(JitInstruction.PushIntLiteral(0)); break;

                    case OpCode.Pop:           instrs.Add(JitInstruction.Pop()); break;
                    case OpCode.Not:           instrs.Add(JitInstruction.Not()); break;

                    case OpCode.Binary:
                        instrs.Add(JitInstruction.Binary((ScriptLex.LexTypes)chunk.ReadInt(ip)));
                        ip += 4;
                        break;
                    case OpCode.BinaryConst:
                        // left already on stack; push the constant, then apply op.
                        instrs.Add(JitInstruction.PushConst(chunk.Constants[chunk.ReadInt(ip + 4)]));
                        instrs.Add(JitInstruction.Binary((ScriptLex.LexTypes)chunk.ReadInt(ip)));
                        ip += 8;
                        break;
                    case OpCode.BinaryIntConst:
                        instrs.Add(JitInstruction.PushIntLiteral(chunk.ReadInt(ip + 4)));
                        instrs.Add(JitInstruction.Binary((ScriptLex.LexTypes)chunk.ReadInt(ip)));
                        ip += 8;
                        break;
                    case OpCode.GetVarGetVarBinary:
                        instrs.Add(JitInstruction.PushVar(chunk.Names[chunk.ReadInt(ip + 4)]));
                        instrs.Add(JitInstruction.PushVar(chunk.Names[chunk.ReadInt(ip + 8)]));
                        instrs.Add(JitInstruction.Binary((ScriptLex.LexTypes)chunk.ReadInt(ip)));
                        ip += 12;
                        break;
                    case OpCode.GetVarGetVarBinaryN:
                        instrs.Add(JitInstruction.PushVar(chunk.Names[code[ip + 1]]));
                        instrs.Add(JitInstruction.PushVar(chunk.Names[code[ip + 2]]));
                        instrs.Add(JitInstruction.Binary((ScriptLex.LexTypes)code[ip]));
                        ip += 3;
                        break;

                    case OpCode.Call:
                    {
                        var site = ip; // operand-start: matches CallProfiles indexing
                        var argc = chunk.ReadInt(ip);
                        var profile = chunk.CallProfiles[site];
                        var mono = profile.State == Chunk.CallSiteMorphism.Monomorphic
                            ? profile.Callee0
                            : null;
                        instrs.Add(JitInstruction.Call(argc, mono));
                        ip += 4;
                        break;
                    }

                    // Structured branches (if/while/for). The conditional variants pop
                    // their condition, so the operand stack is empty at every jump
                    // point — which keeps the emitter's flat model valid. The
                    // conditional-pop variants (&&/||/??/?., for..of) have non-empty
                    // stack effects at the branch and are left declined for now.
                    case OpCode.Jump:
                        jumpIndices.Add(instrs.Count);
                        instrs.Add(JitInstruction.Jump(chunk.ReadInt(ip)));   // raw byte target
                        ip += 4;
                        break;
                    case OpCode.JumpIfFalse:
                        jumpIndices.Add(instrs.Count);
                        instrs.Add(JitInstruction.JumpIfFalse(chunk.ReadInt(ip)));
                        ip += 4;
                        break;
                    case OpCode.JumpIfTrue:
                        jumpIndices.Add(instrs.Count);
                        instrs.Add(JitInstruction.JumpIfTrue(chunk.ReadInt(ip)));
                        ip += 4;
                        break;

                    case OpCode.Return:
                        instrs.Add(JitInstruction.Return());
                        break;
                    case OpCode.Halt:
                        // Implicit return undefined.
                        instrs.Add(JitInstruction.PushUndefined());
                        instrs.Add(JitInstruction.Return());
                        break;

                    default:
                        // Conditional-pop jumps, for..of, assignments, object/array ops,
                        // try, tail and method calls, etc. — not supported.
                        return null;
                }

                // Record the source offset for every instruction emitted this
                // iteration so jump targets (bytecode offsets) can be mapped to indices.
                for (var k = firstIndex; k < instrs.Count; k++)
                    offsets.Add(opOffset);
            }

            // Resolve jump targets (bytecode offsets) to instruction indices.
            var offsetToIndex = new Dictionary<int, int>(offsets.Count);
            for (var i = 0; i < offsets.Count; i++)
                if (!offsetToIndex.ContainsKey(offsets[i]))
                    offsetToIndex[offsets[i]] = i;

            foreach (var ji in jumpIndices)
            {
                if (!offsetToIndex.TryGetValue(instrs[ji].IntValue, out var targetIndex))
                    return null; // target not at an instruction boundary we model
                instrs[ji] = instrs[ji].Kind switch
                {
                    JitOpKind.Jump        => JitInstruction.Jump(targetIndex),
                    JitOpKind.JumpIfFalse => JitInstruction.JumpIfFalse(targetIndex),
                    _                     => JitInstruction.JumpIfTrue(targetIndex),
                };
            }

            return instrs;
        }
    }
}
