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
            var ip = 0;
            var sawReturn = false;

            while (ip < code.Length)
            {
                // A single return ends straight-line code; anything after it implies
                // control flow we do not model.
                if (sawReturn) return null;

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

                    case OpCode.Return:
                        instrs.Add(JitInstruction.Return());
                        sawReturn = true;
                        break;
                    case OpCode.Halt:
                        // Implicit return undefined.
                        instrs.Add(JitInstruction.PushUndefined());
                        instrs.Add(JitInstruction.Return());
                        sawReturn = true;
                        break;

                    default:
                        // Jumps, assignments, object/array/property ops, try, tail and
                        // method calls, etc. — not supported.
                        return null;
                }
            }

            return instrs;
        }
    }
}
