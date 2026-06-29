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
    public sealed class ReflectionEmitJitCompiler : IJitCompiler, IOsrCompiler
    {
        /// <summary>
        /// Diagnostic counter: total number of OSR unboxed-long loop-tier entries this
        /// back-end has successfully compiled (as opposed to falling back to the
        /// conservative OSR entry). Exposed so tests and tuning can confirm the fast
        /// tier actually engaged. Monotonic; compare deltas across a run.
        /// </summary>
        public static long OsrLongLoopCompilations { get; private set; }

        /// <summary>
        /// Diagnostic counter (Lever 2c): number of speculative numeric-tier compilations
        /// that included at least one unboxed object-field read. Lets tests confirm the
        /// field-read fast path engaged rather than falling back to the conservative tier.
        /// </summary>
        public static long SpeculativeFieldReadCompilations { get; private set; }

        /// <summary>
        /// Kill switch for Lever 2c field-read speculation. When true, functions that
        /// read object fields decline the unboxed numeric tiers and use the conservative
        /// tier instead. For A/B measurement and as a safety fallback.
        /// </summary>
        public static bool DisableFieldReadSpeculation { get; set; }

        /// <summary>Diagnostic: the reason the OSR long-loop tier last declined a chunk.</summary>
        internal static string LastLongLoopDecline { get; private set; }

        /// <summary>
        /// Kill switch for Lever 2d monomorphic method-call inlining. When true, method
        /// calls use the general array + InvokeCallable dispatch instead of splicing the
        /// callee body. For A/B measurement and as a safety fallback. (The TailCallMethod
        /// lowering is unaffected — method calls still JIT-compile, just without inlining.)
        /// </summary>
        public static bool DisableMethodInlining { get; set; }

        /// <summary>
        /// Diagnostic counter (Lever 2e): number of int-loop-tier compilations where at
        /// least one non-escaping object literal was scalar-replaced. Lets tests confirm
        /// the scalar replacement engaged rather than falling to the conservative tier.
        /// </summary>
        public static long ScalarObjectReplacements { get; private set; }

        /// <summary>
        /// Kill switch for Lever 2e scalar object replacement. When true, object literals
        /// inside loops are not scalar-replaced and the function falls through to the
        /// conservative tier. For A/B measurement and as a safety fallback.
        /// </summary>
        public static bool DisableScalarObjectReplacement { get; set; }

        // True if the decoded stream contains positional local-slot ops (Lever A),
        // which this back-end does not emit.
        private static bool HasSlotOps(System.Collections.Generic.List<JitInstruction> instrs)
        {
            for (var i = 0; i < instrs.Count; i++)
                if (instrs[i].Kind is JitOpKind.GetLocal or JitOpKind.SetLocal)
                    return true;
            return false;
        }

        public JitDelegate Compile(Chunk chunk)
        {
            var instrs = JitDecoder.Decode(chunk);
            if (instrs == null)
                return null; // declined by the shared front-end

            // Positional local slots (Lever A) are an AOT/closure-build feature; this
            // back-end has no slot emission, so decline slotted chunks (the interpreter
            // runs them). In the default build the flag is off, so this never fires.
            if (HasSlotOps(instrs))
                return null;

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

            ClassifyFieldReads(instrs, out var receiverVars, out var fieldPairs);

            var b = new DynamicMethodBuilder(chunk.Name ?? "anon");
            var svTemp = b.DeclareLocal(typeof(ScriptVar));
            var deopt = b.IL.DefineLabel();
            var chunkIndex = b.AddData(chunk);

            // Prologue: resolve + int-guard each distinct numeric variable once, caching
            // its raw int value in a local. The body then reads locals, never re-resolving.
            var varLocals = new Dictionary<string, LocalBuilder>();
            foreach (var instr in instrs)
            {
                if (instr.Kind != JitOpKind.PushVar) continue;
                if (receiverVars.Contains(instr.Name)) continue;       // object receiver, resolved below
                if (varLocals.ContainsKey(instr.Name)) continue;
                var local = b.DeclareLocal(typeof(int));
                varLocals[instr.Name] = local;
                b.EmitResolveGuardedInt(instr.Name, local, svTemp, deopt);
            }

            // Resolve each object receiver to a ScriptVar local, then prefetch every
            // (receiver, field) value as a guarded raw int. Reads happen once here (the
            // function is pure, so field values cannot change mid-body) and all deopts
            // sit in the prologue with a clean IL stack.
            var objLocals = new Dictionary<string, LocalBuilder>();
            foreach (var rv in receiverVars)
            {
                var ol = b.DeclareLocal(typeof(ScriptVar));
                objLocals[rv] = ol;
                b.EmitResolveObjectVar(rv, ol);
            }
            var fieldLocals = new Dictionary<(string, string), LocalBuilder>();
            foreach (var (recv, field) in fieldPairs)
            {
                var fl = b.DeclareLocal(typeof(int));
                fieldLocals[(recv, field)] = fl;
                b.EmitResolveGuardedIntField(objLocals[recv], field, fl, svTemp, deopt);
            }

            // Body: raw int value flow. A `PushVar receiver; GetProp field` pair loads the
            // prefetched field local and skips the GetProp.
            for (var i = 0; i < instrs.Count; i++)
            {
                var instr = instrs[i];
                switch (instr.Kind)
                {
                    case JitOpKind.PushConst:      b.EmitLdcI4(instr.Constant.IntValue); break;
                    case JitOpKind.PushIntLiteral: b.EmitLdcI4(instr.IntValue); break;
                    case JitOpKind.PushVar:
                        if (receiverVars.Contains(instr.Name))
                        {
                            b.EmitLoadLocal(fieldLocals[(instr.Name, instrs[i + 1].Name)]);
                            i++; // consume the fused GetProp
                        }
                        else
                        {
                            b.EmitLoadLocal(varLocals[instr.Name]);
                        }
                        break;
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
            if (fieldPairs.Count > 0) SpeculativeFieldReadCompilations++;
            return b.Finish(appendRet: false);
        }

        // Classify property reads for speculative field-read support (Lever 2c).
        // A variable is a "field receiver" when every push of it is immediately followed
        // by a GetProp (i.e. it is only ever used as `v.field`, never in arithmetic).
        // Returns false (decline) when the stream mixes a variable's use as a receiver
        // and as a numeric value, or uses an unsupported GetProp form (a chained read,
        // or a GetProp whose receiver is not a plain variable). On success it yields the
        // set of receiver variables and the distinct (receiver, field) pairs to prefetch.
        private static bool ClassifyFieldReads(List<JitInstruction> instrs,
            out HashSet<string> receiverVars, out List<(string recv, string field)> fieldPairs)
        {
            receiverVars = new HashSet<string>();
            fieldPairs = new List<(string, string)>();
            var numericVars = new HashSet<string>();
            var seenPairs = new HashSet<string>();

            for (var i = 0; i < instrs.Count; i++)
            {
                var ins = instrs[i];
                if (ins.Kind == JitOpKind.GetProp)
                {
                    // Only single-level reads off a plain variable are supported.
                    if (i == 0 || instrs[i - 1].Kind != JitOpKind.PushVar) return false;
                    continue;
                }
                if (ins.Kind != JitOpKind.PushVar) continue;

                if (i + 1 < instrs.Count && instrs[i + 1].Kind == JitOpKind.GetProp)
                {
                    receiverVars.Add(ins.Name);
                    var field = instrs[i + 1].Name;
                    if (seenPairs.Add(ins.Name + " " + field))
                        fieldPairs.Add((ins.Name, field));
                }
                else
                {
                    numericVars.Add(ins.Name);
                }
            }

            // A variable cannot be both a field receiver (object) and a numeric value.
            foreach (var r in receiverVars)
                if (numericVars.Contains(r)) return false;
            return true;
        }

        // Eligibility for the speculative int tier.
        private static bool IsIntSpeculable(Chunk chunk, List<JitInstruction> instrs)
        {
            if (instrs.Count == 0 || instrs[instrs.Count - 1].Kind != JitOpKind.Return)
                return false;

            // GetProp is allowed only as a single-level numeric field read (Lever 2c);
            // anything else involving properties declines.
            if (!ClassifyFieldReads(instrs, out var fieldReceivers, out _))
                return false;
            if (DisableFieldReadSpeculation && fieldReceivers.Count > 0)
                return false;

            foreach (var instr in instrs)
            {
                switch (instr.Kind)
                {
                    case JitOpKind.Call:
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
                    case JitOpKind.MakeClosure:
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
            var il = b.IL;

            // +, -, * use overflow-checked IL: on 32-bit overflow they throw, and the
            // caller (the tier-up gate) catches it and deopts to the interpreter, which
            // promotes to a double (matching JS). Bitwise ops are 32-bit by definition.
            switch ((char)op)
            {
                case '+': il.Emit(OpCodes.Add_Ovf); return;
                case '-': il.Emit(OpCodes.Sub_Ovf); return;
                case '*': il.Emit(OpCodes.Mul_Ovf); return;
            }

            var inline = InlineBitwiseIntOp(op);
            if (inline.HasValue) { il.Emit(inline.Value); return; }

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
            // Pre-pass: scalar-replace non-escaping object literals so the loop body
            // contains only register ops, making it eligible for the unboxed int tier.
            var transformed = TryScalarReplaceObjects(instrs) ?? instrs;
            var didScalarReplace = !ReferenceEquals(transformed, instrs);

            if (!IsIntLoopSpeculable(chunk, transformed))
                return null;

            ClassifyFieldReads(transformed, out var receiverVars, out var fieldPairs);
            instrs = transformed; // use transformed list throughout

            var b = new DynamicMethodBuilder(chunk.Name ?? "anon");
            var svTemp = b.DeclareLocal(typeof(ScriptVar));
            var deopt = b.IL.DefineLabel();
            var chunkIndex = b.AddData(chunk);
            var il = b.IL;

            // One raw-int register per numeric variable (object receivers are excluded;
            // they are resolved as ScriptVar locals and their fields prefetched below).
            var regs = new Dictionary<string, LocalBuilder>();
            foreach (var instr in instrs)
                if (instr.Name != null && IsVarRef(instr.Kind)
                    && !receiverVars.Contains(instr.Name) && !regs.ContainsKey(instr.Name))
                    regs[instr.Name] = b.DeclareLocal(typeof(int));

            // Prologue: guard each parameter int and load it into its register. Locals
            // are left at the IL default (0) and set by their prologue assignment before
            // any read (guaranteed by eligibility), so they need no entry guard.
            foreach (var kv in regs)
                if (chunk.Parameters.Contains(kv.Key))
                    b.EmitResolveGuardedInt(kv.Key, kv.Value, svTemp, deopt);

            // Resolve each object receiver once and prefetch every (receiver, field)
            // value into a guarded int register. Sound because the receiver is not
            // reassigned and the loop performs no writes/calls (so the field is stable),
            // and all deopts here sit in the prologue with a clean IL stack.
            var objLocals = new Dictionary<string, LocalBuilder>();
            foreach (var rv in receiverVars)
            {
                var ol = b.DeclareLocal(typeof(ScriptVar));
                objLocals[rv] = ol;
                b.EmitResolveObjectVar(rv, ol);
            }
            var fieldLocals = new Dictionary<(string, string), LocalBuilder>();
            foreach (var (recv, field) in fieldPairs)
            {
                var fl = b.DeclareLocal(typeof(int));
                fieldLocals[(recv, field)] = fl;
                b.EmitResolveGuardedIntField(objLocals[recv], field, fl, svTemp, deopt);
            }

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
                    case JitOpKind.PushConst:
                        // Exact-integer doubles (e.g. 1e6) are accepted by eligibility and emitted as int.
                        b.EmitLdcI4(instr.Constant.Kind == ConstantKind.Int
                            ? instr.Constant.IntValue
                            : (int)instr.Constant.DoubleValue);
                        break;
                    case JitOpKind.PushIntLiteral: b.EmitLdcI4(instr.IntValue); break;
                    case JitOpKind.PushVar:
                        if (receiverVars.Contains(instr.Name))
                        {
                            b.EmitLoadLocal(fieldLocals[(instr.Name, instrs[i + 1].Name)]);
                            i++; // consume the fused GetProp
                        }
                        else
                        {
                            b.EmitLoadLocal(regs[instr.Name]);
                        }
                        break;
                    case JitOpKind.SetVar:         il.Emit(OpCodes.Dup); b.EmitStoreLocal(regs[instr.Name]); break; // expression
                    case JitOpKind.SetVarPop:      b.EmitStoreLocal(regs[instr.Name]); break;
                    case JitOpKind.Pop:            il.Emit(OpCodes.Pop); break;
                    case JitOpKind.DeclareVar:
                    case JitOpKind.DeclareLocal:
                    case JitOpKind.DeclareConst:
                    case JitOpKind.EnterBlock:
                    case JitOpKind.LeaveBlock:     break; // register already exists / no-op in the register tier
                    case JitOpKind.Not:            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); break;
                    case JitOpKind.Binary:         EmitIntBinaryRaw(b, instr.Op); break;
                    case JitOpKind.Jump:           il.Emit(OpCodes.Br, labels[instr.IntValue]); break;
                    case JitOpKind.JumpIfFalse:    il.Emit(OpCodes.Brfalse, labels[instr.IntValue]); break; // raw int condition
                    case JitOpKind.JumpIfTrue:     il.Emit(OpCodes.Brtrue, labels[instr.IntValue]); break;
                    case JitOpKind.Return:         b.EmitFromInt(); il.Emit(OpCodes.Ret); break;
                }
            }

            b.EmitDeoptReturn(deopt, chunkIndex);
            if (fieldPairs.Count > 0) SpeculativeFieldReadCompilations++;
            if (didScalarReplace) ScalarObjectReplacements++;
            return b.Finish(appendRet: false);
        }

        private static bool IsVarRef(JitOpKind k) =>
            k is JitOpKind.PushVar or JitOpKind.SetVar or JitOpKind.SetVarPop;

        private static bool IsJump(JitOpKind k) =>
            k is JitOpKind.Jump or JitOpKind.JumpIfFalse or JitOpKind.JumpIfTrue;

        // ── scalar object replacement (Lever 2e) ─────────────────────────────────
        // For functions that create object literals {p1:v1, …} inside a loop and only
        // read properties from them, replace the allocation with raw-int registers:
        //   NewObject + InitProp "a" + … + SetVarPop "o"  →  SetVarPop "o:a" + …
        //   PushVar "o" + GetProp "a"                      →  PushVar "o:a"
        //
        // Adds a synthetic prologue that zero-initialises every scalar property register
        // so the existing soundness check (first-ref-must-be-write-before-jump) passes.
        // Jump targets are remapped to account for the removed/merged instructions.
        // Returns a new instruction list, or null if no eligible objects were found.
        private static List<JitInstruction> TryScalarReplaceObjects(List<JitInstruction> instrs)
        {
            if (DisableScalarObjectReplacement) return null;
            if (!VerifyStackConsistency(instrs, out var depth)) return null;

            // ── 1. Detect non-escaping constructor sequences ──────────────────────
            // A sequence NewObject[D] … InitProp*[D+2] … SetVarPop "x"[D+1] where D is
            // the stack depth entering NewObject. All reads of "x" must be GetProp reads.
            var candidates = new Dictionary<string, (int newIdx, int setIdx, List<string> props)>();
            var declined   = new HashSet<string>(); // multiple assignments or non-GetProp read

            for (var i = 0; i < instrs.Count; i++)
            {
                if (instrs[i].Kind != JitOpKind.NewObject) continue;
                var D = depth[i];

                var props    = new List<string>();
                string vname = null;
                int    setAt = -1;
                bool   ok    = true;

                for (var j = i + 1; j < instrs.Count && ok; j++)
                {
                    var jd = depth[j];
                    if (jd < 0) { ok = false; break; } // unreachable instruction

                    if (jd == D + 2 && instrs[j].Kind == JitOpKind.InitProp)
                    {
                        props.Add(instrs[j].Name); // property set in construction order
                    }
                    else if (jd == D + 1)
                    {
                        var k2 = instrs[j].Kind;
                        if (k2 == JitOpKind.SetVarPop)
                        {
                            vname = instrs[j].Name;
                            setAt = j;
                            break; // end of constructor
                        }
                        // Jumps at this depth would consume the object via control flow → escape.
                        // Binary/unary at D+1 would consume the object as an operand → escape.
                        // SetVar (expression form) keeps the object on stack → decline for simplicity.
                        if (IsJump(k2) || k2 is JitOpKind.Return or JitOpKind.Pop
                            or JitOpKind.SetVar or JitOpKind.GetProp or JitOpKind.SetProp
                            or JitOpKind.SetPropPop or JitOpKind.SetIndex or JitOpKind.Call
                            or JitOpKind.CallMethod or JitOpKind.GetPropCall0 or JitOpKind.GetPropMethod)
                            ok = false;
                        // else: first instruction of the next value expression — continue
                    }
                    else if (jd < D + 1)
                    {
                        ok = false; // stack underflow below object — malformed
                    }
                    // jd > D+2: body of a multi-instruction value expression — continue
                }

                if (!ok || vname == null || setAt < 0) continue;
                if (declined.Contains(vname)) continue;

                // All reads must be GetProp reads (no aliasing, no identity comparisons, …)
                if (!AllObjectReadsAreGetProp(instrs, vname)) { declined.Add(vname); continue; }

                if (candidates.ContainsKey(vname))
                {
                    declined.Add(vname);     // second NewObject assignment to same var — decline
                    candidates.Remove(vname);
                    continue;
                }
                candidates[vname] = (i, setAt, props);
            }

            if (candidates.Count == 0) return null;

            // ── 2. Build instruction-level replacement maps ───────────────────────
            // removeSet:   indices to skip entirely (NewObject, SetVarPop "o", GetProp reads)
            // replaceMap:  index → replacement instruction (InitProp→SetVarPop, PushVar→PushVar)
            var removeSet  = new HashSet<int>();
            var replaceMap = new Dictionary<int, JitInstruction>();

            foreach (var (vname, (newIdx, setIdx, props)) in candidates)
            {
                var D       = depth[newIdx];
                removeSet.Add(newIdx);  // NewObject: eliminated
                removeSet.Add(setIdx);  // SetVarPop "o": eliminated

                // InitProp instructions → SetVarPop "o:prop"
                var propIdx = 0;
                for (var j = newIdx + 1; j < setIdx && propIdx < props.Count; j++)
                    if (depth[j] == D + 2 && instrs[j].Kind == JitOpKind.InitProp)
                        replaceMap[j] = JitInstruction.SetVarPop(vname + ":" + props[propIdx++]);
            }

            // PushVar "o" + GetProp "field" → PushVar "o:field"  (scan entire list)
            for (var i = 0; i < instrs.Count - 1; i++)
            {
                if (instrs[i].Kind == JitOpKind.PushVar && candidates.ContainsKey(instrs[i].Name)
                    && instrs[i + 1].Kind == JitOpKind.GetProp)
                {
                    replaceMap[i] = JitInstruction.PushVar(instrs[i].Name + ":" + instrs[i + 1].Name);
                    removeSet.Add(i + 1); // consume the GetProp (i itself is in replaceMap, not removeSet)
                    i++;                  // skip over the now-consumed GetProp in this loop
                }
            }

            // ── 3. Build old→new index remapping for jump-target fixup ────────────
            // Dummy prologue: 2 instructions (PushIntLiteral 0 + SetVarPop "o:p") per property.
            int prologueCount = 0;
            foreach (var kv in candidates) prologueCount += kv.Value.props.Count * 2;
            var oldToNew = new int[instrs.Count + 1];
            int newPos = prologueCount;
            for (var i = 0; i <= instrs.Count; i++)
            {
                oldToNew[i] = newPos;
                if (i < instrs.Count && !removeSet.Contains(i))
                    newPos++; // removed instructions contribute 0 output; replaced contribute 1
            }

            // ── 4. Build the transformed instruction list ─────────────────────────
            var result = new List<JitInstruction>(instrs.Count + prologueCount);

            // Dummy prologue: zero-initialise each scalar property register so the
            // first-ref-before-jump soundness check in IsIntLoopSpeculable passes.
            foreach (var (vname, (_, _, props)) in candidates)
                foreach (var p in props)
                {
                    result.Add(JitInstruction.PushIntLiteral(0));
                    result.Add(JitInstruction.SetVarPop(vname + ":" + p));
                }

            for (var i = 0; i < instrs.Count; i++)
            {
                if (replaceMap.TryGetValue(i, out var replacement)) { result.Add(replacement); continue; }
                if (removeSet.Contains(i)) continue;

                var instr = instrs[i];
                // Remap jump targets to account for the removed/merged instructions.
                if (IsJump(instr.Kind))
                {
                    var nt = oldToNew[instr.IntValue];
                    instr = instr.Kind switch
                    {
                        JitOpKind.Jump         => JitInstruction.Jump(nt),
                        JitOpKind.JumpIfFalse  => JitInstruction.JumpIfFalse(nt),
                        JitOpKind.JumpIfTrue   => JitInstruction.JumpIfTrue(nt),
                        _                      => instr,
                    };
                }
                result.Add(instr);
            }

            return result;
        }

        // Returns true when every PushVar of <paramref name="varname"/> is immediately
        // followed by a GetProp, so the variable is used only as a property-read receiver.
        private static bool AllObjectReadsAreGetProp(List<JitInstruction> instrs, string varname)
        {
            for (var i = 0; i < instrs.Count; i++)
                if (instrs[i].Kind == JitOpKind.PushVar && instrs[i].Name == varname)
                    if (i + 1 >= instrs.Count || instrs[i + 1].Kind != JitOpKind.GetProp)
                        return false;
            return true;
        }

        private static bool IsIntLoopSpeculable(Chunk chunk, List<JitInstruction> instrs)
        {
            if (instrs.Count == 0 || instrs[instrs.Count - 1].Kind != JitOpKind.Return)
                return false;

            // GetProp is allowed only as a single-level numeric field read (Lever 2c).
            if (!ClassifyFieldReads(instrs, out var fieldReceivers, out _))
                return false;
            if (DisableFieldReadSpeculation && fieldReceivers.Count > 0)
                return false;
            // A field is prefetched once in the prologue, so its receiver must be stable:
            // decline if any receiver variable is reassigned in the function body. (Field
            // values themselves cannot change — SetProp and calls are rejected below.)
            if (fieldReceivers.Count > 0)
                foreach (var instr in instrs)
                    if ((instr.Kind is JitOpKind.SetVar or JitOpKind.SetVarPop)
                        && fieldReceivers.Contains(instr.Name))
                        return false;

            foreach (var instr in instrs)
            {
                switch (instr.Kind)
                {
                    case JitOpKind.PushConst:
                        if (instr.Constant.Kind == ConstantKind.Int) break;
                        // Accept exact-integer doubles (e.g. 1e6 = 1000000.0) — emitted as int.
                        if (instr.Constant.Kind == ConstantKind.Double)
                        {
                            var d = instr.Constant.DoubleValue;
                            if (d == System.Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue) break;
                        }
                        return false;
                    case JitOpKind.Binary:
                        if (InlineIntOp(instr.Op) == null && !IsIntComparison(instr.Op)) return false; // no /,%, etc.
                        break;
                    case JitOpKind.GetProp:        // single-level numeric field read (validated above)
                    case JitOpKind.PushIntLiteral:
                    case JitOpKind.PushVar:
                    case JitOpKind.SetVar:
                    case JitOpKind.SetVarPop:
                    case JitOpKind.Pop:            // discards a post-increment expression result (i++)
                    case JitOpKind.DeclareVar:
                    case JitOpKind.DeclareLocal:
                    case JitOpKind.DeclareConst:
                    case JitOpKind.EnterBlock:     // no-op in the register tier: all variables are in raw-int
                    case JitOpKind.LeaveBlock:     // registers that bypass the env; closures are declined below
                    case JitOpKind.Not:
                    case JitOpKind.Jump:
                    case JitOpKind.JumpIfFalse:
                    case JitOpKind.JumpIfTrue:
                    case JitOpKind.Return:
                        break;
                    default:
                        return false; // calls, prop writes, indexing, conditional-pop jumps, shifts, etc.
                }
            }

            // Evidence of integer arithmetic: at least one binary site, all Int-only.
            var profiles = chunk.GetBinaryOpProfiles();
            if (profiles.Count == 0) return false;
            foreach (var (_, p) in profiles)
                if (p.LeftTypes != Chunk.BinaryTypeFlags.Int || p.RightTypes != Chunk.BinaryTypeFlags.Int)
                    return false;

            // Shadowing guard: when EnterBlock/LeaveBlock are treated as no-ops, all
            // variables share a single register pool. Decline if the same name is
            // declared more than once (which would mean an inner binding shadows an outer
            // one — the inner SetVarPop would clobber the outer register).
            var declaredNames = new HashSet<string>();
            foreach (var instr in instrs)
                if (instr.Kind is JitOpKind.DeclareConst or JitOpKind.DeclareLocal or JitOpKind.DeclareVar)
                    if (instr.Name != null && !declaredNames.Add(instr.Name))
                        return false; // duplicate declaration → potential shadowing

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
                    case JitOpKind.MakeClosure:
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

        // Build an OSR entry: a conservative-tier compilation that, after its prologue,
        // jumps straight to the loop header at <paramref name="resumeOffset"/> and runs
        // the rest of the function from there. Live locals are shared with the abandoned
        // interpreter frame through the env argument, so no operand-stack transfer is
        // needed — which is sound only when the operand stack is empty at the resume
        // point (true for structured for/while back-edges). Declines (returns null) when
        // that does not hold or the shape is otherwise unsupported.
        public JitDelegate CompileOsr(Chunk chunk, int resumeOffset)
        {
            var instrs = JitDecoder.Decode(chunk);
            if (instrs == null) return null;
            if (HasSlotOps(instrs)) return null; // slots are AOT/closure-only (see Compile)

            var resumeIndex = JitDecoder.OffsetToInstructionIndex(chunk, resumeOffset);
            if (resumeIndex < 0 || resumeIndex >= instrs.Count) return null;

            // The operand stack must be empty where we resume (we transfer no operands).
            if (!VerifyStackConsistency(instrs, out var depth)) return null;
            if (depth[resumeIndex] != 0) return null;

            // Block scopes: the interpreter hands us its current env, which already is
            // the block env at the resume point — so resuming *inside* a block entered
            // in the (skipped) prologue is fine; the conservative tier pre-seeds its
            // current-env local from that env. What is NOT safe is a LeaveBlock in the
            // resumed region that would pop a block entered before the resume point —
            // that would move the env above the level we resumed at. Decline only when
            // the reachable region's block nesting goes below where it started.
            var reach = ReachableFrom(instrs, resumeIndex);
            var ordered = new List<int>(reach);
            ordered.Sort();
            var blockDepth = 0;
            foreach (var i in ordered)
            {
                if (instrs[i].Kind == JitOpKind.EnterBlock) blockDepth++;
                else if (instrs[i].Kind == JitOpKind.LeaveBlock && --blockDepth < 0) return null;
            }

            // The conservative entry is always the safe baseline. Try the faster
            // unboxed-long loop tier on top, falling back to the conservative entry on
            // a type miss at its guard. If the chunk can't even be compiled
            // conservatively, there is no OSR entry.
            var conservative = CompileConservative(chunk, instrs, resumeIndex);
            if (conservative == null) return null;

            var fast = TryCompileOsrLongLoop(chunk, instrs, resumeIndex, depth, conservative);
            return fast ?? conservative;
        }

        // ── speculative unboxed-LONG loop tier (OSR only) ────────────────────────
        // Compile the reachable loop region as raw int64 flowing through IL: scalar
        // variables become CLR `long` registers (no per-iteration env lookup or
        // boxing), monomorphic int-leaf calls are inlined as raw long arithmetic, and
        // values are written back to the environment only at each function exit. The
        // whole IL operand stack is uniformly `long`, so no per-slot type tracking is
        // needed. Soundness rests on three compile-time facts: (1) every binary site is
        // profiled int-only, (2) every assignment's RHS is an int-typed expression
        // (int const / int arithmetic / int-returning inlined call), so once a register
        // holds an int it stays one, and (3) inlined callees read only their own
        // parameters, so they cannot observe the (deliberately stale) environment. An
        // entry guard loads each register from the environment and, on any non-integer,
        // defers to the conservative OSR entry — so reuse across frames with differing
        // types stays correct without any deopt machinery. Returns null to decline.
        private static JitDelegate TryCompileOsrLongLoop(Chunk chunk, List<JitInstruction> instrs,
                                                         int resumeIndex, int[] depth, JitDelegate fallback)
        {
            // Decline the long tier (the caller falls back to the conservative OSR
            // entry). The reason string documents each decline at its call site.
            static JitDelegate Decline(string reason) { LastLongLoopDecline = reason; return null; }

            // (1) No binary site may have been *observed* with a string/object operand.
            // Sites with no profile yet (None) are allowed: they are typically code after
            // the loop that has not executed when OSR fires, and correctness does not rest
            // on the profile anyway — every value in the region flows as a long by
            // construction (guarded-int registers, integer constants, int-leaf call
            // results), and the entry guard hands off to the conservative tier if any
            // register is not actually an integer. The profile only lets us cheaply
            // decline loops already seen running on strings/objects.
            const Chunk.BinaryTypeFlags numeric = Chunk.BinaryTypeFlags.Int | Chunk.BinaryTypeFlags.Double;
            var profiles = chunk.GetBinaryOpProfiles();
            if (profiles.Count == 0) return Decline("no profiles");
            foreach (var (_, p) in profiles)
                if ((p.LeftTypes & ~numeric) != 0 || (p.RightTypes & ~numeric) != 0)
                    return Decline($"non-numeric profile L={p.LeftTypes} R={p.RightTypes}");

            // Region reachable from the resume point, as a contiguous [lo, hi] window.
            var reach = ReachableFrom(instrs, resumeIndex);
            var lo = int.MaxValue;
            var hi = -1;
            foreach (var idx in reach) { if (idx < lo) lo = idx; if (idx > hi) hi = idx; }
            if (hi < 0) return null;

            // Identify inlinable calls and the callee-push instructions they elide.
            var calleeSkip = new HashSet<int>();
            var inlineAt = new Dictionary<int, (Chunk c, List<JitInstruction> body)>();
            if (!AnalyzeLongLoopCalls(instrs, lo, hi, depth, calleeSkip, inlineAt))
                return Decline("call not inlinable");

            // Field reads (Lever 2c): classify single-level numeric field reads in the
            // region. The receiver is resolved once at the entry and its field prefetched
            // into a long register; sound because the region performs no property writes
            // or non-leaf calls (so the field is stable) and the receiver is not
            // reassigned. A guard miss defers to the conservative OSR entry like any other.
            var regionInstrs = instrs.GetRange(lo, hi - lo + 1);
            if (!ClassifyFieldReads(regionInstrs, out var fieldReceivers, out var fieldPairs))
                return Decline("unsupported property form");
            if (DisableFieldReadSpeculation && fieldReceivers.Count > 0)
                return Decline("field speculation disabled");
            if (fieldReceivers.Count > 0)
                for (var i = lo; i <= hi; i++)
                {
                    var k = instrs[i].Kind;
                    if ((k is JitOpKind.SetVar or JitOpKind.SetVarPop) && fieldReceivers.Contains(instrs[i].Name))
                        return Decline("field receiver reassigned in region");
                }
            foreach (var i in calleeSkip)
                if (fieldReceivers.Contains(instrs[i].Name)) return Decline("field receiver used as callee");

            // Promotable scalar registers: every variable referenced in the region that
            // is not an elided callee push.
            var regsOrder = new List<string>();
            var regSet = new HashSet<string>();
            for (var i = lo; i <= hi; i++)
            {
                if (calleeSkip.Contains(i)) continue;
                var instr = instrs[i];
                if (instr.Name != null && IsVarRef(instr.Kind)
                    && !fieldReceivers.Contains(instr.Name) && regSet.Add(instr.Name))
                    regsOrder.Add(instr.Name);
            }
            // A name used as a callee must not also be a register (i.e. reassigned in the
            // region) — that would make the elided push unsound.
            foreach (var i in calleeSkip)
                if (regSet.Contains(instrs[i].Name)) return null; // callee name also assigned in region

            // Every region instruction must be long-emittable; jumps must stay in-region.
            for (var i = lo; i <= hi; i++)
            {
                if (calleeSkip.Contains(i)) continue;
                var instr = instrs[i];
                switch (instr.Kind)
                {
                    case JitOpKind.PushConst:
                        if (!TryConstAsLong(instr.Constant, out _))
                            return Decline($"non-integer const at {i}");
                        break;
                    case JitOpKind.Binary:
                        if (!IsLongLoopBinary(instr.Op)) return Decline($"binary {(char)instr.Op} at {i}");
                        break;
                    case JitOpKind.Call:
                        if (!inlineAt.ContainsKey(i)) return Decline($"non-inlined call at {i}");
                        break;
                    case JitOpKind.Jump:
                    case JitOpKind.JumpIfFalse:
                    case JitOpKind.JumpIfTrue:
                        if (instr.IntValue < lo || instr.IntValue > hi) return Decline($"jump escapes [{lo},{hi}] at {i}->{instr.IntValue}");
                        break;
                    case JitOpKind.GetProp:        // single-level numeric field read (validated by ClassifyFieldReads)
                        break;
                    case JitOpKind.PushIntLiteral:
                    case JitOpKind.PushVar:
                    case JitOpKind.SetVar:
                    case JitOpKind.SetVarPop:
                    case JitOpKind.DeclareVar:
                    case JitOpKind.DeclareLocal:
                    case JitOpKind.DeclareConst:
                    case JitOpKind.Not:
                    case JitOpKind.Pop:
                    case JitOpKind.Return:
                        break;
                    case JitOpKind.PushUndefined:
                        // Only valid as the operand of a fall-through `return undefined`
                        // (anywhere else it would put a non-long on the long stack).
                        if (i >= hi || instrs[i + 1].Kind != JitOpKind.Return)
                            return Decline($"PushUndefined not before Return at {i}");
                        break;
                    default:
                        return Decline($"unsupported {instr.Kind} at {i}");
                }
            }

            // ── emit ──────────────────────────────────────────────────────────────
            var b = new DynamicMethodBuilder(chunk.Name ?? "anon");
            var il = b.IL;
            var svTemp = b.DeclareLocal(typeof(ScriptVar));
            var fallbackIndex = b.AddData(fallback);
            var strict = chunk.IsStrict;

            var regs = new Dictionary<string, LocalBuilder>();
            foreach (var name in regsOrder) regs[name] = b.DeclareLocal(typeof(long));

            var maxArgc = 0;
            foreach (var kv in inlineAt)
                if (kv.Value.c.Parameters.Count > maxArgc) maxArgc = kv.Value.c.Parameters.Count;
            var argTemps = new LocalBuilder[maxArgc];
            for (var j = 0; j < maxArgc; j++) argTemps[j] = b.DeclareLocal(typeof(long));

            var miss = il.DefineLabel();

            // Entry guard: load every register from the environment; any non-integer
            // hands off to the conservative OSR entry.
            foreach (var name in regsOrder)
                b.EmitResolveGuardedLong(name, regs[name], svTemp, miss);

            // Resolve each field receiver once and prefetch its numeric field into a long
            // register; a non-integer / accessor / proxy field hands off to the
            // conservative OSR entry (the shared `miss` label) just like a register guard.
            var objLocals = new Dictionary<string, LocalBuilder>();
            foreach (var rv in fieldReceivers)
            {
                var ol = b.DeclareLocal(typeof(ScriptVar));
                objLocals[rv] = ol;
                b.EmitResolveObjectVar(rv, ol);
            }
            var fieldLocals = new Dictionary<(string, string), LocalBuilder>();
            foreach (var (recv, field) in fieldPairs)
            {
                var fl = b.DeclareLocal(typeof(long));
                fieldLocals[(recv, field)] = fl;
                b.EmitResolveGuardedLongField(objLocals[recv], field, fl, svTemp, miss);
            }

            var labels = new Dictionary<int, Label>();
            for (var i = lo; i <= hi; i++)
            {
                var instr = instrs[i];
                if (instr.Kind is JitOpKind.Jump or JitOpKind.JumpIfFalse or JitOpKind.JumpIfTrue)
                    labels[instr.IntValue] = il.DefineLabel();
            }
            if (!labels.TryGetValue(resumeIndex, out var resumeLabel)) return null;
            il.Emit(OpCodes.Br, resumeLabel);

            for (var i = lo; i <= hi; i++)
            {
                if (labels.TryGetValue(i, out var here)) il.MarkLabel(here);
                if (calleeSkip.Contains(i)) continue; // callee push elided

                var instr = instrs[i];
                switch (instr.Kind)
                {
                    case JitOpKind.PushConst:      TryConstAsLong(instr.Constant, out var cv); b.EmitLdcI8(cv); break;
                    case JitOpKind.PushIntLiteral: b.EmitLdcI8(instr.IntValue); break;
                    case JitOpKind.PushVar:
                        if (fieldReceivers.Contains(instr.Name))
                        {
                            b.EmitLoadLocal(fieldLocals[(instr.Name, instrs[i + 1].Name)]);
                            i++; // consume the fused GetProp
                        }
                        else
                        {
                            b.EmitLoadLocal(regs[instr.Name]);
                        }
                        break;
                    case JitOpKind.SetVar:         il.Emit(OpCodes.Dup); b.EmitStoreLocal(regs[instr.Name]); break;
                    case JitOpKind.SetVarPop:      b.EmitStoreLocal(regs[instr.Name]); break;
                    case JitOpKind.DeclareVar:
                    case JitOpKind.DeclareLocal:
                    case JitOpKind.DeclareConst:   break;
                    case JitOpKind.Pop:            il.Emit(OpCodes.Pop); break;
                    case JitOpKind.Not:            il.Emit(OpCodes.Ldc_I8, 0L); il.Emit(OpCodes.Ceq); b.EmitConvI8(); break;
                    case JitOpKind.Binary:         EmitLongBinary(b, instr.Op); break;
                    case JitOpKind.Jump:           il.Emit(OpCodes.Br, labels[instr.IntValue]); break;
                    case JitOpKind.JumpIfFalse:    il.Emit(OpCodes.Brfalse, labels[instr.IntValue]); break;
                    case JitOpKind.JumpIfTrue:     il.Emit(OpCodes.Brtrue, labels[instr.IntValue]); break;
                    case JitOpKind.Call:           EmitInlinedLongCall(b, inlineAt[i].c, inlineAt[i].body, argTemps); break;
                    case JitOpKind.PushUndefined:  break; // operand of a fall-through return undefined; see Return
                    case JitOpKind.Return:
                        // Write registers back so globals/outer locals observe the final
                        // value, then produce the result. A `return undefined`
                        // (PushUndefined immediately before) has no long on the stack.
                        foreach (var name in regsOrder) b.EmitWriteBackLong(name, regs[name], strict);
                        if (i > lo && instrs[i - 1].Kind == JitOpKind.PushUndefined)
                            b.EmitPushUndefined();
                        else
                            b.EmitBoxLong();
                        il.Emit(OpCodes.Ret);
                        break;
                }
            }

            // Fall-through past the region (dead when the region ends in Return, but
            // keeps the IL well-formed): write back and return undefined.
            foreach (var name in regsOrder) b.EmitWriteBackLong(name, regs[name], strict);
            b.EmitPushUndefined();
            il.Emit(OpCodes.Ret);

            // Type miss: defer to the conservative OSR entry (reads fresh from env).
            il.MarkLabel(miss);
            b.EmitInvokeJitDelegate(fallbackIndex);
            il.Emit(OpCodes.Ret);

            OsrLongLoopCompilations++;
            if (fieldPairs.Count > 0) SpeculativeFieldReadCompilations++;
            return b.Finish(appendRet: false);
        }

        // A constant usable as a raw int64 in the long-loop tier: an int constant, or a
        // double constant whose value is an exact integer within the 2^53 range where
        // doubles represent integers precisely (so treating it as long can't diverge
        // from the interpreter's numeric result). e.g. the loop bound 1e7.
        private static bool TryConstAsLong(ConstantValue c, out long value)
        {
            if (c.Kind == ConstantKind.Int) { value = c.IntValue; return true; }
            if (c.Kind == ConstantKind.Double)
            {
                var d = c.DoubleValue;
                if (!double.IsNaN(d) && !double.IsInfinity(d) &&
                    d == System.Math.Floor(d) && System.Math.Abs(d) < 9007199254740992.0)
                {
                    value = (long)d;
                    return true;
                }
            }
            value = 0;
            return false;
        }

        // Binary operators the long-loop tier handles: +,-,* (unchecked int64, matching
        // VirtualMachine.IntBinary which also wraps via IntOrDouble) and comparisons
        // (0/1 results). Division, modulo, bitwise and shifts can yield doubles or
        // 32-bit-coerced results that don't stay in long flow, so they are declined.
        private static bool IsLongLoopBinary(ScriptLex.LexTypes op) => (char)op switch
        {
            '+' or '-' or '*' => true,
            _ => IsIntComparison(op),
        };

        // Raw int64 binary op over two longs on the IL stack. Comparisons emit a 0/1
        // value (i4 then conv.i8) matching IntBinary's boolean results.
        private static void EmitLongBinary(DynamicMethodBuilder b, ScriptLex.LexTypes op)
        {
            var il = b.IL;
            switch ((char)op)
            {
                case '+': il.Emit(OpCodes.Add); return;
                case '-': il.Emit(OpCodes.Sub); return;
                case '*': il.Emit(OpCodes.Mul); return;
            }
            switch (op)
            {
                case (ScriptLex.LexTypes)'<':   il.Emit(OpCodes.Clt); b.EmitConvI8(); break;
                case (ScriptLex.LexTypes)'>':   il.Emit(OpCodes.Cgt); b.EmitConvI8(); break;
                case ScriptLex.LexTypes.LEqual: il.Emit(OpCodes.Cgt); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); b.EmitConvI8(); break;
                case ScriptLex.LexTypes.GEqual: il.Emit(OpCodes.Clt); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); b.EmitConvI8(); break;
                case ScriptLex.LexTypes.Equal:  il.Emit(OpCodes.Ceq); b.EmitConvI8(); break;
                case ScriptLex.LexTypes.NEqual: il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); b.EmitConvI8(); break;
            }
        }

        // Splice a pure-int param-only leaf callee inline, in long form. The arguments
        // are already on the IL stack as longs (the callee push was elided); pop them
        // into per-parameter temps, then emit the body reading params from those temps.
        private static void EmitInlinedLongCall(DynamicMethodBuilder b, Chunk calleeChunk,
                                                List<JitInstruction> body, LocalBuilder[] argTemps)
        {
            var il = b.IL;
            var argc = calleeChunk.Parameters.Count;
            for (var j = argc - 1; j >= 0; j--) b.EmitStoreLocal(argTemps[j]);

            var labels = new Dictionary<int, Label>();
            foreach (var instr in body)
                if (instr.Kind is JitOpKind.Jump or JitOpKind.JumpIfFalse or JitOpKind.JumpIfTrue)
                    labels[instr.IntValue] = il.DefineLabel();
            var end = il.DefineLabel();

            for (var i = 0; i < body.Count; i++)
            {
                if (labels.TryGetValue(i, out var here)) il.MarkLabel(here);
                var instr = body[i];
                switch (instr.Kind)
                {
                    case JitOpKind.PushConst:      b.EmitLdcI8(instr.Constant.IntValue); break;
                    case JitOpKind.PushIntLiteral: b.EmitLdcI8(instr.IntValue); break;
                    case JitOpKind.PushVar:        b.EmitLoadLocal(argTemps[calleeChunk.Parameters.IndexOf(instr.Name)]); break;
                    case JitOpKind.Not:            il.Emit(OpCodes.Ldc_I8, 0L); il.Emit(OpCodes.Ceq); b.EmitConvI8(); break;
                    case JitOpKind.Binary:         EmitLongBinary(b, instr.Op); break;
                    case JitOpKind.Jump:           il.Emit(OpCodes.Br, labels[instr.IntValue]); break;
                    case JitOpKind.JumpIfFalse:    il.Emit(OpCodes.Brfalse, labels[instr.IntValue]); break;
                    case JitOpKind.JumpIfTrue:     il.Emit(OpCodes.Brtrue, labels[instr.IntValue]); break;
                    case JitOpKind.Return:         il.Emit(OpCodes.Br, end); break;
                }
            }
            il.MarkLabel(end);
        }

        // Instruction indices reachable from <paramref name="start"/> by fall-through
        // and any jump target — used to bound the OSR loop region.
        private static HashSet<int> ReachableFrom(List<JitInstruction> instrs, int start)
        {
            var seen = new HashSet<int>();
            var work = new Stack<int>();
            work.Push(start);
            while (work.Count > 0)
            {
                var i = work.Pop();
                if (i < 0 || i >= instrs.Count || !seen.Add(i)) continue;
                var instr = instrs[i];
                switch (instr.Kind)
                {
                    case JitOpKind.Return:
                        break; // terminal
                    case JitOpKind.Jump:
                        work.Push(instr.IntValue);
                        break;
                    case JitOpKind.JumpIfFalse:
                    case JitOpKind.JumpIfTrue:
                    case JitOpKind.JumpIfFalseOrPop:
                    case JitOpKind.JumpIfTrueOrPop:
                    case JitOpKind.JumpIfNullOrUndefined:
                    case JitOpKind.JumpIfDefined:
                        work.Push(instr.IntValue);
                        work.Push(i + 1);
                        break;
                    default:
                        work.Push(i + 1);
                        break;
                }
            }
            return seen;
        }

        // For each Call in [lo, hi], find the instruction that produced its callee and,
        // when the callee is the baked monomorphic callee and an inline-eligible
        // pure-int param-only leaf, record the inline body and mark the callee push for
        // elision. Returns false (decline) if any call cannot be inlined this way.
        private static bool AnalyzeLongLoopCalls(List<JitInstruction> instrs, int lo, int hi, int[] depth,
                                                 HashSet<int> calleeSkip,
                                                 Dictionary<int, (Chunk c, List<JitInstruction> body)> inlineAt)
        {
            var prod = new List<int>(); // operand-stack of producer instruction indices
            for (var i = lo; i <= hi; i++)
            {
                // Resync at merge points: a structured back-edge / branch target has an
                // empty operand stack, so any partial producer state is stale.
                if (i >= 0 && i < depth.Length && depth[i] == 0) prod.Clear();

                var instr = instrs[i];
                switch (instr.Kind)
                {
                    case JitOpKind.PushConst:
                    case JitOpKind.PushIntLiteral:
                    case JitOpKind.PushVar:
                    case JitOpKind.PushNull:
                    case JitOpKind.PushUndefined:
                        prod.Add(i);
                        break;
                    case JitOpKind.Not:
                    case JitOpKind.GetProp:
                        // replaces top (GetProp pops the receiver and pushes the value)
                        if (prod.Count > 0) { prod[prod.Count - 1] = i; }
                        break;
                    case JitOpKind.Binary:
                        if (prod.Count >= 2) { prod.RemoveAt(prod.Count - 1); prod[prod.Count - 1] = i; }
                        break;
                    case JitOpKind.SetVar:
                        if (prod.Count > 0) prod[prod.Count - 1] = i; // pops value, pushes value
                        break;
                    case JitOpKind.SetVarPop:
                    case JitOpKind.JumpIfFalse:
                    case JitOpKind.JumpIfTrue:
                    case JitOpKind.Return:
                    case JitOpKind.Pop:
                        if (prod.Count > 0) prod.RemoveAt(prod.Count - 1);
                        break;
                    case JitOpKind.Call:
                    {
                        var argc = instr.IntValue;
                        if (prod.Count < argc + 1) return false; // shape we don't model
                        var calleeProducer = prod[prod.Count - argc - 1];
                        // Callee must be a plain PushVar of the baked monomorphic callee.
                        if (instr.MonoCallee == null || instr.MonoCallee1 != null) return false;
                        var producerInstr = instrs[calleeProducer];
                        if (producerInstr.Kind != JitOpKind.PushVar) return false;
                        if (!TryGetIntLeafInlineBody(instr.MonoCallee, argc, out var calleeChunk, out var body))
                            return false;
                        inlineAt[i] = (calleeChunk, body);
                        calleeSkip.Add(calleeProducer);
                        // Collapse the call's operands to a single produced value.
                        for (var k = 0; k < argc + 1; k++) prod.RemoveAt(prod.Count - 1);
                        prod.Add(i);
                        break;
                    }
                    case JitOpKind.DeclareVar:
                    case JitOpKind.DeclareLocal:
                    case JitOpKind.DeclareConst:
                    case JitOpKind.Jump:
                        break; // no operand-stack effect
                    default:
                        return false; // unsupported in a long-loop region with calls
                }
            }
            return true;
        }

        // A callee is long-inline-eligible when it is a small leaf that reads ONLY its
        // own parameters (no free variables — so it cannot observe the deliberately
        // stale environment), uses only int constants and long-safe arithmetic /
        // comparisons, and returns. No property reads, calls, closures, or non-int
        // constants. May contain control flow.
        private static bool TryGetIntLeafInlineBody(ScriptVar callee, int argc, out Chunk calleeChunk,
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
                    case JitOpKind.PushIntLiteral:
                    case JitOpKind.Not:
                    case JitOpKind.Jump:
                    case JitOpKind.JumpIfFalse:
                    case JitOpKind.JumpIfTrue:
                    case JitOpKind.Return:
                        break;
                    case JitOpKind.PushConst:
                        if (instr.Constant.Kind != ConstantKind.Int) return false;
                        break;
                    case JitOpKind.PushVar:
                        if (!c.Parameters.Contains(instr.Name)) return false; // free var → reject
                        break;
                    case JitOpKind.Binary:
                        if (!IsLongLoopBinary(instr.Op)) return false;
                        break;
                    default:
                        return false;
                }
            }

            if (!VerifyStackConsistency(instrs)) return false;

            calleeChunk = c;
            body = instrs;
            return true;
        }

        private static JitDelegate CompileConservative(Chunk chunk, List<JitInstruction> instrs,
                                                       int osrResumeIndex = -1)
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

            // OSR entry: after the prologue (scratch locals + any block-env setup),
            // jump straight to the resume point. The resume index is a loop-header /
            // back-edge target, so it always has a label. Declarations and initialisers
            // before it are skipped — they already ran in the interpreter frame.
            if (osrResumeIndex >= 0)
            {
                if (!labels.TryGetValue(osrResumeIndex, out var resumeLabel))
                    return null;
                b.IL.Emit(OpCodes.Br, resumeLabel);
            }

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
                    case JitOpKind.MakeClosure:   b.EmitMakeClosure(instr.Closure); break;
                    case JitOpKind.InitProp:      b.EmitInitProp(instr.Name); break;
                    case JitOpKind.InitElem:      b.EmitInitElem(instr.IntValue); break;
                    case JitOpKind.New:           b.EmitNew(instr.IntValue, aSlot, bSlot, argArr); break;
                    case JitOpKind.MergeObject:   b.EmitMergeObject(aSlot, bSlot); break;
                    case JitOpKind.InitPropOverwrite: b.EmitInitPropOverwrite(instr.Name, aSlot); break;
                    case JitOpKind.AppendElem:    b.EmitAppendElem(aSlot); break;
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
                    case JitOpKind.CallMethod:    EmitCallMethod(b, instr.MonoCallee, instr.MonoCallee1, instr.IntValue, aSlot, bSlot, rSlot, argArr); break;
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

        private static bool VerifyStackConsistency(List<JitInstruction> instrs)
            => VerifyStackConsistency(instrs, out _);

        // Abstract-interpret the operand-stack depth across all branch edges; return
        // false on any inconsistency or underflow (in which case the chunk is declined
        // rather than risk invalid IL). On success <paramref name="depth"/> holds the
        // operand-stack depth on entry to each instruction (−1 for unreached indices).
        private static bool VerifyStackConsistency(List<JitInstruction> instrs, out int[] depth)
        {
            var n = instrs.Count;
            depth = new int[n];
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
                or JitOpKind.NewObject or JitOpKind.NewArray or JitOpKind.MakeClosure => 1,
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

            // Fast path: monomorphic site where the sole callee is inline-eligible.
            // Avoid building the argArray entirely for the hot inline branch:
            // 1. Store args into per-slot locals (no allocation)
            // 2. Emit the callee identity guard
            // 3. On hit: splice the inline body reading from the per-arg locals
            // 4. On miss: build the array from the saved locals, then general-dispatch
            // This eliminates one ScriptVar[] allocation per inlined call site.
            if (callee1 == null && callee0 != null &&
                TryGetInlineBody(callee0, argc, out var inlineChunk, out var inlineBody))
            {
                // Allocate per-argument locals (argc ≤ InlineBudget ≤ 24, so bounded).
                var argLocals = new LocalBuilder[argc];
                for (var j = 0; j < argc; j++)
                    argLocals[j] = b.DeclareLocal(typeof(ScriptVar));

                // Stack at entry: [callee, arg0, ..., arg{argc-1}] (top = last arg)
                // Pop in reverse order to restore left-to-right layout.
                for (var j = argc - 1; j >= 0; j--)
                    b.EmitStoreLocal(argLocals[j]);
                b.EmitStoreLocal(calleeSlot);   // pop callee

                var done  = il.DefineLabel();
                var miss  = il.DefineLabel();

                // Identity guard: if runtime callee != baked callee → miss
                b.EmitLoadLocal(calleeSlot);
                b.EmitLoadData(b.AddData(callee0), typeof(ScriptVar));
                il.Emit(OpCodes.Ceq);
                il.Emit(OpCodes.Brfalse, miss);

                // Guard hit: splice the body reading parameters directly from per-arg locals
                // — no ScriptVar[] allocation on the hot path.
                var captured = ((VmFunction)callee0.GetData()).Captured;
                EmitInlinedBody(b, inlineChunk, inlineBody, captured, argLocals, argArr, tmp, calleeSlot, rSlot);
                il.Emit(OpCodes.Br, done);

                // Guard miss: build argArr, general dispatch.
                il.MarkLabel(miss);
                b.EmitNewScriptVarArray(argc);
                b.EmitStoreLocal(argArr);
                for (var j = 0; j < argc; j++)
                {
                    b.EmitLoadLocal(argArr);
                    b.EmitLdcI4(j);
                    b.EmitLoadLocal(argLocals[j]);
                    b.EmitStoreElemRef();
                }
                EmitGeneralCall(b, calleeSlot, argArr);

                il.MarkLabel(done);
                return;
            }

            // General case: build the argArray first, then try up to 2 inline guards.
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

            var done2 = il.DefineLabel();

            // One guarded inline path per inline-eligible baked callee (≤2). A callee
            // that isn't inline-eligible gets no guard — it would dispatch identically
            // to the general path, so the guard would be dead weight.
            EmitInlineGuard(b, callee0, argc, calleeSlot, argArr, tmp, rSlot, done2);
            EmitInlineGuard(b, callee1, argc, calleeSlot, argArr, tmp, rSlot, done2);

            // Fallback: dispatch on the runtime callee (handles misses + non-inlined).
            EmitGeneralCall(b, calleeSlot, argArr);
            il.MarkLabel(done2);
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
            => EmitInlinedBody(b, calleeChunk, body, captured, null, argArr, aSlot, bSlot, rSlot);

        // Overload used by the monomorphic fast path: reads parameters directly from
        // <paramref name="argLocals"/> (per-arg CLR locals) instead of from argArr,
        // eliminating the ScriptVar[] allocation entirely in the hot inline case.
        private static void EmitInlinedBody(DynamicMethodBuilder b, Chunk calleeChunk,
                                            List<JitInstruction> body, Environment captured,
                                            LocalBuilder[] argLocals,
                                            LocalBuilder argArr,
                                            LocalBuilder aSlot, LocalBuilder bSlot, LocalBuilder rSlot,
                                            LocalBuilder receiverLocal = null)
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
                        // Method inlining (Lever 2d): `this` binds to the receiver, not the
                        // callee's captured scope.
                        if (receiverLocal != null && instr.Name == "this")
                        {
                            b.EmitLoadLocal(receiverLocal);
                            break;
                        }
                        var p = calleeChunk.Parameters.IndexOf(instr.Name);
                        if (p >= 0)
                        {
                            // Use per-arg locals when available, otherwise fall back to argArr.
                            if (argLocals != null && p < argLocals.Length)
                                b.EmitLoadLocal(argLocals[p]);
                            else
                                b.EmitLoadArgElement(argArr, p);
                        }
                        else
                            b.EmitLoadNamedVarFrom(instr.Name, captured);
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

        // Method call (Lever 2d). At a monomorphic site whose observed method is
        // inline-eligible, bake the method, guard on its identity (stable across all
        // instances that share the prototype method), and splice the body with
        // this=receiver — eliminating the ScriptVar[] allocation and the InvokeCallable
        // dispatch on the hot path. Bimorphic/megamorphic or non-inlinable sites use the
        // general array + InvokeCallable path unchanged.
        private static void EmitCallMethod(DynamicMethodBuilder b, ScriptVar callee0, ScriptVar callee1,
                                           int argc, LocalBuilder argTmp, LocalBuilder calleeSlot,
                                           LocalBuilder receiverSlot, LocalBuilder argArr)
        {
            var il = b.IL;

            if (!DisableMethodInlining && callee1 == null && callee0 != null &&
                TryGetInlineBody(callee0, argc, out var inlineChunk, out var inlineBody))
            {
                // Stack at entry: [receiver, method, arg0 .. arg{argc-1}] (top = last arg).
                var argLocals = new LocalBuilder[argc];
                for (var j = 0; j < argc; j++) argLocals[j] = b.DeclareLocal(typeof(ScriptVar));
                for (var j = argc - 1; j >= 0; j--) b.EmitStoreLocal(argLocals[j]);
                b.EmitStoreLocal(calleeSlot);     // pop method
                b.EmitStoreLocal(receiverSlot);   // pop receiver

                var done = il.DefineLabel();
                var miss = il.DefineLabel();

                // Identity guard on the resolved method function.
                b.EmitLoadLocal(calleeSlot);
                b.EmitLoadData(b.AddData(callee0), typeof(ScriptVar));
                il.Emit(OpCodes.Ceq);
                il.Emit(OpCodes.Brfalse, miss);

                // Hit: splice the body with this=receiver. receiverSlot must stay live, so
                // the body's binary scratch uses argTmp/calleeSlot and a fresh local.
                var scratch = b.DeclareLocal(typeof(ScriptVar));
                var captured = ((VmFunction)callee0.GetData()).Captured;
                EmitInlinedBody(b, inlineChunk, inlineBody, captured, argLocals, argArr,
                                argTmp, calleeSlot, scratch, receiverSlot);
                il.Emit(OpCodes.Br, done);

                // Miss: rebuild argArr and dispatch InvokeCallable(method, receiver, args).
                il.MarkLabel(miss);
                b.EmitNewScriptVarArray(argc);
                b.EmitStoreLocal(argArr);
                for (var j = 0; j < argc; j++)
                {
                    b.EmitLoadLocal(argArr);
                    b.EmitLdcI4(j);
                    b.EmitLoadLocal(argLocals[j]);
                    b.EmitStoreElemRef();
                }
                b.EmitLoadVm();
                b.EmitLoadLocal(calleeSlot);
                b.EmitLoadLocal(receiverSlot);
                b.EmitLoadLocal(argArr);
                b.EmitInvokeCallable();

                il.MarkLabel(done);
                return;
            }

            // General path: array + InvokeCallable(method, receiver, args).
            b.EmitCallMethod(argc, argTmp, calleeSlot, receiverSlot, argArr);
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

            // Specializations are tried in order — int, then double — each falling
            // through to the next on a guard miss, and finally to the generic MathsOp.
            // String concatenation is deliberately NOT specialised here: it is left to
            // MathsOp, whose rope-based ConcatStrings keeps `s += x` amortised O(1) per
            // step. Emitting string.Concat inline would re-materialise the left side
            // every iteration, turning a string-building loop into O(n²).
            var dblOp = DoubleArithOp(op);
            var doubleLabel = dblOp.HasValue ? il.DefineLabel() : default;

            Label afterInt = dblOp.HasValue ? doubleLabel : fallback;

            // Int fast path guard: both operands must be integers (int32 or LargeInt).
            b.EmitIsAnyInt(aSlot);
            il.Emit(OpCodes.Brfalse, afterInt);
            b.EmitIsAnyInt(bSlot);
            il.Emit(OpCodes.Brfalse, afterInt);

            var inlineOp = InlineBitwiseIntOp(op);
            if (inlineOp.HasValue)
            {
                // Bitwise ops (&, |, ^) yield a 32-bit result by definition — emit raw.
                // result = ScriptVar.FromInt(a.Int <op> b.Int)
                b.EmitLoadInt(aSlot);
                b.EmitLoadInt(bSlot);
                il.Emit(inlineOp.Value);
                b.EmitFromInt();
            }
            else
            {
                // +, -, *, /, %, comparisons via IntBinary, which promotes +/-/* to a
                // double on 32-bit overflow (matching JS) and handles the rest.
                b.EmitIntBinaryCall(aSlot, bSlot, op, rSlot, fallback);
            }
            il.Emit(OpCodes.Br, done);

            // Double fast path: both operands numeric (and at least one a double).
            // result = ScriptVar.FromDouble(a.Float <op> b.Float). Mirrors MathsOp's
            // numeric promotion for +, -, *, /.
            if (dblOp.HasValue)
            {
                il.MarkLabel(doubleLabel);
                EmitNumericGuard(b, aSlot, fallback);
                EmitNumericGuard(b, bSlot, fallback);
                b.EmitLoadFloat(aSlot);
                b.EmitLoadFloat(bSlot);
                il.Emit(dblOp.Value);
                b.EmitFromDouble();
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

        // Bitwise int ops whose result is always a 32-bit int (JS bitwise semantics),
        // so they can be emitted as raw IL without overflow promotion — unlike +, -, *.
        private static System.Reflection.Emit.OpCode? InlineBitwiseIntOp(ScriptLex.LexTypes op) => (char)op switch
        {
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
            b.EmitIsAnyInt(slot);  // int32 or LargeInt are both numeric
            b.IL.Emit(OpCodes.Brtrue, ok);
            b.EmitIsDouble(slot);
            b.IL.Emit(OpCodes.Brfalse, onFail);
            b.IL.MarkLabel(ok);
        }
    }
}
