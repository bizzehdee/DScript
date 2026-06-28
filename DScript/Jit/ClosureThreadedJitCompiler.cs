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
        public JitDelegate Compile(Chunk chunk)
        {
            var instrs = JitDecoder.Decode(chunk);
            if (instrs == null)
                return null; // declined by the shared front-end

            var blocks = BuildBlocks(instrs, chunk.IsStrict, chunk, null, allowInline: true, out _);
            if (blocks == null)
                return null; // unsupported control flow / op — VM keeps interpreting

            return RunFromBlock(blocks, 0);
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

            return RunFromBlock(blocks, startBlock);
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
            // boundary; method calls need receiver threading the block model lacks.
            // (Block scopes — EnterBlock/LeaveBlock — are supported via the driver's
            // threaded current environment.)
            foreach (var instr in instrs)
                if (instr.Kind is JitOpKind.JumpIfFalseOrPop or JitOpKind.JumpIfTrueOrPop
                    or JitOpKind.JumpIfNullOrUndefined or JitOpKind.JumpIfDefined
                    or JitOpKind.GetPropMethod or JitOpKind.GetPropCall0 or JitOpKind.CallMethod)
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

            for (var i = start; i < end; i++)
            {
                var instr = instrs[i];

                // Terminators end the block.
                if (instr.Kind == JitOpKind.Jump)
                {
                    if (stack.Count != 0) return null;
                    return Block.Goto(body.ToArray(), idx2blk[instr.IntValue]);
                }
                if (instr.Kind == JitOpKind.JumpIfFalse)
                {
                    if (stack.Count != 1) return null;
                    return Block.Branch(body.ToArray(), stack.Pop(), TermKind.BranchFalse,
                                        idx2blk[instr.IntValue], idx2blk[i + 1]);
                }
                if (instr.Kind == JitOpKind.JumpIfTrue)
                {
                    if (stack.Count != 1) return null;
                    return Block.Branch(body.ToArray(), stack.Pop(), TermKind.BranchTrue,
                                        idx2blk[instr.IntValue], idx2blk[i + 1]);
                }
                if (instr.Kind == JitOpKind.Return)
                {
                    if (stack.Count != 1) return null;
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
            if (stack.Count != 0 || end >= instrs.Count) return null;
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
