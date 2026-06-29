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
    /// A JIT back-end that uses <b>no</b> <see cref="System.Reflection.Emit"/> (and
    /// no runtime code generation of any kind): it composes the chunk into a tree of
    /// C# closures, each a <see cref="JitDelegate"/>, that evaluate the body when
    /// invoked. This eliminates the bytecode dispatch loop, operand decoding, and
    /// profiling overhead the interpreter pays per instruction, while remaining fully
    /// portable and <b>NativeAOT-safe</b> (unlike the Reflection.Emit back-end). Its
    /// gains are more modest than emitted IL — values stay boxed as <see cref="ScriptVar"/>
    /// and calls go through delegates — but it works anywhere.
    ///
    /// It consumes the same normalised instruction stream as the Reflection.Emit
    /// compiler (see <see cref="JitDecoder"/>), so eligibility and decoding are shared;
    /// only the lowering differs. Host code selects it via
    /// <c>JitRegistry.Register(new ClosureThreadedJitCompiler())</c>.
    /// </summary>
    public sealed class ClosureThreadedJitCompiler : IJitCompiler, IOsrCompiler
    {
        /// <summary>
        /// Kill switch for monomorphic method-body inlining on this back-end. When true,
        /// method calls use plain dispatch (CallMethodNode) instead of splicing the
        /// callee body. For A/B measurement and as a safety fallback.
        /// </summary>
        public static bool DisableMethodInlining { get; set; }

        /// <summary>
        /// Kill switch for the unboxed long-register loop tier. When true, integer loops
        /// use the boxed block compile. For A/B measurement and as a safety fallback.
        /// </summary>
        public static bool DisableLongLoop { get; set; }

        public JitDelegate Compile(Chunk chunk)
        {
            var instrs = JitDecoder.Decode(chunk);
            if (instrs == null)
                return null; // declined by the shared front-end

            var blocks = BuildBlocks(instrs, chunk.IsStrict, chunk, null, allowInline: true, out _);
            if (blocks == null)
                return null; // unsupported control flow / op — VM keeps interpreting

            var boxed = RunFromBlock(blocks, 0);

            // Unboxed long-register tier: a pure integer loop runs on raw long registers
            // with no per-iteration boxing — the closure-backend analogue of the
            // Reflection.Emit long-loop tier, and the only thing that removes boxing under
            // NativeAOT. Falls back to the boxed compile if not eligible or on a type miss.
            if (!DisableLongLoop)
            {
                var lng = TryCompileLongLoop(instrs, chunk, boxed, resumeIdx: 0, osr: false);
                if (lng != null) return lng;
            }
            return boxed;
        }

        // ── unboxed long-register loop tier ──────────────────────────────────────

        // A long-typed value-producer / side-effecting step over the register frame.
        private delegate long LongExpr(long[] regs);
        private delegate void LongStep(long[] regs);

        private sealed class LongBlock
        {
            public LongStep[] Body;
            public LongExpr Value;   // branch condition / return value
            public TermKind Kind;
            public int Target;
            public int Next;

            public static LongBlock Goto(LongStep[] b, int t) => new() { Body = b, Kind = TermKind.Jump, Target = t };
            public static LongBlock Fall(LongStep[] b, int t) => new() { Body = b, Kind = TermKind.Jump, Target = t };
            public static LongBlock Branch(LongStep[] b, LongExpr c, TermKind k, int t, int n)
                => new() { Body = b, Value = c, Kind = k, Target = t, Next = n };
            public static LongBlock Ret(LongStep[] b, LongExpr v) => new() { Body = b, Value = v, Kind = TermKind.Return };
        }

        // Operators the long tier handles: +,-,* (raw int64, wrapping — matching
        // IntBinary/IntOrDouble which also use int64) and comparisons (0/1). Division,
        // modulo, bitwise and shifts can leave the int64 domain, so they decline.
        private static bool IsLongLoopOp(ScriptLex.LexTypes op) => (char)op switch
        {
            '+' or '-' or '*' => true,
            '<' or '>' => true,
            _ => op is ScriptLex.LexTypes.Equal or ScriptLex.LexTypes.NEqual
                       or ScriptLex.LexTypes.LEqual or ScriptLex.LexTypes.GEqual,
        };

        private static long LongBinaryOp(long a, long b, ScriptLex.LexTypes op)
        {
            switch ((char)op)
            {
                case '+': return a + b;
                case '-': return a - b;
                case '*': return a * b;
                case '<': return a <  b ? 1L : 0L;
                case '>': return a >  b ? 1L : 0L;
            }
            return op switch
            {
                ScriptLex.LexTypes.Equal  => a == b ? 1L : 0L,
                ScriptLex.LexTypes.NEqual => a != b ? 1L : 0L,
                ScriptLex.LexTypes.LEqual => a <= b ? 1L : 0L,
                _                         => a >= b ? 1L : 0L, // GEqual
            };
        }

        // A long-tier operand: either a compile-time constant or a runtime expression.
        // Carrying constness lets the binary/not builders fold const-const operations and
        // capture a constant operand directly into the LongBinaryOp call — removing the
        // extra `_ => c` delegate dispatch the operand would otherwise cost every
        // iteration of a hot loop.
        private readonly struct LongVal
        {
            public readonly LongExpr Fn;   // null when IsConst
            public readonly long Const;
            public readonly bool IsConst;
            private LongVal(LongExpr fn, long c, bool isConst) { Fn = fn; Const = c; IsConst = isConst; }
            public static LongVal Expr(LongExpr fn) => new(fn, 0, false);
            public static LongVal Lit(long c) => new(null, c, true);
            public LongExpr AsExpr() { if (!IsConst) return Fn; var c = Const; return _ => c; }
        }

        // Build a binary node, folding when both operands are constant and specialising
        // when exactly one is — so a constant operand is baked into the LongBinaryOp call
        // rather than reached through its own delegate.
        private static LongVal LongBinary(LongVal l, LongVal r, ScriptLex.LexTypes op)
        {
            if (l.IsConst && r.IsConst) return LongVal.Lit(LongBinaryOp(l.Const, r.Const, op));
            if (r.IsConst) { var a = l.Fn; var c = r.Const; return LongVal.Expr(x => LongBinaryOp(a(x), c, op)); }
            if (l.IsConst) { var c = l.Const; var b = r.Fn; return LongVal.Expr(x => LongBinaryOp(c, b(x), op)); }
            { var a = l.Fn; var b = r.Fn; return LongVal.Expr(x => LongBinaryOp(a(x), b(x), op)); }
        }

        private static LongVal LongNot(LongVal v)
        {
            if (v.IsConst) return LongVal.Lit(v.Const == 0 ? 1L : 0L);
            var x = v.Fn;
            return LongVal.Expr(r => x(r) == 0 ? 1L : 0L);
        }

        // An int constant, or a double constant that is an exact integer within 2^53
        // (e.g. a loop bound like 1e7). Mirrors the Reflection.Emit tier's TryConstAsLong.
        private static bool TryConstLong(ConstantValue c, out long value)
        {
            if (c.Kind == ConstantKind.Int) { value = c.IntValue; return true; }
            if (c.Kind == ConstantKind.Double)
            {
                var d = c.DoubleValue;
                if (!double.IsNaN(d) && !double.IsInfinity(d) && d == System.Math.Floor(d)
                    && System.Math.Abs(d) < 9007199254740992.0) { value = (long)d; return true; }
            }
            value = 0;
            return false;
        }

        // Compile a pure integer-loop function into a closure that runs on raw long
        // registers. Eligibility mirrors the Reflection.Emit long-loop tier: every op is
        // an int arithmetic/comparison/var/jump/return; every variable is a parameter
        // (guarded int at entry) or a local first written before any read (so it is
        // unconditionally an int by the time it is read). A guard miss runs the boxed
        // fallback. Returns null to decline.
        private static JitDelegate TryCompileLongLoop(List<JitInstruction> instrs, Chunk chunk,
                                                      JitDelegate fallback, int resumeIdx, bool osr)
        {
            var n = instrs.Count;
            if (n == 0 || instrs[n - 1].Kind != JitOpKind.Return) return null;

            // Monomorphic int-leaf calls are inlined as substituted expressions; identify
            // them (and the callee-push instructions they elide) up front. Any other call
            // declines the long tier.
            var calleeSkip = new HashSet<int>();
            var inlineAt = new Dictionary<int, (System.Collections.Generic.List<string> ps, List<JitInstruction> body)>();
            var calleeGuards = new System.Collections.Generic.List<(string name, ScriptVar baked)>();

            // Nested function declarations (MakeClosure ; SetVarPop name) — e.g. a helper
            // defined inside the function being compiled. These are inlined from their
            // compile-time chunk rather than a profiled runtime callee, because each
            // invocation creates a fresh closure instance: identity-based baking would
            // miss every time. Resolution is safe only for a name declared exactly once
            // and never reassigned (so every call targets that declaration). The make +
            // store of such a name are elided from the long body. Any other (ambiguous or
            // reassigned) declaration is left unrecognised, so its MakeClosure declines.
            var decl = new Dictionary<string, (int make, int store)>();
            var dupName = new HashSet<string>();
            for (var i = 0; i + 1 < n; i++)
                if (instrs[i].Kind == JitOpKind.MakeClosure && instrs[i].Closure != null
                    && instrs[i + 1].Kind == JitOpKind.SetVarPop && instrs[i + 1].Name != null)
                {
                    var nm = instrs[i + 1].Name;
                    if (!decl.TryAdd(nm, (i, i + 1))) dupName.Add(nm);
                }
            var reassigned = new HashSet<string>();
            if (decl.Count > 0)
                for (var i = 0; i < n; i++)
                {
                    var k = instrs[i].Kind;
                    if ((k is JitOpKind.SetVar or JitOpKind.SetVarPop) && instrs[i].Name != null
                        && decl.TryGetValue(instrs[i].Name, out var d) && i != d.store)
                        reassigned.Add(instrs[i].Name);
                }
            var nestedChunk = new Dictionary<string, Chunk>();
            var nestedSkip = new HashSet<int>();
            foreach (var kv in decl)
            {
                if (dupName.Contains(kv.Key) || reassigned.Contains(kv.Key)) continue;
                nestedChunk[kv.Key] = instrs[kv.Value.make].Closure;
                nestedSkip.Add(kv.Value.make);
                nestedSkip.Add(kv.Value.store);
            }

            if (!AnalyzeLongInlineCalls(instrs, calleeSkip, inlineAt, calleeGuards, nestedChunk, nestedSkip)) return null;

            // A nested callee whose creation we elide must be used only as an inlined call;
            // if its name is read as a value anywhere else (passed, returned), its closure
            // is observable and cannot be dropped — decline.
            foreach (var ii in nestedSkip) calleeSkip.Add(ii);
            for (var i = 0; i < n; i++)
                if (instrs[i].Kind == JitOpKind.PushVar && instrs[i].Name != null
                    && nestedChunk.ContainsKey(instrs[i].Name) && !calleeSkip.Contains(i))
                    return null;

            // An inlined free/global callee is baked by identity; if its variable is
            // reassigned inside this function the inline could go stale mid-loop, so decline
            // (the entry guard below only covers reassignment between invocations). Nested
            // callees are resolved by chunk, not identity, so they are exempt.
            if (calleeGuards.Count > 0)
                foreach (var ins in instrs)
                    if (ins.Kind is JitOpKind.SetVar or JitOpKind.SetVarPop)
                        foreach (var g in calleeGuards)
                            if (g.name == ins.Name) return null;

            // Collect register variables (named vars and positional slots) and validate
            // the op set. regInfo[i] records how register i is loaded from the frame.
            var reg = new Dictionary<string, int>();
            var regInfo = new List<(bool isSlot, int slot, string name)>();
            var sawBinary = false;
            int GetReg(bool isSlot, int slot, string name)
            {
                var key = isSlot ? "@" + slot : name;
                if (!reg.TryGetValue(key, out var idx)) { idx = reg.Count; reg[key] = idx; regInfo.Add((isSlot, slot, name)); }
                return idx;
            }
            for (var ii = 0; ii < n; ii++)
            {
                if (calleeSkip.Contains(ii)) continue; // elided int-leaf callee ref / nested make+store
                var ins = instrs[ii];
                switch (ins.Kind)
                {
                    case JitOpKind.Binary:
                        if (!IsLongLoopOp(ins.Op)) return null;
                        sawBinary = true;
                        break;
                    case JitOpKind.PushConst:
                        if (!TryConstLong(ins.Constant, out _)) return null;
                        break;
                    case JitOpKind.PushVar:
                        GetReg(false, 0, ins.Name);
                        break;
                    case JitOpKind.SetVar:
                    case JitOpKind.SetVarPop:
                        GetReg(false, 0, ins.Name);
                        break;
                    case JitOpKind.GetLocal:
                    case JitOpKind.SetLocal:
                        GetReg(true, ins.IntValue, null);
                        break;
                    case JitOpKind.EnterBlock:
                    case JitOpKind.LeaveBlock:
                        // A block scope is a no-op for the register frame only when its
                        // locals are flat positional slots; without slots they are named
                        // vars in a child environment the register model does not track.
                        if (!ScriptEngine.EnableLocalSlots) return null;
                        break;
                    case JitOpKind.Call:                       // inlinable int leaf (validated above)
                    case JitOpKind.PushIntLiteral:
                    case JitOpKind.DeclareVar:
                    case JitOpKind.DeclareLocal:
                    case JitOpKind.DeclareConst:
                    case JitOpKind.Not:
                    case JitOpKind.Pop:
                    case JitOpKind.Jump:
                    case JitOpKind.JumpIfFalse:
                    case JitOpKind.JumpIfTrue:
                    case JitOpKind.Return:
                        break;
                    default:
                        return null; // properties, indexing, shifts, etc.
                }
            }
            if (!sawBinary) return null;

            // Slot registers are only supported on the OSR entry, where every live slot is
            // loaded and guarded from the frame. On a whole-function entry the param→slot
            // mapping isn't modelled here, so decline (the boxed compile handles it).
            if (!osr)
                foreach (var ri in regInfo)
                    if (ri.isSlot) return null;

            // Binary sites must all be profiled numeric — never strings or objects. A double
            // operand is admitted (e.g. a loop bound written as 1e7): the entry guards load
            // only genuine integers (a real double register fails IsAnyInt and falls back),
            // and every constant is integral (eligibility requires TryConstLong), so every
            // operand is a long at runtime regardless of the profiled double. At least one
            // integer operand must have been seen, so a purely floating-point loop (which
            // would always miss the entry guard) is declined cheaply rather than compiled.
            var profiles = chunk.GetBinaryOpProfiles();
            if (profiles.Count == 0) return null;
            const Chunk.BinaryTypeFlags nonNumeric = Chunk.BinaryTypeFlags.String | Chunk.BinaryTypeFlags.Other;
            var sawInt = false;
            foreach (var (_, p) in profiles)
            {
                if (((p.LeftTypes | p.RightTypes) & nonNumeric) != 0) return null;
                if (((p.LeftTypes | p.RightTypes) & Chunk.BinaryTypeFlags.Int) != 0) sawInt = true;
            }
            if (!sawInt) return null;

            // Register-promotion soundness (whole-function entry only): a non-parameter
            // variable's first reference must be a write preceding any jump, so it is
            // initialised before any read. Under OSR every register is instead loaded and
            // guarded from the live frame at the resume point, so this is unnecessary.
            if (!osr)
            {
                var firstJump = n;
                for (var i = 0; i < n; i++)
                    if (instrs[i].Kind is JitOpKind.Jump or JitOpKind.JumpIfFalse or JitOpKind.JumpIfTrue) { firstJump = i; break; }
                var firstRef = new Dictionary<string, (int idx, bool write)>();
                for (var i = 0; i < n; i++)
                {
                    if (calleeSkip.Contains(i)) continue; // elided callee ref, not a register
                    var ins = instrs[i];
                    if (ins.Name == null || !(ins.Kind is JitOpKind.PushVar or JitOpKind.SetVar or JitOpKind.SetVarPop)) continue;
                    if (!firstRef.ContainsKey(ins.Name))
                        firstRef[ins.Name] = (i, ins.Kind is JitOpKind.SetVar or JitOpKind.SetVarPop);
                }
                foreach (var kv in firstRef)
                {
                    if (chunk.Parameters.Contains(kv.Key)) continue; // params guarded at entry
                    if (!kv.Value.write || kv.Value.idx >= firstJump) return null;
                }
            }

            // ── partition into basic blocks (same leader logic as BuildBlocks) ──────
            var isLeader = new bool[n];
            isLeader[0] = true;
            for (var i = 0; i < n; i++)
            {
                var k = instrs[i].Kind;
                if (k is JitOpKind.Jump or JitOpKind.JumpIfFalse or JitOpKind.JumpIfTrue)
                {
                    var t = instrs[i].IntValue;
                    if (t < 0 || t >= n) return null;
                    isLeader[t] = true;
                    if (i + 1 < n) isLeader[i + 1] = true;
                }
                else if (k == JitOpKind.Return && i + 1 < n) isLeader[i + 1] = true;
            }
            var idx2blk = new Dictionary<int, int>();
            var starts = new List<int>();
            for (var i = 0; i < n; i++) if (isLeader[i]) { idx2blk[i] = starts.Count; starts.Add(i); }

            var blocks = new LongBlock[starts.Count];
            for (var b = 0; b < starts.Count; b++)
            {
                var start = starts[b];
                var end = b + 1 < starts.Count ? starts[b + 1] : n;
                var lb = CompileLongBlock(instrs, start, end, idx2blk, reg, calleeSkip, inlineAt);
                if (lb == null) return null;
                blocks[b] = lb;
            }

            if (!idx2blk.TryGetValue(resumeIdx, out var startBlock)) return null;

            // Registers loaded + guarded at entry: under OSR every live register comes
            // from the resume frame; for a whole-function entry only the (named) parameters
            // do (non-parameter locals start at 0 and are write-first).
            var entryRegs = new List<(bool isSlot, int slot, string name, int idx)>();
            for (var idx = 0; idx < regInfo.Count; idx++)
            {
                var ri = regInfo[idx];
                if (osr || (!ri.isSlot && chunk.Parameters.Contains(ri.name)))
                    entryRegs.Add((ri.isSlot, ri.slot, ri.name, idx));
            }
            var regCount = reg.Count;

            return (vm, args, env) =>
            {
                // Inlined callees are baked by identity; if a callee variable now resolves
                // to a different function (reassigned between invocations) run the boxed
                // path so the new function executes.
                foreach (var (name, baked) in calleeGuards)
                    if (!ReferenceEquals(VirtualMachine.JitGetVar(env, name), baked))
                        return fallback(vm, args, env);

                var regs = new long[regCount];
                foreach (var (isSlot, slot, name, idx) in entryRegs)
                {
                    var sv = isSlot ? env.Slots[slot] : VirtualMachine.JitGetVar(env, name);
                    if (sv == null || !sv.IsAnyInt) return fallback(vm, args, env); // type miss -> boxed path
                    regs[idx] = sv.Long;
                }
                var bb = startBlock;
                while (true)
                {
                    var blk = blocks[bb];
                    var steps = blk.Body;
                    for (var i = 0; i < steps.Length; i++) steps[i](regs);
                    switch (blk.Kind)
                    {
                        case TermKind.Return: return ScriptVar.FromLong(blk.Value(regs));
                        case TermKind.Jump: bb = blk.Target; break;
                        case TermKind.BranchFalse: bb = blk.Value(regs) != 0 ? blk.Next : blk.Target; break;
                        default: bb = blk.Value(regs) != 0 ? blk.Target : blk.Next; break; // BranchTrue
                    }
                }
            };
        }

        // A straight-line, parameter-only integer leaf callee (e.g. f(a,b,c){return a+b+c})
        // that the long tier can splice as a single substituted expression. Control flow
        // in the callee is declined here (the boxed path handles those).
        private static bool TryIntLeafLong(ScriptVar callee, int argc,
                                           out System.Collections.Generic.List<string> ps,
                                           out List<JitInstruction> body)
        {
            ps = null; body = null;
            if (callee == null || !callee.IsFunction || callee.IsNative) return false;
            if (callee.GetData() is not VmFunction vmfn) return false;
            return TryIntLeafLongChunk(vmfn.Body, argc, out ps, out body);
        }

        // The chunk-level core of TryIntLeafLong. Resolving a callee straight from its
        // compiled chunk (rather than a live ScriptVar) is what lets a nested function
        // declaration be inlined: the body reads only its parameters (free vars are
        // rejected below), so every per-invocation closure instance is behaviourally
        // identical and no runtime identity guard is needed.
        private static bool TryIntLeafLongChunk(Chunk c, int argc,
                                                out System.Collections.Generic.List<string> ps,
                                                out List<JitInstruction> body)
        {
            ps = null; body = null;
            if (c.MakesClosure || c.IsGenerator || c.IsAsync) return false;
            if (c.RestParamIndex != -1 || c.Parameters.Count != argc) return false;
            var instrs = JitDecoder.Decode(c);
            if (instrs == null || instrs.Count == 0 || instrs[instrs.Count - 1].Kind != JitOpKind.Return) return false;
            foreach (var ins in instrs)
            {
                switch (ins.Kind)
                {
                    case JitOpKind.PushIntLiteral:
                    case JitOpKind.Not:
                    case JitOpKind.Return:
                        break;
                    case JitOpKind.PushConst:
                        if (!TryConstLong(ins.Constant, out _)) return false;
                        break;
                    case JitOpKind.PushVar:
                        if (!c.Parameters.Contains(ins.Name)) return false; // free var
                        break;
                    case JitOpKind.GetLocal:
                        if (ins.IntValue < 0 || ins.IntValue >= argc) return false; // param slots only
                        break;
                    case JitOpKind.Binary:
                        if (!IsLongLoopOp(ins.Op)) return false;
                        break;
                    default:
                        return false; // jumps/calls/etc. → not a straight-line int leaf
                }
            }
            ps = c.Parameters;
            body = instrs;
            return true;
        }

        // Build the callee's result as one LongExpr, substituting each argument expression
        // for the corresponding parameter read (the callee reads only its parameters).
        private static LongVal BuildInlinedLong(List<JitInstruction> body, LongVal[] argExprs,
                                                System.Collections.Generic.List<string> ps)
        {
            var pidx = new Dictionary<string, int>(ps.Count);
            for (var i = 0; i < ps.Count; i++) pidx[ps[i]] = i;
            var stack = new Stack<LongVal>();
            foreach (var ins in body)
            {
                switch (ins.Kind)
                {
                    case JitOpKind.PushConst:      { TryConstLong(ins.Constant, out var c); stack.Push(LongVal.Lit(c)); break; }
                    case JitOpKind.PushIntLiteral: stack.Push(LongVal.Lit(ins.IntValue)); break;
                    case JitOpKind.PushVar:        stack.Push(argExprs[pidx[ins.Name]]); break;
                    case JitOpKind.GetLocal:       stack.Push(argExprs[ins.IntValue]); break; // param slot = arg index
                    case JitOpKind.Not:            stack.Push(LongNot(stack.Pop())); break;
                    case JitOpKind.Binary:         { var rr = stack.Pop(); var ll = stack.Pop(); stack.Push(LongBinary(ll, rr, ins.Op)); break; }
                    case JitOpKind.Return:         return stack.Pop();
                }
            }
            return stack.Pop();
        }

        // Find inlinable monomorphic int-leaf calls and the callee-push instructions they
        // elide, by simulating the operand stack of producer indices. Returns false if any
        // call is not an inlinable int leaf (the long tier then declines).
        private static bool AnalyzeLongInlineCalls(List<JitInstruction> instrs, HashSet<int> calleeSkip,
            Dictionary<int, (System.Collections.Generic.List<string> ps, List<JitInstruction> body)> inlineAt,
            System.Collections.Generic.List<(string name, ScriptVar baked)> calleeGuards,
            Dictionary<string, Chunk> nestedChunk, HashSet<int> nestedSkip)
        {
            var prod = new List<int>();
            for (var i = 0; i < instrs.Count; i++)
            {
                var ins = instrs[i];
                switch (ins.Kind)
                {
                    case JitOpKind.MakeClosure:
                        // Only a recognised nested int-leaf declaration (make+store) is
                        // allowed; its push is consumed by the following store. Any other
                        // closure construction declines the tier.
                        if (!nestedSkip.Contains(i)) return false;
                        prod.Add(i);
                        break;
                    case JitOpKind.PushConst:
                    case JitOpKind.PushIntLiteral:
                    case JitOpKind.PushVar:
                    case JitOpKind.GetLocal:
                        prod.Add(i);
                        break;
                    case JitOpKind.Not:
                        if (prod.Count > 0) prod[prod.Count - 1] = i;
                        break;
                    case JitOpKind.Binary:
                        if (prod.Count >= 2) { prod.RemoveAt(prod.Count - 1); prod[prod.Count - 1] = i; }
                        break;
                    case JitOpKind.SetVar:
                    case JitOpKind.SetLocal:
                        if (prod.Count > 0) prod[prod.Count - 1] = i; // pop value, push value
                        break;
                    case JitOpKind.SetVarPop:
                    case JitOpKind.JumpIfFalse:
                    case JitOpKind.JumpIfTrue:
                    case JitOpKind.Return:
                    case JitOpKind.Pop:
                        if (prod.Count > 0) prod.RemoveAt(prod.Count - 1);
                        break;
                    case JitOpKind.DeclareVar:
                    case JitOpKind.DeclareLocal:
                    case JitOpKind.DeclareConst:
                    case JitOpKind.Jump:
                        break;
                    case JitOpKind.EnterBlock:
                    case JitOpKind.LeaveBlock:
                        // No stack effect; admitted only under slots (see eligibility scan).
                        if (!ScriptEngine.EnableLocalSlots) return false;
                        break;
                    case JitOpKind.Call:
                    {
                        var argc = ins.IntValue;
                        if (prod.Count < argc + 1) return false;
                        var calleeProducer = prod[prod.Count - argc - 1];
                        if (instrs[calleeProducer].Kind != JitOpKind.PushVar) return false;
                        System.Collections.Generic.List<string> ps; List<JitInstruction> body;
                        // A nested function declared in this function is resolved from its
                        // compile-time chunk; its body is pure (no free vars) so no runtime
                        // identity guard is needed — unlike a free/global callee, which is
                        // baked by identity and re-checked at entry.
                        if (nestedChunk.TryGetValue(instrs[calleeProducer].Name, out var nc))
                        {
                            if (!TryIntLeafLongChunk(nc, argc, out ps, out body)) return false;
                        }
                        else
                        {
                            if (ins.MonoCallee == null || ins.MonoCallee1 != null) return false;
                            if (!TryIntLeafLong(ins.MonoCallee, argc, out ps, out body)) return false;
                            calleeGuards.Add((instrs[calleeProducer].Name, ins.MonoCallee));
                        }
                        inlineAt[i] = (ps, body);
                        calleeSkip.Add(calleeProducer);
                        for (var k = 0; k < argc + 1; k++) prod.RemoveAt(prod.Count - 1);
                        prod.Add(i);
                        break;
                    }
                    default:
                        return false;
                }
            }
            return true;
        }

        private static LongBlock CompileLongBlock(List<JitInstruction> instrs, int start, int end,
                                                  Dictionary<int, int> idx2blk, Dictionary<string, int> reg,
                                                  HashSet<int> calleeSkip,
                                                  Dictionary<int, (System.Collections.Generic.List<string> ps, List<JitInstruction> body)> inlineAt)
        {
            var stack = new Stack<LongVal>();
            var body = new List<LongStep>();
            for (var i = start; i < end; i++)
            {
                if (calleeSkip.Contains(i)) continue; // elided callee push (inlined int leaf)
                var instr = instrs[i];
                if (instr.Kind == JitOpKind.Jump)
                {
                    if (stack.Count != 0) return null;
                    return LongBlock.Goto(body.ToArray(), idx2blk[instr.IntValue]);
                }
                if (instr.Kind == JitOpKind.JumpIfFalse)
                {
                    if (stack.Count != 1) return null;
                    return LongBlock.Branch(body.ToArray(), stack.Pop().AsExpr(), TermKind.BranchFalse, idx2blk[instr.IntValue], idx2blk[i + 1]);
                }
                if (instr.Kind == JitOpKind.JumpIfTrue)
                {
                    if (stack.Count != 1) return null;
                    return LongBlock.Branch(body.ToArray(), stack.Pop().AsExpr(), TermKind.BranchTrue, idx2blk[instr.IntValue], idx2blk[i + 1]);
                }
                if (instr.Kind == JitOpKind.Return)
                {
                    if (stack.Count != 1) return null;
                    return LongBlock.Ret(body.ToArray(), stack.Pop().AsExpr());
                }
                switch (instr.Kind)
                {
                    case JitOpKind.PushConst:      { TryConstLong(instr.Constant, out var c); stack.Push(LongVal.Lit(c)); break; }
                    case JitOpKind.PushIntLiteral: stack.Push(LongVal.Lit(instr.IntValue)); break;
                    case JitOpKind.PushVar:        { var idx = reg[instr.Name]; stack.Push(LongVal.Expr(r => r[idx])); break; }
                    case JitOpKind.GetLocal:       { var idx = reg["@" + instr.IntValue]; stack.Push(LongVal.Expr(r => r[idx])); break; }
                    case JitOpKind.SetVar:         { var idx = reg[instr.Name]; var val = stack.Pop().AsExpr(); stack.Push(LongVal.Expr(r => { var t = val(r); r[idx] = t; return t; })); break; }
                    case JitOpKind.SetLocal:       { var idx = reg["@" + instr.IntValue]; var val = stack.Pop().AsExpr(); stack.Push(LongVal.Expr(r => { var t = val(r); r[idx] = t; return t; })); break; }
                    case JitOpKind.SetVarPop:      { var idx = reg[instr.Name]; var val = stack.Pop().AsExpr(); body.Add(r => r[idx] = val(r)); break; }
                    case JitOpKind.DeclareVar:
                    case JitOpKind.DeclareLocal:
                    case JitOpKind.DeclareConst:   break;
                    // Block scopes are no-ops on the register frame: under slots, block-scoped
                    // locals are flat positional slots in the same frame (the eligibility scan
                    // only admits these when slots are enabled), so no env push/pop is needed.
                    case JitOpKind.EnterBlock:
                    case JitOpKind.LeaveBlock:     break;
                    case JitOpKind.Not:            stack.Push(LongNot(stack.Pop())); break;
                    case JitOpKind.Binary:         { var rr = stack.Pop(); var ll = stack.Pop(); stack.Push(LongBinary(ll, rr, instr.Op)); break; }
                    // Discarding a pure constant has no effect; only a runtime expression
                    // can carry a side effect worth keeping as a body step.
                    case JitOpKind.Pop:            { var x = stack.Pop(); if (!x.IsConst) { var xe = x.Fn; body.Add(r => xe(r)); } break; }
                    case JitOpKind.Call:
                    {
                        var argc = instr.IntValue;
                        var argExprs = new LongVal[argc];
                        for (var j = argc - 1; j >= 0; j--) argExprs[j] = stack.Pop();
                        var (ps, cbody) = inlineAt[i];
                        stack.Push(BuildInlinedLong(cbody, argExprs, ps));
                        break;
                    }
                    default: return null;
                }
            }
            if (stack.Count != 0 || end >= instrs.Count) return null;
            return LongBlock.Fall(body.ToArray(), idx2blk[end]);
        }

        // On-stack replacement: compile the chunk and resume execution at the basic
        // block whose leader is <paramref name="resumeOffset"/> (a loop header). Live
        // locals flow through the shared <c>env</c>; the operand stack is empty at a
        // structured back-edge, so nothing else needs migrating.
        public JitDelegate CompileOsr(Chunk chunk, int resumeOffset)
        {
            var instrs = JitDecoder.Decode(chunk);
            if (instrs == null)
                return null;

            var resumeIdx = JitDecoder.OffsetToInstructionIndex(chunk, resumeOffset);
            if (resumeIdx < 0)
                return null;

            var blocks = BuildBlocks(instrs, chunk.IsStrict, chunk, null, allowInline: true, out var idxToBlock);
            if (blocks == null)
                return null;

            // The resume point must coincide with a block leader (loop headers are
            // jump targets, so they always are — but decline defensively otherwise).
            if (!idxToBlock.TryGetValue(resumeIdx, out var startBlock))
                return null;

            var boxed = RunFromBlock(blocks, startBlock);

            // Unboxed long-register tier resuming at the loop header: every live register
            // is loaded and guarded from the frame, then the loop runs on raw longs.
            if (!DisableLongLoop)
            {
                var lng = TryCompileLongLoop(instrs, chunk, boxed, resumeIdx, osr: true);
                if (lng != null) return lng;
            }
            return boxed;
        }

        // Partition the decoded instruction stream into basic blocks. Block leaders
        // are index 0, every jump target, and every instruction following a branch or
        // return. Each block is compiled with the shared expression builder
        // (<see cref="CompileBlock"/>); the operand stack is empty at every structured
        // block boundary, so blocks compose without migrating stack state. Returns
        // null to decline the whole chunk (unsupported op, or a non-empty operand
        // stack across a boundary — e.g. short-circuit operators).
        // <paramref name="currentChunk"/> is the chunk being compiled (to bar inlining
        // a self-recursive call); <paramref name="paramMap"/> maps parameter names to
        // positional <c>args</c> slots when compiling an inlined callee body (null for a
        // normal env-based compile); <paramref name="allowInline"/> permits inlining
        // monomorphic calls (false inside an already-inlined body, bounding depth).
        private static Block[] BuildBlocks(List<JitInstruction> instrs, bool strict, Chunk currentChunk,
                                           Dictionary<string, int> paramMap, bool allowInline,
                                           out Dictionary<int, int> idxToBlock)
        {
            idxToBlock = null;
            var n = instrs.Count;
            if (n == 0) return null;

            // Decline control-flow / opcodes the block model does not yet handle:
            // short-circuit and optional-chaining jumps keep a value live across the
            // boundary. (Block scopes — EnterBlock/LeaveBlock — are supported via the
            // driver's threaded current environment. Method calls — GetPropMethod /
            // GetPropCall0 / CallMethod — are handled in CompileBlock via a pending-method
            // stack that evaluates the receiver exactly once; see CallMethodNode.)
            foreach (var instr in instrs)
                if (instr.Kind is JitOpKind.JumpIfFalseOrPop or JitOpKind.JumpIfTrueOrPop
                    or JitOpKind.JumpIfNullOrUndefined or JitOpKind.JumpIfDefined)
                    return null;

            // 1. Mark leaders.
            var isLeader = new bool[n];
            isLeader[0] = true;
            for (var i = 0; i < n; i++)
            {
                var k = instrs[i].Kind;
                if (k is JitOpKind.Jump or JitOpKind.JumpIfFalse or JitOpKind.JumpIfTrue)
                {
                    var target = instrs[i].IntValue;
                    if (target < 0 || target >= n) return null;
                    isLeader[target] = true;
                    if (i + 1 < n) isLeader[i + 1] = true;
                }
                else if (k == JitOpKind.Return && i + 1 < n)
                {
                    isLeader[i + 1] = true;
                }
            }

            // 2. Number the blocks and map each leader index to its block index.
            var map = new Dictionary<int, int>();
            var starts = new List<int>();
            for (var i = 0; i < n; i++)
                if (isLeader[i]) { map[i] = starts.Count; starts.Add(i); }

            // 3. Compile each block.
            var blocks = new Block[starts.Count];
            for (var b = 0; b < starts.Count; b++)
            {
                var start = starts[b];
                var end = b + 1 < starts.Count ? starts[b + 1] : n;
                var blk = CompileBlock(instrs, start, end, strict, map, currentChunk, paramMap, allowInline);
                if (blk == null) return null;
                blocks[b] = blk;
            }

            idxToBlock = map;
            return blocks;
        }

        // Compile one basic block [start, end). All but the terminating instruction
        // are folded into the shared expression builder; the terminator (Jump /
        // JumpIfFalse / JumpIfTrue / Return) decides the successor block(s). The
        // operand stack must be empty at a Jump/fallthrough boundary and hold exactly
        // the branch condition / return value at a conditional/return boundary —
        // anything else declines the chunk.
        private static Block CompileBlock(List<JitInstruction> instrs, int start, int end,
                                          bool strict, Dictionary<int, int> idx2blk, Chunk currentChunk,
                                          Dictionary<string, int> paramMap, bool allowInline)
        {
            var stack = new Stack<JitDelegate>();
            var body = new List<BodyStep>();
            // A method call's receiver is stashed here by GetPropMethod and consumed by
            // the matching CallMethod, so the receiver expression is evaluated exactly
            // once (no double-eval of e.g. getObj().m()). A method call never spans a
            // block boundary in compilable code (control-flow args push operand depth >1
            // at the branch, which this back-end already declines), so this stays balanced
            // within a block — enforced by the pendingMethods checks at every terminator.
            var pendingMethods = new Stack<(JitDelegate recv, string name)>();

            for (var i = start; i < end; i++)
            {
                var instr = instrs[i];

                // Terminators end the block.
                if (instr.Kind == JitOpKind.Jump)
                {
                    if (stack.Count != 0 || pendingMethods.Count != 0) return null;
                    return Block.Goto(body.ToArray(), idx2blk[instr.IntValue]);
                }
                if (instr.Kind == JitOpKind.JumpIfFalse)
                {
                    if (stack.Count != 1 || pendingMethods.Count != 0) return null;
                    return Block.Branch(body.ToArray(), stack.Pop(), TermKind.BranchFalse,
                                        idx2blk[instr.IntValue], idx2blk[i + 1]);
                }
                if (instr.Kind == JitOpKind.JumpIfTrue)
                {
                    if (stack.Count != 1 || pendingMethods.Count != 0) return null;
                    return Block.Branch(body.ToArray(), stack.Pop(), TermKind.BranchTrue,
                                        idx2blk[instr.IntValue], idx2blk[i + 1]);
                }
                if (instr.Kind == JitOpKind.Return)
                {
                    if (stack.Count != 1 || pendingMethods.Count != 0) return null;
                    return Block.Ret(body.ToArray(), stack.Pop());
                }

                switch (instr.Kind)
                {
                    case JitOpKind.PushConst:      stack.Push(ConstNode(instr.Constant)); break;
                    case JitOpKind.PushIntLiteral: stack.Push(IntLitNode(instr.IntValue)); break;
                    case JitOpKind.PushVar:
                        stack.Push(paramMap != null && paramMap.TryGetValue(instr.Name, out var pvi)
                            ? ParamNode(pvi) : VarNode(instr.Name));
                        break;
                    // Inside an inlined callee (paramMap != null) all slots are param
                    // slots (callees with local slots are declined), so a slot access is
                    // a positional argument read/write; otherwise it is a frame slot.
                    case JitOpKind.GetLocal:
                        stack.Push(paramMap != null ? ParamNode(instr.IntValue) : SlotGetNode(instr.IntValue));
                        break;
                    case JitOpKind.SetLocal:
                        stack.Push(paramMap != null
                            ? SetParamNode(instr.IntValue, stack.Pop())
                            : SlotSetNode(instr.IntValue, stack.Pop()));
                        break;
                    case JitOpKind.GetProp:        stack.Push(GetPropNode(stack.Pop(), instr.Name)); break;
                    case JitOpKind.PushNull:       stack.Push(NullNode()); break;
                    case JitOpKind.PushUndefined:  stack.Push(UndefinedNode()); break;
                    case JitOpKind.Not:            stack.Push(NotNode(stack.Pop())); break;
                    case JitOpKind.Negate:         stack.Push(UnaryNode(stack.Pop(), VirtualMachine.JitNegate)); break;
                    case JitOpKind.BitNot:         stack.Push(UnaryNode(stack.Pop(), VirtualMachine.JitBitNot)); break;
                    case JitOpKind.Typeof:         stack.Push(UnaryNode(stack.Pop(), VirtualMachine.JitTypeof)); break;
                    case JitOpKind.ToNumber:       stack.Push(UnaryNode(stack.Pop(), VirtualMachine.JitToNumber)); break;
                    case JitOpKind.GetIndex:
                    {
                        var key = stack.Pop();
                        var obj = stack.Pop();
                        stack.Push(GetIndexNode(obj, key));
                        break;
                    }
                    case JitOpKind.SetIndex:
                    {
                        var value = stack.Pop();
                        var key = stack.Pop();
                        var obj = stack.Pop();
                        stack.Push(SetIndexNode(obj, key, value, strict));
                        break;
                    }
                    case JitOpKind.Shift:
                    {
                        var right = stack.Pop();
                        var left = stack.Pop();
                        stack.Push(ShiftNode(left, right, instr.Op));
                        break;
                    }
                    case JitOpKind.Binary:
                    {
                        var right = stack.Pop();
                        var left = stack.Pop();
                        stack.Push(BinaryNode(left, right, instr.Op));
                        break;
                    }
                    case JitOpKind.Call:
                    {
                        var argNodes = new JitDelegate[instr.IntValue];
                        for (var j = instr.IntValue - 1; j >= 0; j--)
                            argNodes[j] = stack.Pop();
                        var callee = stack.Pop();
                        var inlined = allowInline ? TryInlineCall(instr, callee, argNodes, currentChunk) : null;
                        stack.Push(inlined ?? CallNode(callee, argNodes));
                        break;
                    }
                    case JitOpKind.New:
                    {
                        var argNodes = new JitDelegate[instr.IntValue];
                        for (var j = instr.IntValue - 1; j >= 0; j--)
                            argNodes[j] = stack.Pop();
                        var ctor = stack.Pop();
                        stack.Push(NewNode(ctor, argNodes));
                        break;
                    }
                    case JitOpKind.GetPropMethod:
                        // Stash the receiver; the matching CallMethod evaluates it once.
                        pendingMethods.Push((stack.Pop(), instr.Name));
                        break;
                    case JitOpKind.GetPropCall0:
                        // Fused zero-argument method call; self-contained (no pending).
                        stack.Push(CallMethodNode(stack.Pop(), instr.Name, System.Array.Empty<JitDelegate>()));
                        break;
                    case JitOpKind.CallMethod:
                    {
                        if (pendingMethods.Count == 0) return null; // method call spans a block boundary
                        var margs = new JitDelegate[instr.IntValue];
                        for (var j = instr.IntValue - 1; j >= 0; j--)
                            margs[j] = stack.Pop();
                        var (recvNode, mName) = pendingMethods.Pop();
                        var minlined = allowInline ? TryInlineMethodCall(instr, recvNode, mName, margs, currentChunk) : null;
                        stack.Push(minlined ?? CallMethodNode(recvNode, mName, margs));
                        break;
                    }
                    case JitOpKind.MergeObject:
                    {
                        var source = stack.Pop();
                        var target = stack.Pop();
                        stack.Push(MergeObjectNode(target, source));
                        break;
                    }
                    case JitOpKind.InitPropOverwrite:
                    {
                        var value = stack.Pop();
                        var obj = stack.Pop();
                        stack.Push(InitPropOverwriteNode(obj, value, instr.Name));
                        break;
                    }
                    case JitOpKind.AppendElem:
                    {
                        var value = stack.Pop();
                        var arr = stack.Pop();
                        stack.Push(AppendElemNode(arr, value));
                        break;
                    }
                    case JitOpKind.SetVar:
                        stack.Push(paramMap != null && paramMap.TryGetValue(instr.Name, out var svi)
                            ? SetParamNode(svi, stack.Pop()) : SetVarNode(instr.Name, stack.Pop(), strict));
                        break;
                    case JitOpKind.SetVarPop:
                        body.Add(Eff(paramMap != null && paramMap.TryGetValue(instr.Name, out var svpi)
                            ? SetParamNode(svpi, stack.Pop()) : SetVarNode(instr.Name, stack.Pop(), strict)));
                        break;
                    case JitOpKind.EnterBlock:    body.Add(EnterBlockStep); break;
                    case JitOpKind.LeaveBlock:    body.Add(LeaveBlockStep); break;
                    case JitOpKind.SetProp:
                    {
                        var value = stack.Pop();
                        var obj = stack.Pop();
                        stack.Push(SetPropNode(obj, instr.Name, value, strict));
                        break;
                    }
                    case JitOpKind.SetPropPop:
                    {
                        var value = stack.Pop();
                        var obj = stack.Pop();
                        body.Add(Eff(SetPropNode(obj, instr.Name, value, strict)));
                        break;
                    }
                    case JitOpKind.NewObject:     stack.Push(NewObjectNode()); break;
                    case JitOpKind.NewArray:      stack.Push(NewArrayNode()); break;
                    case JitOpKind.MakeClosure:   stack.Push(MakeClosureNode(instr.Closure)); break;
                    case JitOpKind.InitProp:
                    {
                        var value = stack.Pop();
                        var obj = stack.Pop();
                        stack.Push(InitPropNode(obj, value, instr.Name));
                        break;
                    }
                    case JitOpKind.InitElem:
                    {
                        var value = stack.Pop();
                        var arr = stack.Pop();
                        stack.Push(InitElemNode(arr, value, instr.IntValue));
                        break;
                    }
                    case JitOpKind.DeclareVar:    body.Add(Eff(DeclareNode(instr.Name, JitDeclareKind.Var))); break;
                    case JitOpKind.DeclareLocal:  body.Add(Eff(DeclareNode(instr.Name, JitDeclareKind.Local))); break;
                    case JitOpKind.DeclareConst:  body.Add(Eff(DeclareNode(instr.Name, JitDeclareKind.Const))); break;
                    case JitOpKind.Pop:     body.Add(Eff(stack.Pop())); break;
                }
            }

            // Fell off the end without a terminator: fall through to the next block.
            // (Well-formed chunks end in Return, so this only happens for a block that
            // was split purely because the following instruction is a jump target.)
            if (stack.Count != 0 || pendingMethods.Count != 0 || end >= instrs.Count) return null;
            return Block.Fall(body.ToArray(), idx2blk[end]);
        }

        // One step of a block body: a side-effecting expression node or a block-scope
        // env change (EnterBlock/LeaveBlock). The current environment is threaded by
        // ref so EnterBlock/LeaveBlock can rebind it for every following step and block.
        private delegate void BodyStep(VirtualMachine vm, ScriptVar[] args, ref Environment env);

        // Wrap a value/effect node (which reads env by value) as a body step.
        private static BodyStep Eff(JitDelegate node) =>
            (VirtualMachine vm, ScriptVar[] args, ref Environment env) => node(vm, args, env);

        // Block-scope steps: push a fresh child env / restore the parent.
        private static readonly BodyStep EnterBlockStep =
            (VirtualMachine vm, ScriptVar[] args, ref Environment env) => env = VirtualMachine.JitEnterBlock(env);

        private static readonly BodyStep LeaveBlockStep =
            (VirtualMachine vm, ScriptVar[] args, ref Environment env) => env = VirtualMachine.JitLeaveBlock(env);

        // Drive the compiled basic blocks from <paramref name="startBlock"/> until a
        // Return. Locals live in <c>env</c>, which is threaded across blocks so block
        // scopes entered in one block stay in effect for later ones; the per-block
        // operand stack is empty at entry, so a block's steps run, then its terminator
        // picks the successor.
        private static JitDelegate RunFromBlock(Block[] blocks, int startBlock) =>
            (vm, args, env) =>
            {
                var curEnv = env;
                var bb = startBlock;
                while (true)
                {
                    var b = blocks[bb];
                    var steps = b.Body;
                    for (var i = 0; i < steps.Length; i++)
                        steps[i](vm, args, ref curEnv);

                    switch (b.Kind)
                    {
                        case TermKind.Return:
                            return b.Value != null ? b.Value(vm, args, curEnv) : ScriptVar.CreateUndefined();
                        case TermKind.Jump:
                            bb = b.Target;
                            break;
                        case TermKind.BranchFalse:
                            bb = b.Value(vm, args, curEnv).Bool ? b.Next : b.Target;
                            break;
                        default: // BranchTrue
                            bb = b.Value(vm, args, curEnv).Bool ? b.Target : b.Next;
                            break;
                    }
                }
            };

        // Terminator kinds for a basic block.
        private enum TermKind { Jump, BranchFalse, BranchTrue, Return }

        // A compiled basic block: body steps to run in order, then a terminator.
        // <see cref="Value"/> is the branch condition (BranchFalse/True) or the
        // returned value (Return); null for an unconditional Jump.
        private sealed class Block
        {
            public BodyStep[] Body;
            public JitDelegate Value;
            public TermKind Kind;
            public int Target; // taken successor / unconditional target
            public int Next;   // not-taken successor (conditional branches only)

            public static Block Goto(BodyStep[] body, int target) =>
                new() { Body = body, Kind = TermKind.Jump, Target = target };

            public static Block Fall(BodyStep[] body, int target) =>
                new() { Body = body, Kind = TermKind.Jump, Target = target };

            public static Block Branch(BodyStep[] body, JitDelegate cond, TermKind kind, int target, int next) =>
                new() { Body = body, Value = cond, Kind = kind, Target = target, Next = next };

            public static Block Ret(BodyStep[] body, JitDelegate value) =>
                new() { Body = body, Value = value, Kind = TermKind.Return };
        }

        // Each factory captures only its own parameters, so closures built in a loop
        // never share mutable state.

        private static JitDelegate ConstNode(ConstantValue c) => (vm, args, env) => c.Materialize();

        private static JitDelegate IntLitNode(int v) => (vm, args, env) => ScriptVar.FromInt(v);

        private static JitDelegate VarNode(string name) => (vm, args, env) => VirtualMachine.JitGetVar(env, name);

        // Positional parameter access inside an inlined callee body: the call site binds
        // arguments into the JitDelegate `args` array, so a parameter read/write is an
        // index, not a name lookup. (Only used when compiling with a paramMap.)
        private static JitDelegate ParamNode(int index) => (vm, args, env) => args[index];

        // Positional local-slot access (Lever A): read/write the call frame's slot array.
        private static JitDelegate SlotGetNode(int slot) => (vm, args, env) => env.Slots[slot];

        private static JitDelegate SlotSetNode(int slot, JitDelegate value) =>
            (vm, args, env) =>
            {
                var v = value(vm, args, env);
                env.Slots[slot] = v;
                return v;
            };

        private static JitDelegate SetParamNode(int index, JitDelegate value) =>
            (vm, args, env) =>
            {
                var v = value(vm, args, env);
                args[index] = v;
                return v;
            };

        // Try to inline a monomorphic call to a small script function: bind its
        // parameters positionally and run its compiled body directly, skipping the
        // InvokeCallable frame setup (env + this + name-bound params + arg array). A
        // runtime identity guard falls back to a full call if the callee differs from
        // the one that was profiled. Returns null to decline (use the normal CallNode).
        private static JitDelegate TryInlineCall(JitInstruction instr, JitDelegate calleeNode,
                                                 JitDelegate[] argNodes, Chunk currentChunk)
        {
            var mono = instr.MonoCallee;
            if (mono == null || instr.MonoCallee1 != null) return null; // need a monomorphic site
            if (!mono.IsFunction || mono.IsNative || mono.IsProxy) return null;
            if (mono.GetData() is not VmFunction vmfn) return null;

            var body = vmfn.Body;
            if (body == currentChunk) return null;                       // no self-recursion
            if (body.IsGenerator || body.IsAsync) return null;
            if (body.RestParamIndex >= 0 || body.UsesArguments) return null;
            if (body.MakesClosure) return null;                          // closures need a real frame
            // A param-only-slotted callee inlines fine (slot access becomes a positional
            // arg read). One with *local* slots would need its own slot frame — decline.
            if (body.UsesSlots && body.SlotCount > body.Parameters.Count) return null;

            var calleeInstrs = JitDecoder.Decode(body);
            if (calleeInstrs == null) return null;

            // Block scopes could shadow a parameter name (the flat paramMap can't tell
            // them apart); this/super/new.target are frame-bound, not in the closure
            // env. Decline either. Track whether the body declares non-parameter locals.
            var hasDeclares = false;
            foreach (var ci in calleeInstrs)
            {
                if (ci.Kind is JitOpKind.EnterBlock or JitOpKind.LeaveBlock or JitOpKind.MakeClosure)
                    return null;
                if (ci.Kind is JitOpKind.PushVar or JitOpKind.SetVar or JitOpKind.SetVarPop
                    && ci.Name is "this" or "arguments" or "super" or "new.target")
                    return null;
                if (ci.Kind is JitOpKind.DeclareVar or JitOpKind.DeclareLocal or JitOpKind.DeclareConst)
                    hasDeclares = true;
            }

            var parameters = body.Parameters;
            var paramMap = new Dictionary<string, int>(parameters.Count);
            for (var i = 0; i < parameters.Count; i++) paramMap[parameters[i]] = i; // last duplicate wins (JS)

            var blocks = BuildBlocks(calleeInstrs, body.IsStrict, body, paramMap, allowInline: false, out _);
            if (blocks == null) return null;

            var calleeBody = RunFromBlock(blocks, 0);
            return InlineCallNode(calleeNode, argNodes, mono, calleeBody, parameters.Count,
                                  vmfn.Captured, hasDeclares);
        }

        private static JitDelegate InlineCallNode(JitDelegate calleeNode, JitDelegate[] argNodes, ScriptVar baked,
                                                  JitDelegate calleeBody, int nparams, Environment closureEnv,
                                                  bool hasDeclares) =>
            (vm, args, env) =>
            {
                var actual = calleeNode(vm, args, env);
                if (!ReferenceEquals(actual, baked))
                {
                    // Callee changed (reassigned / polymorphic): fall back to a full call.
                    var resolved = new ScriptVar[argNodes.Length];
                    for (var j = 0; j < argNodes.Length; j++) resolved[j] = argNodes[j](vm, args, env);
                    return vm.InvokeCallable(actual, null, resolved);
                }

                var callArgs = nparams == 0 ? System.Array.Empty<ScriptVar>() : new ScriptVar[nparams];
                for (var j = 0; j < argNodes.Length; j++)
                {
                    var v = argNodes[j](vm, args, env);   // evaluate in the caller's scope, in order
                    if (j < nparams) callArgs[j] = v;     // extra args still evaluated for side effects
                }
                for (var j = argNodes.Length; j < nparams; j++)
                    callArgs[j] = ScriptVar.CreateUndefined(); // missing args -> undefined

                // A body with no local declarations only reads params (args) and free
                // vars (closure env), so it needs no fresh frame; otherwise give its
                // locals a fresh child env per call.
                var bodyEnv = hasDeclares ? new Environment(ScriptVar.CreateObject(), closureEnv) : closureEnv;
                return calleeBody(vm, callArgs, bodyEnv);
            };

        // A method call obj.m(args): evaluate the receiver exactly once, resolve the
        // method through the per-site inline cache (getter/prototype-aware, Lever 2a),
        // then dispatch with this = receiver. Unlike the Reflection.Emit back-end this
        // does not inline the callee body (monomorphic body inlining stays RE-only — the
        // closure back-end favours portable, modest gains), but it compiles method calls
        // instead of declining them, giving both back-ends method-call parity.
        private static JitDelegate CallMethodNode(JitDelegate receiver, string name, JitDelegate[] argNodes)
        {
            var cell = new PropCacheCell();
            return (vm, args, env) =>
            {
                var recv = receiver(vm, args, env);
                var link = cell.Lookup(recv);
                var method = (link != null && link.Getter == null) ? link.Var
                                                                    : vm.JitGetPropCached(recv, name, cell);
                var argv = argNodes.Length == 0 ? System.Array.Empty<ScriptVar>()
                                                : new ScriptVar[argNodes.Length];
                for (var j = 0; j < argNodes.Length; j++) argv[j] = argNodes[j](vm, args, env);
                return vm.InvokeCallable(method, recv, argv);
            };
        }

        // Monomorphic method-body inlining (the method-call analogue of TryInlineCall).
        // At a monomorphic CallMethod site whose method is inline-eligible, splice the
        // callee body with `this` bound to the receiver — eliminating the InvokeCallable
        // frame and the boxed arg array, and (once subexpression unboxing lands) letting
        // values flow across the former call boundary. The receiver is passed as a
        // synthetic trailing argument slot (mapped from the name "this"); a runtime
        // method-identity mismatch falls back to a normal dispatch. Returns null to
        // decline (use plain dispatch via CallMethodNode).
        private static JitDelegate TryInlineMethodCall(JitInstruction instr, JitDelegate receiverNode,
                                                       string name, JitDelegate[] argNodes, Chunk currentChunk)
        {
            if (DisableMethodInlining) return null;
            var mono = instr.MonoCallee;
            if (mono == null || instr.MonoCallee1 != null) return null;   // need a monomorphic site
            if (!mono.IsFunction || mono.IsNative || mono.IsProxy) return null;
            if (mono.GetData() is not VmFunction vmfn) return null;

            var body = vmfn.Body;
            if (body == currentChunk) return null;                        // no self-recursion
            if (body.IsGenerator || body.IsAsync) return null;
            if (body.RestParamIndex >= 0 || body.UsesArguments) return null;
            if (body.MakesClosure) return null;
            if (body.UsesSlots && body.SlotCount > body.Parameters.Count) return null;

            var calleeInstrs = JitDecoder.Decode(body);
            if (calleeInstrs == null) return null;

            // `this` is bound to the receiver below; super/arguments/new.target are
            // frame-bound and not threaded here, so decline a body that reads them.
            var hasDeclares = false;
            foreach (var ci in calleeInstrs)
            {
                if (ci.Kind is JitOpKind.EnterBlock or JitOpKind.LeaveBlock or JitOpKind.MakeClosure)
                    return null;
                if (ci.Kind is JitOpKind.PushVar or JitOpKind.SetVar or JitOpKind.SetVarPop
                    && ci.Name is "arguments" or "super" or "new.target")
                    return null;
                if (ci.Kind is JitOpKind.DeclareVar or JitOpKind.DeclareLocal or JitOpKind.DeclareConst)
                    hasDeclares = true;
            }

            var parameters = body.Parameters;
            var paramMap = new Dictionary<string, int>(parameters.Count + 1);
            for (var i = 0; i < parameters.Count; i++) paramMap[parameters[i]] = i; // last duplicate wins (JS)
            paramMap["this"] = parameters.Count; // receiver rides the trailing arg slot

            var blocks = BuildBlocks(calleeInstrs, body.IsStrict, body, paramMap, allowInline: false, out _);
            if (blocks == null) return null;

            var calleeBody = RunFromBlock(blocks, 0);
            return InlineMethodCallNode(receiverNode, name, argNodes, mono, calleeBody,
                                        parameters.Count, vmfn.Captured, hasDeclares);
        }

        private static JitDelegate InlineMethodCallNode(JitDelegate receiverNode, string name,
                                                        JitDelegate[] argNodes, ScriptVar baked,
                                                        JitDelegate calleeBody, int nparams,
                                                        Environment closureEnv, bool hasDeclares)
        {
            var cell = new PropCacheCell();
            return (vm, args, env) =>
            {
                var recv = receiverNode(vm, args, env);            // evaluate the receiver exactly once
                var link = cell.Lookup(recv);
                var method = (link != null && link.Getter == null) ? link.Var
                                                                    : vm.JitGetPropCached(recv, name, cell);
                if (!ReferenceEquals(method, baked))
                {
                    // Method changed / polymorphic: fall back to a full dispatch.
                    var resolved = new ScriptVar[argNodes.Length];
                    for (var j = 0; j < argNodes.Length; j++) resolved[j] = argNodes[j](vm, args, env);
                    return vm.InvokeCallable(method, recv, resolved);
                }

                // callArgs = [param0 .. param{nparams-1}, this]; extra args are still
                // evaluated for their side effects, missing args default to undefined.
                var callArgs = new ScriptVar[nparams + 1];
                for (var j = 0; j < argNodes.Length; j++)
                {
                    var v = argNodes[j](vm, args, env);
                    if (j < nparams) callArgs[j] = v;
                }
                for (var j = argNodes.Length; j < nparams; j++)
                    callArgs[j] = ScriptVar.CreateUndefined();
                callArgs[nparams] = recv;                          // `this`

                var bodyEnv = hasDeclares ? new Environment(ScriptVar.CreateObject(), closureEnv) : closureEnv;
                return calleeBody(vm, callArgs, bodyEnv);
            };
        }

        private static JitDelegate GetPropNode(JitDelegate obj, string name)
        {
            var cell = new PropCacheCell(); // one inline-cache cell per site
            return (vm, args, env) =>
            {
                var o = obj(vm, args, env);
                // Inline cache fast path (Lever 2a): a cached data property (no getter)
                // is read directly, skipping the JitGetPropCached call frame. Misses,
                // getters and the full resolve fall through.
                var link = cell.Lookup(o);
                if (link != null && link.Getter == null) return link.Var;
                return vm.JitGetPropCached(o, name, cell);
            };
        }

        private static JitDelegate SetVarNode(string name, JitDelegate value, bool strict) =>
            (vm, args, env) =>
            {
                var v = value(vm, args, env);
                VirtualMachine.JitSetVar(env, name, v, strict);
                return v;
            };

        private static JitDelegate SetPropNode(JitDelegate obj, string name, JitDelegate value, bool strict)
        {
            var cell = new PropCacheCell(); // one inline-cache cell per write site
            return (vm, args, env) =>
            {
                var o = obj(vm, args, env);     // object evaluated before value (push order)
                var v = value(vm, args, env);
                // Inline cache fast path (Lever 2b): overwrite a cached own writable data
                // property in place, skipping the JitSetProp/SetMember call frame. A
                // SetProp cell only caches own data properties (see JitSetPropCached).
                var link = cell.Lookup(o);
                if (link != null && link.Writable) { link.ReplaceWith(v); return v; }
                vm.JitSetPropCached(o, name, v, cell, strict);
                return v;
            };
        }

        private static JitDelegate DeclareNode(string name, JitDeclareKind kind) =>
            (vm, args, env) =>
            {
                switch (kind)
                {
                    case JitDeclareKind.Var:   VirtualMachine.JitDeclareVar(env, name); break;
                    case JitDeclareKind.Local: VirtualMachine.JitDeclareLocal(env, name); break;
                    default:                   VirtualMachine.JitDeclareConst(env, name); break;
                }
                return ScriptVar.CreateUndefined(); // discarded by the effects runner
            };

        // Object/array literals. NewObject/NewArray create a fresh instance; each
        // Init* node evaluates the construction-so-far (a single instance, since the
        // chain is linear — each consumes its predecessor), mutates it, and returns it.
        private static JitDelegate NewObjectNode() => (vm, args, env) => ScriptVar.CreateObject();

        private static JitDelegate NewArrayNode() => (vm, args, env) => ScriptVar.CreateArray();

        private static JitDelegate InitPropNode(JitDelegate objNode, JitDelegate valueNode, string name) =>
            (vm, args, env) =>
            {
                var o = objNode(vm, args, env);
                o.AddChild(name, valueNode(vm, args, env));
                return o;
            };

        private static JitDelegate InitElemNode(JitDelegate arrNode, JitDelegate valueNode, int index) =>
            (vm, args, env) =>
            {
                var a = arrNode(vm, args, env);
                a.SetArrayIndex(index, valueNode(vm, args, env));
                return a;
            };

        private static JitDelegate MakeClosureNode(Chunk fnChunk) =>
            (vm, args, env) => VirtualMachine.JitMakeClosure(env, fnChunk);

        private static JitDelegate NullNode() => (vm, args, env) => ScriptVar.CreateNull();

        private static JitDelegate UndefinedNode() => (vm, args, env) => ScriptVar.CreateUndefined();

        private static JitDelegate NotNode(JitDelegate operand) =>
            (vm, args, env) => ScriptVar.FromInt(operand(vm, args, env).Bool ? 0 : 1);

        private static JitDelegate UnaryNode(JitDelegate operand, System.Func<ScriptVar, ScriptVar> op) =>
            (vm, args, env) => op(operand(vm, args, env));

        private static JitDelegate GetIndexNode(JitDelegate obj, JitDelegate key) =>
            (vm, args, env) => vm.JitGetIndex(obj(vm, args, env), key(vm, args, env));

        private static JitDelegate SetIndexNode(JitDelegate obj, JitDelegate key, JitDelegate value, bool strict) =>
            (vm, args, env) =>
            {
                var o = obj(vm, args, env);
                var k = key(vm, args, env);
                var v = value(vm, args, env);
                vm.JitSetIndex(o, k, v, strict);
                return v; // SetIndex is an expression
            };

        private static JitDelegate ShiftNode(JitDelegate left, JitDelegate right, ScriptLex.LexTypes op) =>
            (vm, args, env) => VirtualMachine.JitShift(left(vm, args, env), right(vm, args, env), op);

        private static JitDelegate BinaryNode(JitDelegate left, JitDelegate right, ScriptLex.LexTypes op) =>
            (vm, args, env) => VirtualMachine.JitBinary(left(vm, args, env), right(vm, args, env), op);

        private static JitDelegate CallNode(JitDelegate callee, JitDelegate[] argNodes) =>
            (vm, args, env) =>
            {
                var c = callee(vm, args, env);
                var resolved = new ScriptVar[argNodes.Length];
                for (var j = 0; j < argNodes.Length; j++)
                    resolved[j] = argNodes[j](vm, args, env);
                return vm.InvokeCallable(c, null, resolved);
            };

        private static JitDelegate NewNode(JitDelegate ctor, JitDelegate[] argNodes) =>
            (vm, args, env) =>
            {
                var c = ctor(vm, args, env);
                var resolved = new ScriptVar[argNodes.Length];
                for (var j = 0; j < argNodes.Length; j++)
                    resolved[j] = argNodes[j](vm, args, env);
                return vm.JitNew(c, resolved);
            };

        private static JitDelegate MergeObjectNode(JitDelegate target, JitDelegate source) =>
            (vm, args, env) => vm.JitMergeObject(target(vm, args, env), source(vm, args, env));

        private static JitDelegate InitPropOverwriteNode(JitDelegate objNode, JitDelegate valueNode, string name) =>
            (vm, args, env) =>
            {
                var o = objNode(vm, args, env);
                o.AddChildNoDup(name, valueNode(vm, args, env));
                return o;
            };

        private static JitDelegate AppendElemNode(JitDelegate arrNode, JitDelegate valueNode) =>
            (vm, args, env) =>
            {
                var a = arrNode(vm, args, env);
                a.AppendArrayElement(valueNode(vm, args, env));
                return a;
            };
    }
}
