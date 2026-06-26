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
        public static List<JitInstruction> Decode(Chunk chunk) => Decode(chunk, out _);

        /// <summary>
        /// Map a bytecode offset to the index of the first <see cref="JitInstruction"/>
        /// decoded from it, for the given chunk. Returns <c>-1</c> if the chunk declines
        /// or the offset is not an instruction boundary. Used by OSR to resolve a
        /// loop-header offset to a resume index in the decoded stream.
        /// </summary>
        public static int OffsetToInstructionIndex(Chunk chunk, int offset)
        {
            Decode(chunk, out _, out var offsetToIndex);
            if (offsetToIndex == null) return -1;
            return offsetToIndex.TryGetValue(offset, out var idx) ? idx : -1;
        }

        public static List<JitInstruction> Decode(Chunk chunk, out string declineReason)
            => Decode(chunk, out declineReason, out _);

        /// <summary>
        /// Decode a chunk, also reporting <paramref name="declineReason"/> — null on
        /// success, otherwise a short human-readable reason the chunk cannot be JIT
        /// compiled (used by <see cref="JitDiagnostics"/>) — and
        /// <paramref name="offsetToIndex"/>, a map from each instruction's source
        /// bytecode offset to its index in the decoded stream (null on decline).
        /// </summary>
        public static List<JitInstruction> Decode(Chunk chunk, out string declineReason,
                                                  out Dictionary<int, int> offsetToIndex)
        {
            declineReason = null;
            offsetToIndex = null;

            // Suspend/resume execution models are not linearisable here.
            if (chunk.IsGenerator || chunk.IsAsync)
            {
                declineReason = "generator or async function";
                return null;
            }

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

                    // Fused GetVar + GetProp superinstruction → expand to primitives.
                    case OpCode.GetVarGetProp:
                        instrs.Add(JitInstruction.PushVar(chunk.Names[chunk.ReadInt(ip)]));
                        instrs.Add(JitInstruction.GetProp(chunk.Names[chunk.ReadInt(ip + 4)]));
                        ip += 8;
                        break;
                    case OpCode.GetVarGetPropN:
                        instrs.Add(JitInstruction.PushVar(chunk.Names[code[ip]]));
                        instrs.Add(JitInstruction.GetProp(chunk.Names[code[ip + 1]]));
                        ip += 2;
                        break;

                    case OpCode.GetPropMethod:
                        instrs.Add(JitInstruction.GetPropMethod(chunk.Names[chunk.ReadInt(ip)]));
                        ip += 4;
                        break;
                    case OpCode.GetPropMethodN:
                        instrs.Add(JitInstruction.GetPropMethod(chunk.Names[code[ip]]));
                        ip += 1;
                        break;
                    case OpCode.GetPropCall0:
                        instrs.Add(JitInstruction.GetPropCall0(chunk.Names[chunk.ReadInt(ip)]));
                        ip += 4;
                        break;
                    case OpCode.GetPropCall0N:
                        instrs.Add(JitInstruction.GetPropCall0(chunk.Names[code[ip]]));
                        ip += 1;
                        break;
                    case OpCode.CallMethod:
                        instrs.Add(JitInstruction.CallMethod(chunk.ReadInt(ip)));
                        ip += 4;
                        break;

                    case OpCode.SetVar:
                        instrs.Add(JitInstruction.SetVar(chunk.Names[chunk.ReadInt(ip)]));
                        ip += 4;
                        break;
                    case OpCode.SetVarN:
                        instrs.Add(JitInstruction.SetVar(chunk.Names[code[ip]]));
                        ip += 1;
                        break;
                    case OpCode.SetVarPop:
                        instrs.Add(JitInstruction.SetVarPop(chunk.Names[chunk.ReadInt(ip)]));
                        ip += 4;
                        break;
                    case OpCode.SetVarPopN:
                        instrs.Add(JitInstruction.SetVarPop(chunk.Names[code[ip]]));
                        ip += 1;
                        break;
                    case OpCode.SetProp:
                        instrs.Add(JitInstruction.SetProp(chunk.Names[chunk.ReadInt(ip)]));
                        ip += 4;
                        break;
                    case OpCode.SetPropN:
                        instrs.Add(JitInstruction.SetProp(chunk.Names[code[ip]]));
                        ip += 1;
                        break;
                    case OpCode.SetPropPop:
                        instrs.Add(JitInstruction.SetPropPop(chunk.Names[chunk.ReadInt(ip)]));
                        ip += 4;
                        break;
                    case OpCode.SetPropPopN:
                        instrs.Add(JitInstruction.SetPropPop(chunk.Names[code[ip]]));
                        ip += 1;
                        break;

                    case OpCode.DeclareVar:
                        instrs.Add(JitInstruction.DeclareVar(chunk.Names[chunk.ReadInt(ip)]));
                        ip += 4;
                        break;
                    case OpCode.DeclareVarN:
                        instrs.Add(JitInstruction.DeclareVar(chunk.Names[code[ip]]));
                        ip += 1;
                        break;
                    case OpCode.DeclareLocal:
                        instrs.Add(JitInstruction.DeclareLocal(chunk.Names[chunk.ReadInt(ip)]));
                        ip += 4;
                        break;
                    case OpCode.DeclareLocalN:
                        instrs.Add(JitInstruction.DeclareLocal(chunk.Names[code[ip]]));
                        ip += 1;
                        break;
                    case OpCode.DeclareConst:
                        instrs.Add(JitInstruction.DeclareConst(chunk.Names[chunk.ReadInt(ip)]));
                        ip += 4;
                        break;
                    case OpCode.DeclareConstN:
                        instrs.Add(JitInstruction.DeclareConst(chunk.Names[code[ip]]));
                        ip += 1;
                        break;

                    case OpCode.PushUndefined: instrs.Add(JitInstruction.PushUndefined()); break;
                    case OpCode.PushNull:      instrs.Add(JitInstruction.PushNull()); break;
                    case OpCode.PushTrue:      instrs.Add(JitInstruction.PushIntLiteral(1)); break;
                    case OpCode.PushFalse:     instrs.Add(JitInstruction.PushIntLiteral(0)); break;

                    case OpCode.Pop:           instrs.Add(JitInstruction.Pop()); break;
                    case OpCode.Not:           instrs.Add(JitInstruction.Not()); break;
                    case OpCode.EnterBlock:    instrs.Add(JitInstruction.EnterBlock()); break;
                    case OpCode.LeaveBlock:    instrs.Add(JitInstruction.LeaveBlock()); break;
                    case OpCode.GetIndex:      instrs.Add(JitInstruction.GetIndex()); break;
                    case OpCode.SetIndex:      instrs.Add(JitInstruction.SetIndex()); break;
                    case OpCode.Negate:        instrs.Add(JitInstruction.Negate()); break;
                    case OpCode.BitNot:        instrs.Add(JitInstruction.BitNot()); break;
                    case OpCode.Typeof:        instrs.Add(JitInstruction.Typeof()); break;
                    case OpCode.ToNumber:      instrs.Add(JitInstruction.ToNumber()); break;
                    case OpCode.Shift:
                        instrs.Add(JitInstruction.Shift((ScriptLex.LexTypes)chunk.ReadInt(ip)));
                        ip += 4;
                        break;

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

                    case OpCode.MakeClosure:
                        // Create a function object capturing the current environment.
                        // The nested chunk is baked so the emitter can reconstruct it.
                        instrs.Add(JitInstruction.MakeClosure(chunk.Functions[chunk.ReadInt(ip)]));
                        ip += 4;
                        break;

                    case OpCode.Call:
                    {
                        var site = ip; // operand-start: matches CallProfiles indexing
                        var argc = chunk.ReadInt(ip);
                        var profile = chunk.CallProfiles[site];
                        // Bake the observed callee(s) so the emitter can inline them:
                        // one for a monomorphic site, two for a bimorphic site.
                        ScriptVar callee0 = null, callee1 = null;
                        if (profile.State == Chunk.CallSiteMorphism.Monomorphic)
                        {
                            callee0 = profile.Callee0;
                        }
                        else if (profile.State == Chunk.CallSiteMorphism.Bimorphic)
                        {
                            callee0 = profile.Callee0;
                            callee1 = profile.Callee1;
                        }
                        instrs.Add(JitInstruction.Call(argc, callee0, callee1));
                        ip += 4;
                        break;
                    }

                    case OpCode.TailCall:
                    {
                        // `return f(x)` in tail position. The interpreter routes VM
                        // callees through a trampoline that gives unbounded tail
                        // recursion; compiled code can't reproduce that, so a
                        // self-tail-recursive function would grow the C# stack and
                        // overflow. We therefore decline when any profiled callee is
                        // this very chunk, and otherwise lower the tail call to an
                        // ordinary Call + Return (correct for bounded depth, and it
                        // reuses the existing inlining/dispatch machinery).
                        var site = ip;
                        var argc = chunk.ReadInt(ip);
                        var profile = chunk.CallProfiles[site];
                        ScriptVar callee0 = null, callee1 = null;
                        if (profile.State == Chunk.CallSiteMorphism.Monomorphic)
                        {
                            callee0 = profile.Callee0;
                        }
                        else if (profile.State == Chunk.CallSiteMorphism.Bimorphic)
                        {
                            callee0 = profile.Callee0;
                            callee1 = profile.Callee1;
                        }
                        if (IsSelfCallee(callee0, chunk) || IsSelfCallee(callee1, chunk))
                        {
                            declineReason = "self tail-recursion (relies on the interpreter trampoline)";
                            return null;
                        }
                        instrs.Add(JitInstruction.Call(argc, callee0, callee1));
                        instrs.Add(JitInstruction.Return());
                        ip += 4;
                        break;
                    }

                    // Object & array literals. Computed keys (SetPropDynamic),
                    // spread, getters/setters are separate opcodes left declined.
                    case OpCode.NewObject:
                        instrs.Add(JitInstruction.NewObject());
                        break;
                    case OpCode.NewArray:
                        instrs.Add(JitInstruction.NewArray());
                        break;
                    case OpCode.InitProp:
                        instrs.Add(JitInstruction.InitProp(chunk.Names[chunk.ReadInt(ip)]));
                        ip += 4;
                        break;
                    case OpCode.InitPropN:
                        instrs.Add(JitInstruction.InitProp(chunk.Names[code[ip]]));
                        ip += 1;
                        break;
                    case OpCode.InitElem:
                        instrs.Add(JitInstruction.InitElem(chunk.ReadInt(ip)));
                        ip += 4;
                        break;

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
                    case OpCode.JumpIfFalseOrPop:
                        jumpIndices.Add(instrs.Count);
                        instrs.Add(JitInstruction.JumpIfFalseOrPop(chunk.ReadInt(ip)));
                        ip += 4;
                        break;
                    case OpCode.JumpIfTrueOrPop:
                        jumpIndices.Add(instrs.Count);
                        instrs.Add(JitInstruction.JumpIfTrueOrPop(chunk.ReadInt(ip)));
                        ip += 4;
                        break;
                    case OpCode.JumpIfNullOrUndefined:
                        jumpIndices.Add(instrs.Count);
                        instrs.Add(JitInstruction.JumpIfNullOrUndefined(chunk.ReadInt(ip)));
                        ip += 4;
                        break;
                    case OpCode.JumpIfDefined:
                        jumpIndices.Add(instrs.Count);
                        instrs.Add(JitInstruction.JumpIfDefined(chunk.ReadInt(ip)));
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

                    // Constructor calls, object/array spread.
                    case OpCode.New:
                        instrs.Add(JitInstruction.New(chunk.ReadInt(ip)));
                        ip += 4;
                        break;
                    case OpCode.MergeObject:
                        instrs.Add(JitInstruction.MergeObject());
                        break;
                    case OpCode.InitPropOverwrite:
                        instrs.Add(JitInstruction.InitPropOverwrite(chunk.Names[chunk.ReadInt(ip)]));
                        ip += 4;
                        break;
                    case OpCode.AppendElem:
                        instrs.Add(JitInstruction.AppendElem());
                        break;

                    default:
                        // Conditional-pop jumps, for..of, object/array literals,
                        // try, tail and method calls, etc. — not supported.
                        declineReason = "unsupported opcode: " + op;
                        return null;
                }

                // Record the source offset for every instruction emitted this
                // iteration so jump targets (bytecode offsets) can be mapped to indices.
                for (var k = firstIndex; k < instrs.Count; k++)
                    offsets.Add(opOffset);
            }

            // Resolve jump targets (bytecode offsets) to instruction indices.
            var map = new Dictionary<int, int>(offsets.Count);
            for (var i = 0; i < offsets.Count; i++)
                if (!map.ContainsKey(offsets[i]))
                    map[offsets[i]] = i;

            foreach (var ji in jumpIndices)
            {
                if (!map.TryGetValue(instrs[ji].IntValue, out var targetIndex))
                {
                    declineReason = "unresolvable jump target";
                    return null; // target not at an instruction boundary we model
                }
                instrs[ji] = instrs[ji].Kind switch
                {
                    JitOpKind.Jump                  => JitInstruction.Jump(targetIndex),
                    JitOpKind.JumpIfFalse           => JitInstruction.JumpIfFalse(targetIndex),
                    JitOpKind.JumpIfTrue            => JitInstruction.JumpIfTrue(targetIndex),
                    JitOpKind.JumpIfFalseOrPop      => JitInstruction.JumpIfFalseOrPop(targetIndex),
                    JitOpKind.JumpIfTrueOrPop       => JitInstruction.JumpIfTrueOrPop(targetIndex),
                    JitOpKind.JumpIfNullOrUndefined => JitInstruction.JumpIfNullOrUndefined(targetIndex),
                    _                               => JitInstruction.JumpIfDefined(targetIndex),
                };
            }

            offsetToIndex = map;
            return instrs;
        }

        /// <summary>True if <paramref name="callee"/> is a VM function whose body is
        /// <paramref name="chunk"/> itself — i.e. a self (tail-)recursive call.</summary>
        private static bool IsSelfCallee(ScriptVar callee, Chunk chunk)
        {
            if (callee == null || !callee.IsFunction || callee.IsNative)
                return false;
            return callee.GetData() is VmFunction fn && fn.Body == chunk;
        }
    }
}
