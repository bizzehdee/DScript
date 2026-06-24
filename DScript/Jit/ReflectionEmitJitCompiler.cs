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

            // Control-flow lowering is added in a later task; decline for now so the
            // decoder's new jump support is a no-op until the emitter handles it.
            if (HasControlFlow(instrs))
                return null;

            // Speculative unboxed-int tier first (unless repeated deopts proved it
            // unprofitable), then the conservative boxed tier.
            if (!chunk.PreferConservativeTier)
            {
                var asInt = TryCompileSpeculativeInt(chunk, instrs);
                if (asInt != null) return asInt;
                var asDouble = TryCompileSpeculativeDouble(chunk, instrs);
                if (asDouble != null) return asDouble;
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
                        return false; // not pure / not int-typed / control flow
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
                        return false; // not pure / non-double / control flow
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

        // True when the instruction stream contains any branch.
        private static bool HasControlFlow(List<JitInstruction> instrs)
        {
            foreach (var instr in instrs)
                if (instr.Kind is JitOpKind.Jump or JitOpKind.JumpIfFalse or JitOpKind.JumpIfTrue)
                    return true;
            return false;
        }

        private static JitDelegate CompileConservative(Chunk chunk, List<JitInstruction> instrs)
        {
            var b = new DynamicMethodBuilder(chunk.Name ?? "anon");

            // Two scratch slots for binary operands and one for IntBinary's out value.
            // Each binary op fully consumes them before the next, so they are reused.
            var aSlot = b.DeclareLocal(typeof(ScriptVar));
            var bSlot = b.DeclareLocal(typeof(ScriptVar));
            var rSlot = b.DeclareLocal(typeof(ScriptVar));
            var argArr = b.DeclareLocal(typeof(ScriptVar[]));

            var emittedTerminalReturn = false;

            foreach (var instr in instrs)
            {
                switch (instr.Kind)
                {
                    case JitOpKind.PushConst:     b.EmitMaterializeConstant(instr.Constant); break;
                    case JitOpKind.PushIntLiteral: b.EmitPushIntConst(instr.IntValue); break;
                    case JitOpKind.PushVar:       b.EmitLoadNamedVar(instr.Name); break;
                    case JitOpKind.GetProp:       b.EmitGetProp(instr.Name, aSlot); break;
                    case JitOpKind.PushUndefined: b.EmitPushUndefined(); break;
                    case JitOpKind.PushNull:      b.EmitPushNull(); break;
                    case JitOpKind.Pop:           b.IL.Emit(OpCodes.Pop); break;
                    case JitOpKind.Not:           b.EmitLogicalNot(); break;
                    case JitOpKind.Binary:        EmitBinary(b, instr.Op, aSlot, bSlot, rSlot); break;
                    case JitOpKind.Call:          EmitCall(b, instr.MonoCallee, instr.IntValue, aSlot, bSlot, argArr); break;
                    case JitOpKind.Return:
                        b.IL.Emit(OpCodes.Ret);   // returns the ScriptVar on top of stack
                        emittedTerminalReturn = true;
                        break;
                }
            }

            if (!emittedTerminalReturn)
                b.EmitPushUndefined(); // fall-through: function returns undefined

            return b.Finish();
        }

        // Emit a plain (non-tail) call. The callee and its argc arguments are on the
        // IL stack as [callee, arg0, ..., arg{argc-1}] (top = last arg), matching the
        // interpreter's Call layout. Tail and method calls are declined by the decoder.
        //
        // Monomorphic sites (T14): the decoder supplies the single observed callee;
        // bake it, guard the runtime callee against it, and dispatch through
        // vm.InvokeCallable. A guard miss falls through to the same general dispatch
        // on the runtime callee. Bimorphic/megamorphic/unobserved sites (T15):
        // monoCallee is null, so dispatch on the runtime callee with no baked guard.
        private static void EmitCall(DynamicMethodBuilder b, ScriptVar monoCallee, int argc,
                                     LocalBuilder tmp, LocalBuilder calleeSlot, LocalBuilder argArr)
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

            if (monoCallee == null)
            {
                // Megamorphic fallback (T15): bimorphic, megamorphic, and unobserved
                // sites dispatch on the runtime callee with no baked guard.
                EmitGeneralCall(b, calleeSlot, argArr);
                return;
            }

            var bakedIndex = b.AddData(monoCallee);
            var general = il.DefineLabel();
            var done = il.DefineLabel();

            // Guard: runtime callee is the same object the site always saw.
            b.EmitLoadLocal(calleeSlot);
            b.EmitLoadData(bakedIndex, typeof(ScriptVar));
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Brfalse, general);

            // Monomorphic fast path: dispatch on the baked callee.
            b.EmitLoadVm();
            b.EmitLoadData(bakedIndex, typeof(ScriptVar));
            il.Emit(OpCodes.Ldnull);                   // thisArg
            b.EmitLoadLocal(argArr);
            b.EmitInvokeCallable();
            il.Emit(OpCodes.Br, done);

            // Guard miss: dispatch on the runtime callee.
            il.MarkLabel(general);
            EmitGeneralCall(b, calleeSlot, argArr);

            il.MarkLabel(done);
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
