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
using System.Reflection.Emit;
using DScript.Vm;

namespace DScript.Jit
{
    /// <summary>
    /// A first-tier JIT back-end built on <see cref="System.Reflection.Emit"/>.
    ///
    /// It consumes the normalised instruction stream from the shared
    /// <see cref="JitDecoder"/> (which decides eligibility and declines control flow,
    /// assignments, generators, etc.) and lowers each instruction to IL: constant
    /// loads, variable reads (resolved through the full lexical scope chain),
    /// arithmetic, and plain calls. Because the emitted code mirrors the interpreter's
    /// operand semantics (its int fast path, double/string specialisations, and
    /// <c>MathsOp</c> fallback) the JIT output is value-identical to interpretation.
    /// </summary>
    public sealed class ReflectionEmitJitCompiler : IJitCompiler
    {
        public JitDelegate Compile(Chunk chunk)
        {
            var instrs = JitDecoder.Decode(chunk);
            if (instrs == null)
                return null; // declined by the shared front-end

            // Speculative unboxed-int tier first (unless repeated deopts proved it
            // unprofitable), then the conservative boxed tier.
            if (!chunk.PreferConservativeTier)
            {
                var asInt = TryCompileSpeculativeInt(chunk, instrs);
                if (asInt != null) return asInt;
                var asDouble = TryCompileSpeculativeDouble(chunk, instrs);
                if (asDouble != null) return asDouble;
                var asIntLoop = TryCompileSpeculativeIntLoop(chunk, instrs);
                if (asIntLoop != null) return asIntLoop;
            }

            return CompileConservative(chunk, instrs);
        }

        // ── speculative unboxed-int tier ─────────────────────────────────────────
        // For a pure (call-free), straight-line function whose binary sites are all
        // profiled Int-only, compile a body where values flow as raw int through IL —
        // no per-op ScriptVar allocation, no MathsOp. Variables are type-guarded once
        // in the prologue (and their .Int cached in locals), so the body is guard-free
        // and a guard miss deopts with a clean stack. Returns null if not eligible.
        private static JitDelegate TryCompileSpeculativeInt(Chunk chunk, List<JitInstruction> instrs)
        {
            if (!IsIntSpeculable(chunk, instrs))
                return null;

            var b = new DynamicMethodBuilder(chunk.Name ?? "anon");
            var svTemp = b.DeclareLocal(typeof(ScriptVar));
            var deopt = b.IL.DefineLabel();
            var chunkIndex = b.AddData(chunk);

            // Prologue: resolve + int-guard each distinct variable once, caching its
            // raw int value in a local. The body then reads locals, never re-resolving.
            var varLocals = new Dictionary<string, LocalBuilder>();
            foreach (var instr in instrs)
            {
                if (instr.Kind != JitOpKind.PushVar || varLocals.ContainsKey(instr.Name)) continue;
                var local = b.DeclareLocal(typeof(int));
                varLocals[instr.Name] = local;
                b.EmitResolveGuardedInt(instr.Name, local, svTemp, deopt);
            }

            // Body: raw int value flow.
            foreach (var instr in instrs)
            {
                switch (instr.Kind)
                {
                    case JitOpKind.PushConst:      b.EmitLdcI4(instr.Constant.IntValue); break;
                    case JitOpKind.PushIntLiteral: b.EmitLdcI4(instr.IntValue); break;
                    case JitOpKind.PushVar:        b.EmitLoadLocal(varLocals[instr.Name]); break;
                    case JitOpKind.Not:
                        b.IL.Emit(OpCodes.Ldc_I4_0);
                        b.IL.Emit(OpCodes.Ceq);     // x == 0  → !x
                        break;
                    case JitOpKind.Binary:         EmitIntBinaryRaw(b, instr.Op); break;
                    case JitOpKind.Pop:            b.IL.Emit(OpCodes.Pop); break;
                    case JitOpKind.Return:
                        b.EmitFromInt();            // box raw int at the boundary
                        b.IL.Emit(OpCodes.Ret);
                        break;
                }
            }

            b.EmitDeoptReturn(deopt, chunkIndex);
            return b.Finish(appendRet: false);
        }

        // Eligibility for the speculative int tier.
        private static bool IsIntSpeculable(Chunk chunk, List<JitInstruction> instrs)
        {
            if (instrs.Count == 0 || instrs[instrs.Count - 1].Kind != JitOpKind.Return)
                return false;

            foreach (var instr in instrs)
            {
                switch (instr.Kind)
                {
                    case JitOpKind.Call:
                    case JitOpKind.GetProp:
                    case JitOpKind.PushNull:
                    case JitOpKind.PushUndefined:
                    case JitOpKind.Jump:
                    case JitOpKind.JumpIfFalse:
                    case JitOpKind.JumpIfTrue:
                    case JitOpKind.JumpIfFalseOrPop:
                    case JitOpKind.JumpIfTrueOrPop:
                    case JitOpKind.JumpIfNullOrUndefined:
                    case JitOpKind.JumpIfDefined:
                    case JitOpKind.GetPropMethod:
                    case JitOpKind.GetPropCall0:
                    case JitOpKind.CallMethod:
                    case JitOpKind.SetVar:
                    case JitOpKind.SetVarPop:
                    case JitOpKind.SetProp:
                    case JitOpKind.SetPropPop:
                    case JitOpKind.DeclareVar:
                    case JitOpKind.DeclareLocal:
                    case JitOpKind.DeclareConst:
                    case JitOpKind.EnterBlock:
                    case JitOpKind.LeaveBlock:
                    case JitOpKind.NewObject:
                    case JitOpKind.NewArray:
                    case JitOpKind.InitProp:
                    case JitOpKind.InitElem:
                    case JitOpKind.GetIndex:
                    case JitOpKind.SetIndex:
                    case JitOpKind.Negate:
                    case JitOpKind.BitNot:
                    case JitOpKind.Typeof:
                    case JitOpKind.ToNumber:
                    case JitOpKind.Shift:
                        return false; // not pure / not int-typed / control flow / mutation
                    case JitOpKind.PushConst:
                        if (instr.Constant.Kind != ConstantKind.Int) return false;
                        break;
                    case JitOpKind.Binary:
                        if (InlineIntOp(instr.Op) == null && !IsIntComparison(instr.Op)) return false;
                        break;
                }
            }

            // Require evidence of integer arithmetic: at least one binary site, all
            // observed as Int-only. (Avoids speculating int on, e.g., string identity
            // functions that would only ever deopt.)
            var profiles = chunk.GetBinaryOpProfiles();
            if (profiles.Count == 0) return false;
            foreach (var (_, p) in profiles)
                if (p.LeftTypes != Chunk.BinaryTypeFlags.Int || p.RightTypes != Chunk.BinaryTypeFlags.Int)
                    return false;

            return true;
        }

        // Raw-int binary op over two ints on the IL stack. Arithmetic/bitwise are
        // inlined; comparisons emit 0/1 matching IntBinary's boolean results.
        private static void EmitIntBinaryRaw(DynamicMethodBuilder b, ScriptLex.LexTypes op)
        {
            var inline = InlineIntOp(op);
            if (inline.HasValue) { b.IL.Emit(inline.Value); return; }

            var il = b.IL;
            switch (op)
            {
                case (ScriptLex.LexTypes)'<':       il.Emit(OpCodes.Clt); break;
                case (ScriptLex.LexTypes)'>':       il.Emit(OpCodes.Cgt); break;
                case ScriptLex.LexTypes.LEqual:     il.Emit(OpCodes.Cgt); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case ScriptLex.LexTypes.GEqual:     il.Emit(OpCodes.Clt); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                case ScriptLex.LexTypes.Equal:      il.Emit(OpCodes.Ceq); break;
                case ScriptLex.LexTypes.NEqual:     il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
            }
        }

        private static bool IsIntComparison(ScriptLex.LexTypes op) =>
            (char)op == '<' || (char)op == '>' ||
            op == ScriptLex.LexTypes.LEqual || op == ScriptLex.LexTypes.GEqual ||
            op == ScriptLex.LexTypes.Equal || op == ScriptLex.LexTypes.NEqual;

        // ── speculative unboxed-int LOOP tier ────────────────────────────────────
        // For a pure (call-free) function whose binary sites are all profiled Int-only,
        // compile control flow + assignments with variables promoted to raw-int IL
        // registers — no per-iteration boxing. Because every value that flows is
        // provably int (int constants, int-guarded params, int arithmetic, 0/1
        // comparisons), the only guards are the entry parameter guards (clean-stack
        // deopt); the loop body runs fully unboxed and boxes only at the return.
        //
        // Soundness of register promotion: a parameter is guarded int at entry; a local
        // is promoted only when its first reference is an assignment in the straight-line
        // prologue (before any jump), so it is unconditionally initialised before any
        // read — its initialiser dominates all uses. Otherwise the chunk is declined.
        private static JitDelegate TryCompileSpeculativeIntLoop(Chunk chunk, List<JitInstruction> instrs)
        {
            if (!IsIntLoopSpeculable(chunk, instrs))
                return null;

            var b = new DynamicMethodBuilder(chunk.Name ?? "anon");
            var svTemp = b.DeclareLocal(typeof(ScriptVar));
            var deopt = b.IL.DefineLabel();
            var chunkIndex = b.AddData(chunk);
            var il = b.IL;

            // One raw-int register per variable.
            var regs = new Dictionary<string, LocalBuilder>();
            foreach (var instr in instrs)
                if (instr.Name != null && IsVarRef(instr.Kind) && !regs.ContainsKey(instr.Name))
                    regs[instr.Name] = b.DeclareLocal(typeof(int));

            // Prologue: guard each parameter int and load it into its register. Locals
            // are left at the IL default (0) and set by their prologue assignment before
            // any read (guaranteed by eligibility), so they need no entry guard.
            foreach (var kv in regs)
                if (chunk.Parameters.Contains(kv.Key))
                    b.EmitResolveGuardedInt(kv.Key, kv.Value, svTemp, deopt);

            var labels = new Dictionary<int, Label>();
            foreach (var instr in instrs)
                if (IsJump(instr.Kind))
                    labels[instr.IntValue] = il.DefineLabel();

            for (var i = 0; i < instrs.Count; i++)
            {
                if (labels.TryGetValue(i, out var here)) il.MarkLabel(here);
                var instr = instrs[i];
                switch (instr.Kind)
                {
                    case JitOpKind.PushConst:      b.EmitLdcI4(instr.Constant.IntValue); break;
                    case JitOpKind.PushIntLiteral: b.EmitLdcI4(instr.IntValue); break;
                    case JitOpKind.PushVar:        b.EmitLoadLocal(regs[instr.Name]); break;
                    case JitOpKind.SetVar:         il.Emit(OpCodes.Dup); b.EmitStoreLocal(regs[instr.Name]); break; // expression
                    case JitOpKind.SetVarPop:      b.EmitStoreLocal(regs[instr.Name]); break;
                    case JitOpKind.DeclareVar:
                    case JitOpKind.DeclareLocal:
                    case JitOpKind.DeclareConst:   break; // register already exists
                    case JitOpKind.Not:            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                    case JitOpKind.Binary:         EmitIntBinaryRaw(b, instr.Op); break;
                    case JitOpKind.Jump:           il.Emit(OpCodes.Br, labels[instr.IntValue]); break;
                    case JitOpKind.JumpIfFalse:    il.Emit(OpCodes.Brfalse, labels[instr.IntValue]); break; // raw int condition
                    case JitOpKind.JumpIfTrue:     il.Emit(OpCodes.Brtrue, labels[instr.IntValue]); break;
                    case JitOpKind.Return:         b.EmitFromInt(); il.Emit(OpCodes.Ret); break;
                }
            }

            b.EmitDeoptReturn(deopt, chunkIndex);
            return b.Finish(appendRet: false);
        }

        private static bool IsVarRef(JitOpKind k) =>
            k is JitOpKind.PushVar or JitOpKind.SetVar or JitOpKind.SetVarPop;

        private static bool IsJump(JitOpKind k) =>
            k is JitOpKind.Jump or JitOpKind.JumpIfFalse or JitOpKind.JumpIfTrue;

        private static bool IsIntLoopSpeculable(Chunk chunk, List<JitInstruction> instrs)
        {
            if (instrs.Count == 0 || instrs[instrs.Count - 1].Kind != JitOpKind.Return)
                return false;

            foreach (var instr in instrs)
            {
                switch (instr.Kind)
                {
                    case JitOpKind.PushConst:
                        if (instr.Constant.Kind != ConstantKind.Int) return false;
                        break;
                    case JitOpKind.Binary:
                        if (InlineIntOp(instr.Op) == null && !IsIntComparison(instr.Op)) return false; // no /,%, etc.
                        break;
                    case JitOpKind.PushIntLiteral:
                    case JitOpKind.PushVar:
                    case JitOpKind.SetVar:
                    case JitOpKind.SetVarPop:
                    case JitOpKind.DeclareVar:
                    case JitOpKind.DeclareLocal:
                    case JitOpKind.DeclareConst:
                    case JitOpKind.Not:
                    case JitOpKind.Jump:
                    case JitOpKind.JumpIfFalse:
                    case JitOpKind.JumpIfTrue:
                    case JitOpKind.Return:
                        break; // EnterBlock/LeaveBlock deliberately absent — block scopes go to the conservative tier
                    default:
                        return false; // calls, props, indexing, conditional-pop jumps, shifts, etc.
                }
            }

            // Evidence of integer arithmetic: at least one binary site, all Int-only.
            var profiles = chunk.GetBinaryOpProfiles();
            if (profiles.Count == 0) return false;
            foreach (var (_, p) in profiles)
                if (p.LeftTypes != Chunk.BinaryTypeFlags.Int || p.RightTypes != Chunk.BinaryTypeFlags.Int)
                    return false;

            // Register-promotion soundness: every non-parameter variable's first
            // reference must be an assignment in the straight-line prologue (before any
            // jump), so it is initialised unconditionally before any read.
            var firstJump = instrs.Count;
            for (var i = 0; i < instrs.Count; i++)
                if (IsJump(instrs[i].Kind)) { firstJump = i; break; }

            var firstRef = new Dictionary<string, (int index, bool isWrite)>();
            for (var i = 0; i < instrs.Count; i++)
            {
                var instr = instrs[i];
                if (instr.Name == null || !IsVarRef(instr.Kind)) continue;
                if (firstRef.ContainsKey(instr.Name)) continue;
                firstRef[instr.Name] = (i, instr.Kind is JitOpKind.SetVar or JitOpKind.SetVarPop);
            }

            foreach (var kv in firstRef)
            {
                if (chunk.Parameters.Contains(kv.Key)) continue;     // params guarded at entry
                if (!kv.Value.isWrite || kv.Value.index >= firstJump) return false; // not unconditionally pre-assigned
            }

            return true;
        }

        // ── speculative unboxed-double tier ──────────────────────────────────────
        // For a pure, straight-line function whose binary sites are numeric with at
        // least one double, compile a body where values flow as raw double through IL
        // (+,-,*,/ only; division-by-zero yields Infinity/NaN, matching MathsOp, so no
        // deopt is needed for it). Variables are guarded numeric once in the prologue
        // and cached as double. Returns null if not eligible.
        private static JitDelegate TryCompileSpeculativeDouble(Chunk chunk, List<JitInstruction> instrs)
        {
            if (!IsDoubleSpeculable(chunk, instrs))
                return null;

            var b = new DynamicMethodBuilder(chunk.Name ?? "anon");
            var svTemp = b.DeclareLocal(typeof(ScriptVar));
            var deopt = b.IL.DefineLabel();
            var chunkIndex = b.AddData(chunk);

            var varLocals = new Dictionary<string, LocalBuilder>();
            foreach (var instr in instrs)
            {
                if (instr.Kind != JitOpKind.PushVar || varLocals.ContainsKey(instr.Name)) continue;
                var local = b.DeclareLocal(typeof(double));
                varLocals[instr.Name] = local;
                b.EmitResolveGuardedDouble(instr.Name, local, svTemp, deopt);
            }

            foreach (var instr in instrs)
            {
                switch (instr.Kind)
                {
                    case JitOpKind.PushConst:
                        if (instr.Constant.Kind == ConstantKind.Double)
                            b.EmitLdcR8(instr.Constant.DoubleValue);
                        else { b.EmitLdcI4(instr.Constant.IntValue); b.EmitConvR8(); }
                        break;
                    case JitOpKind.PushIntLiteral: b.EmitLdcI4(instr.IntValue); b.EmitConvR8(); break;
                    case JitOpKind.PushVar:        b.EmitLoadLocal(varLocals[instr.Name]); break;
                    case JitOpKind.Binary:         b.IL.Emit(DoubleArithOp(instr.Op).Value); break;
                    case JitOpKind.Pop:            b.IL.Emit(OpCodes.Pop); break;
                    case JitOpKind.Return:
                        b.EmitFromDouble();         // box raw double at the boundary
                        b.IL.Emit(OpCodes.Ret);
                        break;
                }
            }

            b.EmitDeoptReturn(deopt, chunkIndex);
            return b.Finish(appendRet: false);
        }

        private static bool IsDoubleSpeculable(Chunk chunk, List<JitInstruction> instrs)
        {
            if (instrs.Count == 0 || instrs[instrs.Count - 1].Kind != JitOpKind.Return)
                return false;

            foreach (var instr in instrs)
            {
                switch (instr.Kind)
                {
                    case JitOpKind.Call:
                    case JitOpKind.GetProp:
                    case JitOpKind.PushNull:
                    case JitOpKind.PushUndefined:
                    case JitOpKind.Not:
                    case JitOpKind.Jump:
                    case JitOpKind.JumpIfFalse:
                    case JitOpKind.JumpIfTrue:
                    case JitOpKind.JumpIfFalseOrPop:
                    case JitOpKind.JumpIfTrueOrPop:
                    case JitOpKind.JumpIfNullOrUndefined:
                    case JitOpKind.JumpIfDefined:
                    case JitOpKind.GetPropMethod:
                    case JitOpKind.GetPropCall0:
                    case JitOpKind.CallMethod:
                    case JitOpKind.SetVar:
                    case JitOpKind.SetVarPop:
                    case JitOpKind.SetProp:
                    case JitOpKind.SetPropPop:
                    case JitOpKind.DeclareVar:
                    case JitOpKind.DeclareLocal:
                    case JitOpKind.DeclareConst:
                    case JitOpKind.EnterBlock:
                    case JitOpKind.LeaveBlock:
                    case JitOpKind.NewObject:
                    case JitOpKind.NewArray:
                    case JitOpKind.InitProp:
                    case JitOpKind.InitElem:
                    case JitOpKind.GetIndex:
                    case JitOpKind.SetIndex:
                    case JitOpKind.Negate:
                    case JitOpKind.BitNot:
                    case JitOpKind.Typeof:
                    case JitOpKind.ToNumber:
                    case JitOpKind.Shift:
                        return false; // not pure / non-double / control flow / mutation
                    case JitOpKind.PushConst:
                        if (instr.Constant.Kind != ConstantKind.Int && instr.Constant.Kind != ConstantKind.Double)
                            return false;
                        break;
                    case JitOpKind.Binary:
                        if (DoubleArithOp(instr.Op) == null) return false; // only +,-,*,/
                        break;
                }
            }

            // All numeric sites (no string/other), with at least one double observed —
            // pure-int functions are already handled by the int tier (tried first).
            var profiles = chunk.GetBinaryOpProfiles();
            if (profiles.Count == 0) return false;
            var sawDouble = false;
            foreach (var (_, p) in profiles)
            {
                const Chunk.BinaryTypeFlags numeric = Chunk.BinaryTypeFlags.Int | Chunk.BinaryTypeFlags.Double;
                if ((p.LeftTypes & ~numeric) != 0 || (p.RightTypes & ~numeric) != 0) return false;
                if (p.LeftTypes == Chunk.BinaryTypeFlags.None || p.RightTypes == Chunk.BinaryTypeFlags.None) return false;
                if ((p.LeftTypes & Chunk.BinaryTypeFlags.Double) != 0 || (p.RightTypes & Chunk.BinaryTypeFlags.Double) != 0)
                    sawDouble = true;
            }
            return sawDouble;
        }

        private static JitDelegate CompileConservative(Chunk chunk, List<JitInstruction> instrs)
        {
            // Verify operand-stack depth is consistent at every branch edge; decline
            // if not (guards against emitting invalid IL). For the structured jumps we
            // decode this always holds, but the check is cheap insurance.
            if (!VerifyStackConsistency(instrs))
                return null;

            var b = new DynamicMethodBuilder(chunk.Name ?? "anon");

            // Two scratch slots for binary operands and one for IntBinary's out value.
            // Each binary op fully consumes them before the next, so they are reused.
            var aSlot = b.DeclareLocal(typeof(ScriptVar));
            var bSlot = b.DeclareLocal(typeof(ScriptVar));
            var rSlot = b.DeclareLocal(typeof(ScriptVar));
            var argArr = b.DeclareLocal(typeof(ScriptVar[]));

            // Block scopes: track the active environment in a local that variable ops
            // resolve against (EnterBlock/LeaveBlock swap it). Only set up when the
            // chunk actually has blocks, so non-block chunks keep using the env argument.
            LocalBuilder currentEnv = null;
            foreach (var instr in instrs)
                if (instr.Kind == JitOpKind.EnterBlock)
                {
                    currentEnv = b.DeclareLocal(typeof(Environment));
                    b.EmitLoadEnv();                 // ldarg env (CurrentEnvLocal still null)
                    b.EmitStoreLocal(currentEnv);
                    b.CurrentEnvLocal = currentEnv;  // subsequent var ops use the local
                    break;
                }

            // One IL label per jump-target instruction index, marked before that
            // instruction is emitted.
            var labels = new Dictionary<int, Label>();
            foreach (var instr in instrs)
                if (instr.Kind is JitOpKind.Jump or JitOpKind.JumpIfFalse or JitOpKind.JumpIfTrue
                    or JitOpKind.JumpIfFalseOrPop or JitOpKind.JumpIfTrueOrPop
                    or JitOpKind.JumpIfNullOrUndefined or JitOpKind.JumpIfDefined)
                    labels[instr.IntValue] = b.IL.DefineLabel();

            var lastWasReturn = false;
            for (var i = 0; i < instrs.Count; i++)
            {
                if (labels.TryGetValue(i, out var here))
                    b.IL.MarkLabel(here);

                var instr = instrs[i];
                lastWasReturn = false;
                switch (instr.Kind)
                {
                    case JitOpKind.PushConst:     b.EmitMaterializeConstant(instr.Constant); break;
                    case JitOpKind.PushIntLiteral: b.EmitPushIntConst(instr.IntValue); break;
                    case JitOpKind.PushVar:       b.EmitLoadNamedVar(instr.Name); break;
                    case JitOpKind.GetProp:       b.EmitGetProp(instr.Name, aSlot); break;
                    case JitOpKind.SetVar:        b.EmitSetVar(instr.Name, chunk.IsStrict, leaveValue: true,  valTemp: aSlot); break;
                    case JitOpKind.SetVarPop:     b.EmitSetVar(instr.Name, chunk.IsStrict, leaveValue: false, valTemp: aSlot); break;
                    case JitOpKind.SetProp:       b.EmitSetProp(instr.Name, chunk.IsStrict, leaveValue: true,  valTemp: aSlot, objTemp: bSlot); break;
                    case JitOpKind.SetPropPop:    b.EmitSetProp(instr.Name, chunk.IsStrict, leaveValue: false, valTemp: aSlot, objTemp: bSlot); break;
                    case JitOpKind.DeclareVar:    b.EmitDeclare(instr.Name, JitDeclareKind.Var); break;
                    case JitOpKind.DeclareLocal:  b.EmitDeclare(instr.Name, JitDeclareKind.Local); break;
                    case JitOpKind.DeclareConst:  b.EmitDeclare(instr.Name, JitDeclareKind.Const); break;
                    case JitOpKind.EnterBlock:    b.EmitEnterBlock(currentEnv); break;
                    case JitOpKind.LeaveBlock:    b.EmitLeaveBlock(currentEnv); break;
                    case JitOpKind.NewObject:     b.EmitNewObject(); break;
                    case JitOpKind.NewArray:      b.EmitNewArray(); break;
                    case JitOpKind.InitProp:      b.EmitInitProp(instr.Name); break;
                    case JitOpKind.InitElem:      b.EmitInitElem(instr.IntValue); break;
                    case JitOpKind.PushUndefined: b.EmitPushUndefined(); break;
                    case JitOpKind.PushNull:      b.EmitPushNull(); break;
                    case JitOpKind.Pop:           b.IL.Emit(OpCodes.Pop); break;
                    case JitOpKind.Not:           b.EmitLogicalNot(); break;
                    case JitOpKind.GetIndex:      b.EmitGetIndex(aSlot, bSlot); break;
                    case JitOpKind.SetIndex:      b.EmitSetIndex(chunk.IsStrict, leaveValue: true, valTmp: aSlot, keyTmp: bSlot, objTmp: rSlot); break;
                    case JitOpKind.Negate:        b.EmitNegate(); break;
                    case JitOpKind.BitNot:        b.EmitBitNot(); break;
                    case JitOpKind.Typeof:        b.EmitTypeof(); break;
                    case JitOpKind.ToNumber:      b.EmitToNumber(); break;
                    case JitOpKind.Shift:         b.EmitShift(instr.Op); break;
                    case JitOpKind.Binary:        EmitBinary(b, instr.Op, aSlot, bSlot, rSlot); break;
                    case JitOpKind.Call:          EmitCall(b, instr.MonoCallee, instr.MonoCallee1, instr.IntValue, aSlot, bSlot, argArr, rSlot); break;
                    case JitOpKind.GetPropMethod: b.IL.Emit(OpCodes.Dup); b.EmitGetProp(instr.Name, aSlot); break; // keep receiver, push method
                    case JitOpKind.GetPropCall0:  b.EmitGetPropCall0(instr.Name, aSlot); break;
                    case JitOpKind.CallMethod:    b.EmitCallMethod(instr.IntValue, aSlot, bSlot, rSlot, argArr); break;
                    case JitOpKind.Jump:
                        b.IL.Emit(OpCodes.Br, labels[instr.IntValue]);
                        break;
                    case JitOpKind.JumpIfFalse:
                        b.EmitToBool();                                  // pop condition -> bool
                        b.IL.Emit(OpCodes.Brfalse, labels[instr.IntValue]);
                        break;
                    case JitOpKind.JumpIfTrue:
                        b.EmitToBool();
                        b.IL.Emit(OpCodes.Brtrue, labels[instr.IntValue]);
                        break;
                    case JitOpKind.JumpIfFalseOrPop:
                        // && : keep the operand and branch if falsy, else pop it.
                        b.IL.Emit(OpCodes.Dup);
                        b.EmitToBool();
                        b.IL.Emit(OpCodes.Brfalse, labels[instr.IntValue]);
                        b.IL.Emit(OpCodes.Pop);
                        break;
                    case JitOpKind.JumpIfTrueOrPop:
                        // || : keep the operand and branch if truthy, else pop it.
                        b.IL.Emit(OpCodes.Dup);
                        b.EmitToBool();
                        b.IL.Emit(OpCodes.Brtrue, labels[instr.IntValue]);
                        b.IL.Emit(OpCodes.Pop);
                        break;
                    case JitOpKind.JumpIfDefined:
                        // optional chaining: pop value, branch if it is not undefined.
                        b.EmitIsUndefined();
                        b.IL.Emit(OpCodes.Brfalse, labels[instr.IntValue]);
                        break;
                    case JitOpKind.JumpIfNullOrUndefined:
                        EmitJumpIfNullOrUndefined(b, aSlot, labels[instr.IntValue]);
                        break;
                    case JitOpKind.Return:
                        b.IL.Emit(OpCodes.Ret);   // returns the ScriptVar on top of stack
                        lastWasReturn = true;
                        break;
                }
            }

            if (!lastWasReturn)
                b.EmitPushUndefined(); // fall-through: function returns undefined

            return b.Finish();
        }

        // `??` : pop the value; if it is null/undefined push undefined and branch to
        // `target`, otherwise push the value back and fall through. Mirrors the
        // JumpIfNullOrUndefined opcode. valTmp is a scratch ScriptVar local.
        private static void EmitJumpIfNullOrUndefined(DynamicMethodBuilder b, LocalBuilder valTmp,
                                                      System.Reflection.Emit.Label target)
        {
            var il = b.IL;
            var nullish = il.DefineLabel();
            var fallThrough = il.DefineLabel();

            b.EmitStoreLocal(valTmp);                       // pop value
            b.EmitLoadLocal(valTmp); b.EmitIsNull();      il.Emit(OpCodes.Brtrue, nullish);
            b.EmitLoadLocal(valTmp); b.EmitIsUndefined(); il.Emit(OpCodes.Brtrue, nullish);

            b.EmitLoadLocal(valTmp);                        // not nullish: push value, fall through
            il.Emit(OpCodes.Br, fallThrough);

            il.MarkLabel(nullish);
            b.EmitPushUndefined();                          // push undefined, take the branch
            il.Emit(OpCodes.Br, target);

            il.MarkLabel(fallThrough);
        }

        // Abstract-interpret the operand-stack depth across all branch edges; return
        // false on any inconsistency or underflow (in which case the chunk is declined
        // rather than risk invalid IL).
        private static bool VerifyStackConsistency(List<JitInstruction> instrs)
        {
            var n = instrs.Count;
            var depth = new int[n];
            for (var i = 0; i < n; i++) depth[i] = -1;

            var work = new Stack<(int index, int d)>();
            work.Push((0, 0));
            while (work.Count > 0)
            {
                var (i, d) = work.Pop();
                if (i < 0 || i > n) return false;
                if (i == n) { if (d != 0) return false; continue; } // fell off the end
                if (depth[i] != -1) { if (depth[i] != d) return false; continue; }
                depth[i] = d;

                var instr = instrs[i];

                // Jumps can have different stack effects on the taken edge vs the
                // fall-through edge (the conditional-pop variants keep the operand on
                // one edge and drop it on the other).
                switch (instr.Kind)
                {
                    case JitOpKind.Jump:
                        if (d < 0) return false;
                        work.Push((instr.IntValue, d));      // unconditional, no effect
                        break;
                    case JitOpKind.JumpIfFalse:
                    case JitOpKind.JumpIfTrue:
                        if (d - 1 < 0) return false;          // pops the condition on both edges
                        work.Push((instr.IntValue, d - 1));
                        work.Push((i + 1, d - 1));
                        break;
                    case JitOpKind.JumpIfFalseOrPop:
                    case JitOpKind.JumpIfTrueOrPop:
                        if (d - 1 < 0) return false;
                        work.Push((instr.IntValue, d));       // branch keeps the operand
                        work.Push((i + 1, d - 1));            // fall-through pops it
                        break;
                    case JitOpKind.JumpIfNullOrUndefined:
                        if (d - 1 < 0) return false;          // pops, then pushes on both edges
                        work.Push((instr.IntValue, d));
                        work.Push((i + 1, d));
                        break;
                    case JitOpKind.JumpIfDefined:
                        if (d - 1 < 0) return false;          // pops the value on both edges
                        work.Push((instr.IntValue, d - 1));
                        work.Push((i + 1, d - 1));
                        break;
                    case JitOpKind.Return:
                        if (d + StackEffect(instr) < 0) return false; // terminal
                        break;
                    default:
                    {
                        var after = d + StackEffect(instr);
                        if (after < 0) return false;
                        work.Push((i + 1, after));
                        break;
                    }
                }
            }
            return true;
        }

        // Net operand-stack effect of an instruction.
        private static int StackEffect(JitInstruction instr) => instr.Kind switch
        {
            JitOpKind.PushConst or JitOpKind.PushIntLiteral or JitOpKind.PushVar
                or JitOpKind.PushNull or JitOpKind.PushUndefined
                or JitOpKind.NewObject or JitOpKind.NewArray => 1,
            JitOpKind.InitProp or JitOpKind.InitElem => -1,    // pop value, keep object/array
            JitOpKind.GetProp or JitOpKind.Not or JitOpKind.SetVar or JitOpKind.GetPropCall0
                or JitOpKind.Negate or JitOpKind.BitNot or JitOpKind.Typeof or JitOpKind.ToNumber => 0,
            JitOpKind.GetPropMethod => 1,                    // peek receiver (kept), push method
            JitOpKind.Binary or JitOpKind.SetProp or JitOpKind.GetIndex or JitOpKind.Shift => -1,
            JitOpKind.Call => -instr.IntValue,               // pop callee + argc, push result
            JitOpKind.CallMethod => -(instr.IntValue + 1),   // pop receiver + callee + argc, push result
            JitOpKind.Pop or JitOpKind.Return or JitOpKind.SetVarPop
                or JitOpKind.JumpIfFalse or JitOpKind.JumpIfTrue => -1,
            JitOpKind.SetPropPop or JitOpKind.SetIndex => -2,
            JitOpKind.Jump or JitOpKind.DeclareVar or JitOpKind.DeclareLocal or JitOpKind.DeclareConst
                or JitOpKind.EnterBlock or JitOpKind.LeaveBlock => 0,
            _ => 0,
        };

        // Emit a plain (non-tail) call. The callee and its argc arguments are on the
        // IL stack as [callee, arg0, ..., arg{argc-1}] (top = last arg), matching the
        // interpreter's Call layout. Tail and method calls are declined by the decoder.
        //
        // The decoder bakes the observed callee(s): one for a monomorphic site, two
        // for a bimorphic site. For each baked callee that is inline-eligible (a small
        // pure-parameter leaf), emit an identity guard that splices its body inline on
        // a match (no call frame allocated). Anything not inlined — a non-eligible
        // baked callee, a guard miss, or a megamorphic/unobserved site (both callees
        // null) — falls through to general dispatch on the runtime callee.
        private static void EmitCall(DynamicMethodBuilder b, ScriptVar callee0, ScriptVar callee1, int argc,
                                     LocalBuilder tmp, LocalBuilder calleeSlot, LocalBuilder argArr,
                                     LocalBuilder rSlot)
        {
            var il = b.IL;

            // Materialize the argument array from the stack (top-down), then the callee.
            b.EmitNewScriptVarArray(argc);
            b.EmitStoreLocal(argArr);
            for (var j = argc - 1; j >= 0; j--)
            {
                b.EmitStoreLocal(tmp);                 // pop one argument
                b.EmitLoadLocal(argArr);
                b.EmitLdcI4(j);
                b.EmitLoadLocal(tmp);
                b.EmitStoreElemRef();
            }
            b.EmitStoreLocal(calleeSlot);              // pop callee

            var done = il.DefineLabel();

            // One guarded inline path per inline-eligible baked callee (≤2). A callee
            // that isn't inline-eligible gets no guard — it would dispatch identically
            // to the general path, so the guard would be dead weight.
            EmitInlineGuard(b, callee0, argc, calleeSlot, argArr, tmp, rSlot, done);
            EmitInlineGuard(b, callee1, argc, calleeSlot, argArr, tmp, rSlot, done);

            // Fallback: dispatch on the runtime callee (handles misses + non-inlined).
            EmitGeneralCall(b, calleeSlot, argArr);
            il.MarkLabel(done);
        }

        // If <paramref name="callee"/> is inline-eligible, emit: guard runtimeCallee ==
        // callee; on a match splice the body inline and branch to <paramref name="done"/>;
        // on a miss fall through to whatever the caller emits next. No-op otherwise.
        private static void EmitInlineGuard(DynamicMethodBuilder b, ScriptVar callee, int argc,
                                            LocalBuilder calleeSlot, LocalBuilder argArr,
                                            LocalBuilder tmp, LocalBuilder rSlot, System.Reflection.Emit.Label done)
        {
            if (callee == null) return;
            if (!TryGetInlineBody(callee, argc, out var calleeChunk, out var body)) return;

            var il = b.IL;
            var skip = il.DefineLabel();
            b.EmitLoadLocal(calleeSlot);
            b.EmitLoadData(b.AddData(callee), typeof(ScriptVar));
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Brfalse, skip);

            // Guard hit: splice the callee body (clobbers calleeSlot, but this path
            // branches to done without reaching any later guard that reads it). Free
            // (global) reads resolve against the callee's captured defining scope.
            var captured = ((VmFunction)callee.GetData()).Captured;
            EmitInlinedBody(b, calleeChunk, body, captured, argArr, tmp, calleeSlot, rSlot);
            il.Emit(OpCodes.Br, done);

            il.MarkLabel(skip);
        }

        // Maximum inlined callee size (instructions), bounding code growth.
        private const int InlineBudget = 24;

        // A callee is inline-eligible when it is a small, mutation-free leaf function
        // (no calls, no assignments, no local declarations). It may contain control
        // flow (if/while branches) and may read its own parameters and free variables
        // (e.g. globals). Parameter reads are rewritten to the caller's argument array;
        // free-variable reads resolve against the function's captured defining scope —
        // so the spliced body needs no per-call environment of its own.
        private static bool TryGetInlineBody(ScriptVar callee, int argc, out Chunk calleeChunk,
                                             out List<JitInstruction> body)
        {
            calleeChunk = null;
            body = null;

            if (callee == null || !callee.IsFunction || callee.IsNative) return false;
            if (callee.GetData() is not VmFunction vmfn) return false;

            var c = vmfn.Body;
            if (c.MakesClosure || c.IsGenerator || c.IsAsync) return false;
            if (c.RestParamIndex != -1 || c.Parameters.Count != argc) return false;

            var instrs = JitDecoder.Decode(c);
            if (instrs == null || instrs.Count == 0 || instrs.Count > InlineBudget) return false;
            if (instrs[instrs.Count - 1].Kind != JitOpKind.Return) return false;

            foreach (var instr in instrs)
            {
                switch (instr.Kind)
                {
                    case JitOpKind.PushConst:
                    case JitOpKind.PushIntLiteral:
                    case JitOpKind.PushVar:        // param -> arg array; free var -> captured env
                    case JitOpKind.PushNull:
                    case JitOpKind.PushUndefined:
                    case JitOpKind.Not:
                    case JitOpKind.Binary:
                    case JitOpKind.GetProp:
                    case JitOpKind.Pop:
                    case JitOpKind.Return:
                    case JitOpKind.Jump:
                    case JitOpKind.JumpIfFalse:
                    case JitOpKind.JumpIfTrue:
                        break;
                    default:
                        // calls, assignments, declares, blocks, conditional-pop jumps,
                        // index/literal ops — not inlined (would need an env or be unsafe).
                        return false;
                }
            }

            // Control flow in the body must satisfy the same flat IL-stack model the
            // conservative tier verifies before we splice it.
            if (!VerifyStackConsistency(instrs)) return false;

            calleeChunk = c;
            body = instrs;
            return true;
        }

        // Splice a mutation-free leaf callee body inline. Parameter reads become reads
        // of the caller's argument array; free-variable reads resolve against the
        // callee's captured defining scope. Control flow uses a FRESH label set local
        // to this body, and every Return branches to a shared end label (the result is
        // on the IL stack there = the call result).
        private static void EmitInlinedBody(DynamicMethodBuilder b, Chunk calleeChunk,
                                            List<JitInstruction> body, Environment captured,
                                            LocalBuilder argArr,
                                            LocalBuilder aSlot, LocalBuilder bSlot, LocalBuilder rSlot)
        {
            var il = b.IL;
            var labels = new Dictionary<int, Label>();
            foreach (var instr in body)
                if (instr.Kind is JitOpKind.Jump or JitOpKind.JumpIfFalse or JitOpKind.JumpIfTrue)
                    labels[instr.IntValue] = il.DefineLabel();
            var end = il.DefineLabel();

            for (var i = 0; i < body.Count; i++)
            {
                if (labels.TryGetValue(i, out var here))
                    il.MarkLabel(here);

                var instr = body[i];
                switch (instr.Kind)
                {
                    case JitOpKind.PushConst:      b.EmitMaterializeConstant(instr.Constant); break;
                    case JitOpKind.PushIntLiteral: b.EmitPushIntConst(instr.IntValue); break;
                    case JitOpKind.PushVar:
                    {
                        var p = calleeChunk.Parameters.IndexOf(instr.Name);
                        if (p >= 0) b.EmitLoadArgElement(argArr, p);
                        else        b.EmitLoadNamedVarFrom(instr.Name, captured);
                        break;
                    }
                    case JitOpKind.PushNull:       b.EmitPushNull(); break;
                    case JitOpKind.PushUndefined:  b.EmitPushUndefined(); break;
                    case JitOpKind.Not:            b.EmitLogicalNot(); break;
                    case JitOpKind.Binary:         EmitBinary(b, instr.Op, aSlot, bSlot, rSlot); break;
                    case JitOpKind.GetProp:        b.EmitGetProp(instr.Name, aSlot); break;
                    case JitOpKind.Pop:            il.Emit(OpCodes.Pop); break;
                    case JitOpKind.Jump:           il.Emit(OpCodes.Br, labels[instr.IntValue]); break;
                    case JitOpKind.JumpIfFalse:    b.EmitToBool(); il.Emit(OpCodes.Brfalse, labels[instr.IntValue]); break;
                    case JitOpKind.JumpIfTrue:     b.EmitToBool(); il.Emit(OpCodes.Brtrue, labels[instr.IntValue]); break;
                    case JitOpKind.Return:         il.Emit(OpCodes.Br, end); break; // result on stack -> end
                }
            }

            il.MarkLabel(end);
        }

        // General dispatch: vm.InvokeCallable(callee, null, args).
        private static void EmitGeneralCall(DynamicMethodBuilder b, LocalBuilder calleeSlot, LocalBuilder argArr)
        {
            b.EmitLoadVm();
            b.EmitLoadLocal(calleeSlot);
            b.IL.Emit(OpCodes.Ldnull);                 // thisArg
            b.EmitLoadLocal(argArr);
            b.EmitInvokeCallable();
        }

        // Emit a binary operator over two operands already on the IL stack
        // (top = right, below = left). Mirrors the interpreter's Binary handler:
        // an int fast path (inline arithmetic where the operator is plain-int safe,
        // otherwise a call to VirtualMachine.IntBinary) with an a.MathsOp(b, op)
        // fallback whenever the operands are not both integers.
        private static void EmitBinary(DynamicMethodBuilder b, ScriptLex.LexTypes op,
                                       LocalBuilder aSlot, LocalBuilder bSlot, LocalBuilder rSlot)
        {
            var il = b.IL;
            b.EmitStoreLocal(bSlot); // right
            b.EmitStoreLocal(aSlot); // left

            var fallback = il.DefineLabel();
            var done = il.DefineLabel();

            // Specializations are tried in order — int, then double, then string —
            // each falling through to the next on a guard miss, and finally to the
            // generic MathsOp. Define entry labels only for the paths that apply.
            var dblOp = DoubleArithOp(op);
            var hasString = (char)op == '+';
            var doubleLabel = dblOp.HasValue ? il.DefineLabel() : default;
            var stringLabel = hasString ? il.DefineLabel() : default;

            Label afterInt = dblOp.HasValue ? doubleLabel : (hasString ? stringLabel : fallback);

            // Int fast path guard: both operands must be integers.
            b.EmitIsInt(aSlot);
            il.Emit(OpCodes.Brfalse, afterInt);
            b.EmitIsInt(bSlot);
            il.Emit(OpCodes.Brfalse, afterInt);

            var inlineOp = InlineIntOp(op);
            if (inlineOp.HasValue)
            {
                // result = ScriptVar.FromInt(a.Int <op> b.Int)
                b.EmitLoadInt(aSlot);
                b.EmitLoadInt(bSlot);
                il.Emit(inlineOp.Value);
                b.EmitFromInt();
            }
            else
            {
                // result = IntBinary(a.Int, b.Int, op, out r); falls back if not an int op.
                b.EmitIntBinaryCall(aSlot, bSlot, op, rSlot, fallback);
            }
            il.Emit(OpCodes.Br, done);

            // Double fast path: both operands numeric (and at least one a double).
            // result = ScriptVar.FromDouble(a.Float <op> b.Float). Mirrors MathsOp's
            // numeric promotion for +, -, *, /.
            if (dblOp.HasValue)
            {
                var afterDouble = hasString ? stringLabel : fallback;
                il.MarkLabel(doubleLabel);
                EmitNumericGuard(b, aSlot, afterDouble);
                EmitNumericGuard(b, bSlot, afterDouble);
                b.EmitLoadFloat(aSlot);
                b.EmitLoadFloat(bSlot);
                il.Emit(dblOp.Value);
                b.EmitFromDouble();
                il.Emit(OpCodes.Br, done);
            }

            // String fast path for '+': both operands strings.
            // result = ScriptVar.FromString(string.Concat(a.String, b.String)).
            // Mixed string/number concatenation needs ToString coercion, so it is
            // left to MathsOp.
            if (hasString)
            {
                il.MarkLabel(stringLabel);
                b.EmitIsString(aSlot);
                il.Emit(OpCodes.Brfalse, fallback);
                b.EmitIsString(bSlot);
                il.Emit(OpCodes.Brfalse, fallback);
                b.EmitLoadString(aSlot);
                b.EmitLoadString(bSlot);
                b.EmitStringConcat();
                b.EmitFromString();
                il.Emit(OpCodes.Br, done);
            }

            il.MarkLabel(fallback);
            b.EmitMathsOpFallback(aSlot, bSlot, op);

            il.MarkLabel(done);
        }

        // Operators whose integer result is plain 32-bit arithmetic (matching
        // VirtualMachine.IntBinary exactly) and can be emitted inline. Division,
        // modulo and comparisons have special handling and go through IntBinary.
        private static System.Reflection.Emit.OpCode? InlineIntOp(ScriptLex.LexTypes op) => (char)op switch
        {
            '+' => OpCodes.Add,
            '-' => OpCodes.Sub,
            '*' => OpCodes.Mul,
            '&' => OpCodes.And,
            '|' => OpCodes.Or,
            '^' => OpCodes.Xor,
            _   => null,
        };

        // Floating-point IL op for the numeric arithmetic operators, or null for
        // operators (comparisons, bitwise, modulo) we do not specialize for doubles.
        private static System.Reflection.Emit.OpCode? DoubleArithOp(ScriptLex.LexTypes op) => (char)op switch
        {
            '+' => OpCodes.Add,
            '-' => OpCodes.Sub,
            '*' => OpCodes.Mul,
            '/' => OpCodes.Div,
            _   => null,
        };

        // Guard that the operand in <paramref name="slot"/> is numeric (int or
        // double); branch to <paramref name="onFail"/> otherwise.
        private static void EmitNumericGuard(DynamicMethodBuilder b, LocalBuilder slot, Label onFail)
        {
            var ok = b.IL.DefineLabel();
            b.EmitIsInt(slot);
            b.IL.Emit(OpCodes.Brtrue, ok);
            b.EmitIsDouble(slot);
            b.IL.Emit(OpCodes.Brfalse, onFail);
            b.IL.MarkLabel(ok);
        }
    }
}
