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

using System;
using System.Collections.Generic;
using System.Globalization;
using DScript.Debugger;
using DScript.Profiler;

namespace DScript.Vm
{
    /// <summary>
    /// Stack-based virtual machine that executes compiled <see cref="Chunk"/>
    /// bytecode. Phase 1 implements the expression/stack/control-flow core;
    /// variable, property, function, and exception opcodes are filled in by
    /// later phases.
    /// </summary>
    public sealed partial class VirtualMachine
    {
        private readonly ScriptEngine engine;

        // Operand stack backed by a plain array with an explicit pointer. This is
        // markedly cheaper than List<ScriptVar>: Push/Pop/Peek become bare array
        // indexing with no per-call method dispatch, bounds-clearing, or shifting.
        private Value[] stack = new Value[64];
        private int sp;

        // Nested script-call depth. Bounded (see MaxCallStackDepth) so runaway or
        // infinite recursion throws a catchable error rather than overflowing the
        // native stack and crashing the process.
        private int _callDepth;
        private const int DefaultMaxCallDepth = 10000;
        // Cached at construction (the VM is created fresh per top-level Run) so the
        // per-call guard reads a plain field rather than walking to the engine.
        private readonly int _maxCallDepth;

        // Shared read-only operand for unary 0-based ops. MathsOp only reads its
        // operands (results are always freshly allocated), so sharing is safe and
        // avoids allocating a throwaway zero on every Negate/Not.
        private static readonly ScriptVar Zero = ScriptVar.FromInt(0);

        // Shared immutable singletons for the four primitive constants that are
        // produced on virtually every bytecode tick (comparisons, undefined reads,
        // null literals).  Made non-extensible so that property-assignment on a
        // primitive (JS silently ignores it) cannot corrupt the shared instance.
        private static readonly ScriptVar SharedTrue;
        private static readonly ScriptVar SharedFalse;
        private static readonly ScriptVar SharedUndefined;
        private static readonly ScriptVar SharedNull;

        static VirtualMachine()
        {
            SharedTrue      = ScriptVar.FromInt(1);      SharedTrue.PreventExtensionsSelf();
            SharedFalse     = ScriptVar.FromInt(0);      SharedFalse.PreventExtensionsSelf();
            SharedUndefined = ScriptVar.CreateUndefined(); SharedUndefined.PreventExtensionsSelf();
            SharedNull      = ScriptVar.CreateNull();      SharedNull.PreventExtensionsSelf();
        }

        // --- structured exception handling ----------------------------------

        private struct TryFrame
        {
            public int CatchPC;      // -1 if no catch clause (or catch already entered)
            public int FinallyPC;    // -1 if no finally clause
            public int CatchVarIdx;  // Names index for the catch binding; -1 if absent
            public int StackDepth;   // value-stack depth at EnterTry (restored on exception)
        }

        private readonly Stack<TryFrame> tryStack = new();

        // Set by exception dispatch when no catch is present but a finally must run.
        // LeaveFinally re-throws this after the finally body completes.
        private JITException pendingException;

        // Set by SaveReturn when `return` exits a try-with-finally mid-body.
        // LeaveFinally performs the actual return after all finally blocks have run.
        private bool hasPendingReturn;
        private ScriptVar pendingReturnValue;

        // Trampoline state for tail-call elimination.  TailCall / TailCallMethod
        // handlers set these three fields and return null from Execute; the
        // InvokeVmFunctionFromStack trampoline loop re-executes the callee without
        // growing the C# call stack, enabling unbounded tail recursion.
        private VmFunction _pendingTailCallFn;
        private ScriptVar[] _pendingTailCallArgs;
        private ScriptVar _pendingTailCallThis;

        // Pool of call-frame binding containers. A function whose frame cannot
        // escape (no closure captures it — see Chunk.RecyclableFrame) returns its
        // bindings var here on exit and the next such call reuses it, avoiding a
        // ScriptVar allocation per call. Reuse detaches (does not dispose) the old
        // bindings, preserving the lifetime of any value that escaped the frame.
        // Bounded to prevent unbounded memory growth in recursive workloads.
        private const int FramePoolMaxSize = 64;
        private readonly Stack<ScriptVar> frameVarsPool = new();

        // Pool of native-call scope containers. Every native function invocation
        // creates a temporary Function ScriptVar for its parameter bindings; all such
        // scopes are safe to recycle because native scopes never escape into closures.
        // Reuse detaches (does not dispose) old bindings, matching GC lifetime semantics.
        // Array-based to keep rent/return to a compare + array read/write per call.
        private const int NativeScopePoolMaxSize = 32;
        private int _nativeScopeCount;
        private readonly ScriptVar[] _nativeScopeArr = new ScriptVar[NativeScopePoolMaxSize];

        private ScriptVar BorrowFrameVars()
        {
            return frameVarsPool.Count > 0 ? frameVarsPool.Pop() : ScriptVar.CreateObject();
        }

        private void ReturnFrameVars(ScriptVar vars)
        {
            if (frameVarsPool.Count >= FramePoolMaxSize) return; // drop to GC instead of growing pool
            vars.ResetForReuse();
            frameVarsPool.Push(vars);
        }

        private ScriptVar BorrowNativeScope()
        {
            if (_nativeScopeCount > 0)
                return _nativeScopeArr[--_nativeScopeCount];
            return ScriptVar.CreateFunction();
        }

        private void ReturnNativeScope(ScriptVar scope)
        {
            if (_nativeScopeCount >= NativeScopePoolMaxSize) return;
            scope.ResetForReuse();
            _nativeScopeArr[_nativeScopeCount++] = scope;
        }


        // Per-VM inline property cache: a direct-mapped array of 256 slots, indexed
        // by Fibonacci hash ((uint)nameIndex * 2654435761u >> 24). Each slot stores the last object seen at that site,
        // its shape version at cache-fill time, the interned property name, and the
        // cached link. A hit requires both reference equality of the object and a
        // matching shape version (ensuring no structural changes since fill time).
        // The link is stored rather than the value so that subsequent mutations via
        // ReplaceWith (e.g. assignment to an existing property) are always visible
        // through ce.Link.Var without requiring a cache invalidation.
        private struct PropCacheEntry
        {
            public string Name;              // interned string — ReferenceEquals is valid (both paths)
            // Shape-keyed path (ShapeId > 0): keyed on hidden-class identity, works
            // across all instances that share the same shape — no object identity check.
            public int ShapeId;             // >0 = shape path; 0 = identity path / empty
            public int SlotIndex;           // walk count from obj._shapeRoot (0 = _shapeRoot itself)
            // Identity-keyed path (ShapeId == 0): keyed on object identity + shapeVersion.
            public ScriptVar Object;        // reference equality
            public int ShapeVersion;
            public ScriptVarLink Link;      // points into the object's child list
        }
        private readonly PropCacheEntry[] _propCache = new PropCacheEntry[256];

        // Negative setter cache: when we look up a property name on a prototype chain
        // and find no accessor (setter/non-writable), record (proto, name) so the next
        // instance of the same class can skip the FindInParentClasses walk entirely.
        // Keyed by prototype identity + name; valid as long as no accessor is defined
        // on the prototype after the fact (an uncommon mutation pattern).
        private System.Collections.Generic.Dictionary<(ScriptVar, string), bool> _noSetterCache;

        // --- resource limits ------------------------------------------------
        // Snapshotted from the engine at the start of each Run() call so the
        // hot Execute() loop reads a plain field, not a virtual property chain.
        private long _instrCount;
        private long _instrHardLimit = long.MaxValue; // MaxValue = no limit
        private long _deadlineTicks = long.MinValue;  // MinValue = no timeout

        // --- step debugger --------------------------------------------------

        // Lightweight mutable record for one active call frame.
        private sealed class CallFrame
        {
            public readonly Chunk Chunk;
            public readonly Environment Env;
            public int Ip; // updated before each instruction when a debugger is attached

            public CallFrame(Chunk chunk, Environment env) { Chunk = chunk; Env = env; }
        }

        private IDebugger _debugger;
        private readonly HashSet<(string Source, int Line)> _breakpoints = [];
        private readonly List<CallFrame> _callFrames = [];

        // Step state — shared across all recursive Execute() calls on this VM.
        private DebugAction _stepAction = DebugAction.Continue;
        private int _stepTargetDepth;   // depth at which the last step action was issued
        private Chunk _lastPausedChunk;
        private int _lastPausedLine;

        internal void AttachDebugger(IDebugger debugger, DebugAction initialAction = DebugAction.Continue)
        {
            _debugger = debugger;
            _stepAction = initialAction;
        }

        internal void DetachDebugger() => _debugger = null;

        private Profiler.IProfiler _profiler;

        internal void AttachProfiler(Profiler.IProfiler profiler) => _profiler = profiler;
        internal void DetachProfiler() => _profiler = null;

        internal void AddBreakpoint(string source, int line) => _breakpoints.Add((source, line));
        internal void RemoveBreakpoint(string source, int line) => _breakpoints.Remove((source, line));

        private bool ShouldPauseAtLine(Chunk chunk, int line)
        {
            var sameLocation = chunk == _lastPausedChunk && line == _lastPausedLine;
            var depth = _callFrames.Count;
            return _stepAction switch
            {
                DebugAction.StepIn   => !sameLocation,
                DebugAction.StepOver => depth <= _stepTargetDepth && !sameLocation,
                DebugAction.StepOut  => depth < _stepTargetDepth,
                // Breakpoints fire at most once per line visit, same as steps.
                DebugAction.Continue => !sameLocation && _breakpoints.Contains((chunk.Name, line)),
                _                    => false,
            };
        }

        private void FireDebugPause(Chunk chunk, int ip, int line, Environment env)
        {
            _lastPausedChunk = chunk;
            _lastPausedLine = line;
            _stepTargetDepth = _callFrames.Count;

            var location = new DebugLocation(chunk.Name, line, ip);
            var frames = BuildCallStack(chunk, ip, line, env);
            _stepAction = _debugger.OnPause(new DebugEvent(location, frames));
        }

        private List<DebugFrame> BuildCallStack(Chunk topChunk, int topIp, int topLine, Environment topEnv)
        {
            var frames = new List<DebugFrame>(_callFrames.Count + 1);
            frames.Add(MakeFrame(topChunk, topIp, topLine, topEnv));
            for (var i = _callFrames.Count - 1; i >= 0; i--)
            {
                var f = _callFrames[i];
                frames.Add(MakeFrame(f.Chunk, f.Ip, f.Chunk.GetLineForOffset(f.Ip), f.Env));
            }
            return frames;
        }

        private static DebugFrame MakeFrame(Chunk chunk, int ip, int line, Environment env)
        {
            var loc = new DebugLocation(chunk.Name, line, ip);
            var locals = new List<(string, ScriptVar)>();
            var link = env.Vars.FirstChild;
            while (link != null) { locals.Add((link.Name, link.Var)); link = link.Next; }
            return new DebugFrame(chunk.Name, loc, locals);
        }

        public VirtualMachine()
        {
            _maxCallDepth = DefaultMaxCallDepth;
        }

        public VirtualMachine(ScriptEngine engine)
        {
            this.engine = engine;
            _maxCallDepth = engine?.MaxCallStackDepth ?? DefaultMaxCallDepth;
        }

        /// <summary>
        /// Drain all pending micro-tasks. Call after running async code.
        /// </summary>
        public static void DrainMicroTasks() => MicroTaskQueue.DrainAll();

        // Phase 1a (Lever 1): the operand stack is a Value[] rather than ScriptVar[].
        // For now every value is carried as a boxed reference (Value.Ref), so Push/Pop
        // are a pure pass-through with identical behaviour — this is the structural
        // checkpoint. Later phases push raw int/double Values for unboxed arithmetic
        // (PushValue/PopValue) and box only at object-world boundaries.
        private void Push(ScriptVar value)
        {
            if (sp == stack.Length)
            {
                System.Array.Resize(ref stack, stack.Length * 2);
            }
            stack[sp++] = Value.Ref(value);
        }

        private void PushValue(Value value)
        {
            if (sp == stack.Length)
            {
                System.Array.Resize(ref stack, stack.Length * 2);
            }
            stack[sp++] = value;
        }

        private ScriptVar Pop() => stack[--sp].ToScriptVar();

        private Value PopValue() => stack[--sp];

        private ScriptVar Peek() => stack[sp - 1].ToScriptVar();

        /// <summary>
        /// Execute a top-level chunk and return the produced value (the operand
        /// of a <see cref="OpCode.Return"/>, or undefined when the chunk halts).
        /// </summary>
        public ScriptVar Run(Chunk chunk)
        {
            return Run(chunk, new Environment(ScriptVar.CreateObject(), null));
        }

        // Size of the worker-thread stack used for top-level execution. The
        // interpreter's Execute frame is large, so the default ~1 MB thread stack only
        // holds a few hundred recursion levels; a dedicated large stack lets scripts
        // recurse to a useful depth, with MaxCallStackDepth (default 10000) bounding
        // it well short of overflow — leaving room for the error to unwind from that
        // depth — so runaway recursion throws a catchable error instead of crashing.
        private const int ExecutionStackBytes = 256 * 1024 * 1024;

        // True while running on a spawned execution thread, so nested/reentrant Run
        // calls (e.g. a host callback that re-enters the engine) execute inline rather
        // than spawning another thread.
        [ThreadStatic] private static bool _onExecutionThread;

        public ScriptVar Run(Chunk chunk, Environment env)
        {
            if (_onExecutionThread)
                return RunCore(chunk, env);

            ScriptVar result = null;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo captured = null;
            var worker = new System.Threading.Thread(() =>
            {
                _onExecutionThread = true;
                try { result = RunCore(chunk, env); }
                catch (Exception ex) { captured = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex); }
            }, ExecutionStackBytes);
            worker.IsBackground = true;
            worker.Start();
            worker.Join();
            captured?.Throw();
            return result;
        }

        private ScriptVar RunCore(Chunk chunk, Environment env)
        {
            _instrCount = 0;
            _instrHardLimit = (engine != null && engine.InstructionLimit > 0) ? engine.InstructionLimit : long.MaxValue;
            _deadlineTicks = (engine != null && engine.ExecutionTimeout > TimeSpan.Zero)
                ? DateTime.UtcNow.Ticks + engine.ExecutionTimeout.Ticks
                : long.MinValue;

            var startDepth = sp;

            ScriptVar result;
            if (chunk.IsAsync)
            {
                // Top-level await: run the chunk as an async body in the provided env.
                // Variables declared with `var` land in env.Vars (the engine root),
                // not in a nested function scope.
                var vmfn = new VmFunction(chunk, env);
                result = CreateAsyncPromise(vmfn, env);
            }
            else
            {
                result = Execute(chunk, env);
            }

            // discard anything the chunk left behind to keep the stack balanced
            sp = startDepth;

            return result ?? ScriptVar.CreateUndefined();
        }

        private ScriptVar Execute(Chunk chunk, Environment env, GeneratorObject genObj = null, GeneratorState gsState = null, bool bypassJit = false)
        {
            // Count every entry into this chunk (includes generator resumes, which
            // are legitimate re-entries but are not fresh invocations).
            chunk.InvocationCount++;

            // JIT tier-up. Only plain invocations are eligible: generator and
            // async resumes (genObj/gsState != null) keep using the interpreter so
            // their suspended-state machinery is preserved. When no compiler is
            // registered this whole block is skipped and behaviour is unchanged.
            // bypassJit is set by Deoptimize so a bailed speculative run re-executes
            // on the interpreter without re-entering the compiled code.
            if (!bypassJit && genObj == null && gsState == null)
            {
                if (chunk.CompiledDelegate != null)
                {
                    try { return chunk.CompiledDelegate(this, System.Array.Empty<ScriptVar>(), env); }
                    catch (OverflowException) { return DeoptimizeOverflow(chunk, env, genObj, gsState); }
                }

                if (chunk.IsHot() && JitRegistry.Current != null)
                {
                    chunk.JitState = Chunk.JitStatus.Compiling;
                    if (JitRegistry.BackgroundCompilation)
                    {
                        // Hand off to the worker; keep interpreting until it publishes
                        // the delegate (picked up by the CompiledDelegate check above
                        // on a later invocation).
                        JitRegistry.EnqueueForCompilation(chunk);
                    }
                    else
                    {
                        try
                        {
                            var compiled = JitRegistry.Current.Compile(chunk);
                            if (compiled != null)
                            {
                                chunk.CompiledDelegate = compiled;
                                chunk.JitState = Chunk.JitStatus.Compiled;
                                return compiled(this, System.Array.Empty<ScriptVar>(), env);
                            }
                            // Compiler declined this chunk: don't retry it.
                            chunk.JitState = Chunk.JitStatus.Failed;
                        }
                        catch (OverflowException)
                        {
                            // A speculative unboxed-int path overflowed 32 bits on its
                            // first run: deopt and prefer the (overflow-safe) tiers.
                            return DeoptimizeOverflow(chunk, env, genObj, gsState);
                        }
                        catch
                        {
                            // Compilation blew up: fall back to the interpreter for good.
                            chunk.JitState = Chunk.JitStatus.Failed;
                        }
                    }
                }
            }

            var code = chunk.CodeBytes;
            // Hoist the inline-cache array once so each GetVar/SetVar avoids the
            // property's lazy null-check on every resolution.
            var cache = chunk.InlineCache;
            // Hoist the call-site and binary-op profile arrays for the same reason.
            var callProf  = chunk.CallProfiles;
            var binProf   = chunk.BinaryOpProfiles;
            var ip = gsState != null && gsState.Started ? gsState.SavedIp : 0;

            // Stackless generator resume: push the input value passed to .next(v).
            if (gsState != null && gsState.Started && gsState.ResumeValue != null)
            {
                Push(gsState.ResumeValue);
                gsState.ResumeValue = null;
            }

            // Block-scope env stack: EnterBlock pushes the outer env here and
            // switches to a new child env; LeaveBlock restores the saved env.
            Stack<Environment> blockEnvStack = null;

            // Save handler-stack depth so any unclosed frames from this invocation
            // (e.g. `return` mid-try without finally) are cleaned up on all exit paths.
            var savedTryDepth = tryStack.Count;

            // Track this frame for the debugger's call-stack view.
            CallFrame callFrame = null;
            if (_debugger != null)
            {
                callFrame = new CallFrame(chunk, env);
                _callFrames.Add(callFrame);
            }

            var generatorYielded = false; // set when stackless Yield breaks the loop

            try
            {

            while (ip < code.Length && !generatorYielded)
            {
                var instrIp = ip; // capture before advancing — used for line lookup on error
                var op = (OpCode)code[ip];
                ip++;

                // Resource limits — checked outside the inner try/catch so that
                // ScriptTimeoutException bypasses script-side try/catch blocks.
                if (++_instrCount >= _instrHardLimit)
                    throw new ScriptTimeoutException("Script instruction limit exceeded");
                if ((_instrCount & 0x3FF) == 0 && _deadlineTicks != long.MinValue && DateTime.UtcNow.Ticks >= _deadlineTicks)
                    throw new ScriptTimeoutException("Script execution timed out");

                // Step debugger hook — fired at every new source line.
                if (_debugger != null)
                {
                    callFrame.Ip = instrIp;
                    var line = chunk.GetLineForOffset(instrIp);
                    if (line > 0 && ShouldPauseAtLine(chunk, line))
                        FireDebugPause(chunk, instrIp, line, env);
                }

                try
                {
                switch (op)
                {
                    case OpCode.Constant:
                        PushValue(ToValue(chunk.Constants[ReadOperand(code, ref ip)].Materialize()));
                        break;
                    case OpCode.PushUndefined:
                        Push(SharedUndefined);
                        break;
                    case OpCode.PushNull:
                        Push(SharedNull);
                        break;
                    case OpCode.PushTrue:
                        Push(SharedTrue);
                        break;
                    case OpCode.PushFalse:
                        Push(SharedFalse);
                        break;

                    case OpCode.Pop:
                        Pop();
                        break;
                    case OpCode.Dup:
                        PushValue(stack[sp - 1]);
                        break;
                    case OpCode.Dup2:
                    {
                        var b = stack[sp - 1];
                        var a = stack[sp - 2];
                        PushValue(a);
                        PushValue(b);
                        break;
                    }
                    case OpCode.EnumKeys:
                    {
                        var obj = Pop();
                        // Proxy [[OwnKeys]] trap
                        if (obj.IsProxy)
                        {
                            var handler = obj.ProxyHandler;
                            var ownKeysTrap = handler?.FindChild("ownKeys")?.Var;
                            if (ownKeysTrap != null && ownKeysTrap.IsFunction)
                            {
                                Push(InvokeCallable(ownKeysTrap, handler, new[] { obj.ProxyTarget }));
                                break;
                            }
                            obj = obj.ProxyTarget ?? obj;
                        }
                        var keys = ScriptVar.CreateArray();
                        var index = 0;
                        var member = obj.FirstChild;
                        while (member != null)
                        {
                            if (member.Name != ScriptVar.PrototypeClassName && member.Enumerable)
                            {
                                keys.SetArrayIndex(index++, ScriptVar.FromString(member.Name));
                            }
                            member = member.Next;
                        }
                        Push(keys);
                        break;
                    }

                    case OpCode.GetVar:
                    {
                        var site = ip;
                        var nameIdx = ReadOperand(code, ref ip);
                        var link = ResolveCached(cache, chunk, site, env, nameIdx);
                        if (link != null) { PushValue(ToValue(link.Var)); break; }
                        // globalThis is a virtual built-in — not a real scope binding.
                        // Checked here (cache-miss path) so the hot path pays no cost.
                        if (chunk.Names[nameIdx] == "globalThis") { Push(env.Global().Vars); break; }
                        Push(SharedUndefined);
                        break;
                    }
                    case OpCode.SetVar:
                    {
                        var site = ip;
                        var nameIdx = ReadOperand(code, ref ip);
                        var value = Pop();
                        var link = ResolveCached(cache, chunk, site, env, nameIdx);
                        if (link != null)
                        {
                            link.ReplaceWith(value);
                        }
                        else
                        {
                            if (chunk.IsStrict)
                                throw new ScriptException($"ReferenceError: '{chunk.Names[nameIdx]}' is not defined");
                            // New global binding: bump the global scope's version so
                            // any cached resolutions of this name re-validate.
                            var global = env.Global();
                            global.Vars.AddChildNoDup(chunk.Names[nameIdx], value);
                            global.Version++;
                        }
                        Push(value); // assignment is an expression
                        break;
                    }
                    case OpCode.GetLocal:
                    {
                        PushValue(ToValue(env.Slots[ReadOperand(code, ref ip)]));
                        break;
                    }
                    case OpCode.SetLocal:
                    {
                        var slot = ReadOperand(code, ref ip);
                        var value = Pop();
                        env.Slots[slot] = value;
                        Push(value); // assignment is an expression
                        break;
                    }
                    case OpCode.DeclareVar:
                    {
                        var name = chunk.Names[ReadOperand(code, ref ip)];
                        // `var` is function-scoped: skip any block scopes pushed by
                        // EnterBlock and declare in the nearest non-block environment.
                        var declEnv = env;
                        while (declEnv.IsBlockScope && declEnv.Parent != null)
                            declEnv = declEnv.Parent;
                        declEnv.Vars.FindChildOrCreate(name);
                        declEnv.Version++; // a new binding may shadow a cached outer resolution
                        break;
                    }
                    case OpCode.DeclareConst:
                    {
                        var name = chunk.Names[ReadOperand(code, ref ip)];
                        env.Vars.FindChildOrCreate(name, ScriptVar.Flags.Undefined, readOnly: true);
                        env.Version++; // a new binding may shadow a cached outer resolution
                        break;
                    }
                    case OpCode.DeclareLocal:
                    {
                        // `let`: declare in the innermost scope (no hoisting past blocks).
                        var name = chunk.Names[ReadOperand(code, ref ip)];
                        env.Vars.FindChildOrCreate(name);
                        env.Version++; // a new binding may shadow a cached outer resolution
                        break;
                    }

                    case OpCode.GetProp:
                    {
                        var nameIdx = ReadOperand(code, ref ip);
                        var name = chunk.Names[nameIdx];
                        var obj = Pop();

                        // Inline property cache: 256-slot direct-mapped.
                        // Two paths: shape-keyed (ShapeId>0, shared across all objects
                        // with the same hidden class) and identity-keyed (ShapeId==0,
                        // existing behaviour for non-shaped objects).
                        var cacheSlot = (int)((uint)nameIdx * 2654435761u >> 24);
                        ref var ce = ref _propCache[cacheSlot];
                        if (ReferenceEquals(ce.Name, name))
                        {
                            if (ce.ShapeId > 0)
                            {
                                var shapedObj = obj as ShapedScriptVar;
                                var shp = shapedObj?._shape;
                                if (shp != null && ce.ShapeId == shp.Id)
                                {
                                    var sl = shapedObj._shapeRoot;
                                    for (int _i = 0; _i < ce.SlotIndex; _i++) sl = sl?.Next;
                                    if (sl != null)
                                    {
                                        Push(sl.Getter != null
                                            ? InvokeCallable(sl.Getter, obj, System.Array.Empty<ScriptVar>())
                                            : sl.Var);
                                        break;
                                    }
                                }
                            }
                            else if (ReferenceEquals(ce.Object, obj) &&
                                     ce.ShapeVersion == obj.ShapeVersion &&
                                     ce.Link != null)
                            {
                                Push(ce.Link.Getter != null
                                    ? InvokeCallable(ce.Link.Getter, obj, System.Array.Empty<ScriptVar>())
                                    : ce.Link.Var);
                                break;
                            }
                        }

                        if (obj.IsProxy) { Push(GetMember(obj, name)); break; }

                        // Shape miss: populate via slot if available.
                        {
                            var shapedObj = obj as ShapedScriptVar;
                            var shp = shapedObj?._shape;
                            if (shp != null && shp.Slots.TryGetValue(name, out var slotIdx))
                            {
                                var sl = shapedObj._shapeRoot;
                                for (int _i = 0; _i < slotIdx; _i++) sl = sl?.Next;
                                if (sl != null)
                                {
                                    var propResult = sl.Getter != null
                                        ? InvokeCallable(sl.Getter, obj, System.Array.Empty<ScriptVar>())
                                        : sl.Var;
                                    ce.Name = name; ce.ShapeId = shp.Id; ce.SlotIndex = slotIdx;
                                    ce.Object = null; ce.Link = null;
                                    Push(propResult);
                                    break;
                                }
                            }
                        }

                        // Full lookup (no shape or property not in shape).
                        var link = obj.FindChild(name);
                        if (link == null && engine != null)
                            link = engine.FindInParentClasses(obj, name);

                        ScriptVar propResult2;
                        if (link != null)
                        {
                            if (link.Getter != null)
                                propResult2 = InvokeCallable(link.Getter, obj, System.Array.Empty<ScriptVar>());
                            else
                                propResult2 = link.Var;
                            if (obj.IsObject || obj.IsArray)
                            {
                                ce.Name = name; ce.ShapeId = 0;
                                ce.Object = obj; ce.ShapeVersion = obj.ShapeVersion; ce.Link = link;
                            }
                        }
                        else
                        {
                            if (obj.IsArray && name == "length")
                                propResult2 = ScriptVar.FromInt(obj.GetArrayLength());
                            else if (obj.IsString && name == "length")
                                propResult2 = ScriptVar.FromInt(obj.String.Length);
                            else if (name == "size" && obj.GetData() is INativeContainer container)
                                propResult2 = ScriptVar.FromInt(container.GetSize());
                            else
                                propResult2 = ScriptVar.CreateUndefined();
                        }

                        Push(propResult2);
                        break;
                    }
                    case OpCode.SetProp:
                    {
                        var name = chunk.Names[ReadOperand(code, ref ip)];
                        var value = Pop();
                        var obj = Pop();
                        SetMember(obj, name, value, chunk.IsStrict);
                        Push(value);
                        break;
                    }
                    case OpCode.GetIndex:
                    {
                        var key = Pop();
                        var obj = Pop();
                        if (key.IsAnyInt && obj.GetData() is ITypedArrayAccess taGet)
                        {
                            var idx = key.Int;
                            Push(idx >= 0 && idx < taGet.Length ? taGet.GetElement(idx) : ScriptVar.CreateUndefined());
                        }
                        else
                        {
                            Push(GetMember(obj, KeyName(key)));
                        }
                        break;
                    }
                    case OpCode.GetIndexMethod:
                    {
                        // Pops key, peeks obj (keeps it), pushes fn.
                        // Stack: [obj, key] → [obj, fn]
                        // Preserves receiver for subsequent CallMethod.
                        var key = Pop();
                        var obj = Peek();
                        Push(GetMember(obj, KeyName(key)));
                        break;
                    }
                    case OpCode.SetIndex:
                    {
                        var value = Pop();
                        var key = Pop();
                        var obj = Pop();
                        if (key.IsAnyInt && obj.GetData() is ITypedArrayAccess taSet2)
                        {
                            var idx = key.Int;
                            if (idx >= 0 && idx < taSet2.Length) taSet2.SetElement(idx, value);
                        }
                        else
                        {
                            SetMember(obj, KeyName(key), value, chunk.IsStrict);
                        }
                        Push(value);
                        break;
                    }
                    case OpCode.DeleteProp:
                    {
                        var name = chunk.Names[ReadOperand(code, ref ip)];
                        DeleteMember(Pop(), name);
                        Push(ScriptVar.FromBool(true));
                        break;
                    }
                    case OpCode.DeleteIndex:
                    {
                        var key = Pop();
                        DeleteMember(Pop(), KeyName(key));
                        Push(ScriptVar.FromBool(true));
                        break;
                    }

                    case OpCode.In:
                    {
                        var obj = Pop();
                        var key = Pop();
                        // Proxy [[Has]] trap
                        if (obj.IsProxy)
                        {
                            var handler = obj.ProxyHandler;
                            var hasTrap = handler?.FindChild("has")?.Var;
                            if (hasTrap != null && hasTrap.IsFunction)
                            {
                                var r = InvokeCallable(hasTrap, handler, new[] { obj.ProxyTarget, ScriptVar.FromString(key.String) });
                                Push(ScriptVar.FromBool(r?.Bool == true));
                                break;
                            }
                            obj = obj.ProxyTarget ?? obj;
                        }
                        var exists = obj.FindChild(key.String) != null ||
                                     (engine != null && engine.FindInParentClasses(obj, key.String) != null);
                        Push(ScriptVar.FromBool(exists));
                        break;
                    }
                    case OpCode.InstanceOf:
                    {
                        var ctor = Pop();
                        var value = Pop();
                        // Check for [Symbol.hasInstance] override on the constructor
                        var hasInstanceLink = ctor.FindChild(WellKnownSymbols.HasInstance.GetSymbolKey());
                        if (hasInstanceLink != null && hasInstanceLink.Var.IsFunction)
                        {
                            var result = InvokeCallable(hasInstanceLink.Var, ctor, new[] { value });
                            Push(ScriptVar.FromBool(result?.Bool ?? false));
                        }
                        else
                        {
                            Push(ScriptVar.FromBool(IsInstanceOf(value, ctor)));
                        }
                        break;
                    }

                    case OpCode.Binary:
                    {
                        var site = ip;
                        var operatorCode = (ScriptLex.LexTypes)ReadOperand(code, ref ip);
                        var bv = PopValue();
                        var av = PopValue();
                        // int fast path: compute and push an unboxed Value — no MathsOp
                        // dispatch and no ScriptVar allocation for the result.
                        if (TryLong(av, out var al) && TryLong(bv, out var bl)
                            && IntBinaryValue(al, bl, operatorCode, out var fast))
                        {
                            ref var bp = ref binProf[site];
                            bp.LeftTypes  |= Chunk.BinaryTypeFlags.Int;
                            bp.RightTypes |= Chunk.BinaryTypeFlags.Int;
                            PushValue(fast);
                        }
                        else
                        {
                            var a = av.ToScriptVar();
                            var b = bv.ToScriptVar();
                            RecordBinaryOpTypes(binProf, site, a, b);
                            Push(a.MathsOp(b, operatorCode));
                        }
                        break;
                    }
                    case OpCode.BinaryConst:
                    {
                        // Fused Constant + Binary: the right operand is a literal,
                        // read from the constant pool instead of the stack.
                        var site = ip;
                        var operatorCode = (ScriptLex.LexTypes)ReadOperand(code, ref ip);
                        var constant = chunk.Constants[ReadOperand(code, ref ip)];
                        var av = PopValue();
                        var rightFlag = constant.Kind switch
                        {
                            ConstantKind.Int    => Chunk.BinaryTypeFlags.Int,
                            ConstantKind.Double => Chunk.BinaryTypeFlags.Double,
                            ConstantKind.String => Chunk.BinaryTypeFlags.String,
                            _                   => Chunk.BinaryTypeFlags.Other,
                        };
                        ref var bcp = ref binProf[site];
                        bcp.RightTypes |= rightFlag;
                        // Int-vs-int-literal fast path: compute directly into an unboxed
                        // Value, skipping the constant materialization and MathsOp.
                        if (constant.Kind == ConstantKind.Int && TryLong(av, out var al) &&
                            IntBinaryValue(al, (long)constant.IntValue, operatorCode, out var fast))
                        {
                            bcp.LeftTypes |= Chunk.BinaryTypeFlags.Int;
                            PushValue(fast);
                        }
                        else
                        {
                            var a = av.ToScriptVar();
                            bcp.LeftTypes |= TypeFlagOf(a);
                            Push(a.MathsOp(constant.Materialize(), operatorCode));
                        }
                        break;
                    }
                    case OpCode.BinaryIntConst:
                    {
                        // Like BinaryConst but the integer value is stored inline in
                        // the instruction stream, skipping the constant-pool lookup.
                        // Emitted when the right operand is a known integer literal.
                        var site = ip;
                        var operatorCode = (ScriptLex.LexTypes)ReadOperand(code, ref ip);
                        var intValue = ReadOperand(code, ref ip);
                        var av = PopValue();
                        ref var bip = ref binProf[site];
                        bip.RightTypes |= Chunk.BinaryTypeFlags.Int;
                        if (TryLong(av, out var al) && IntBinaryValue(al, (long)intValue, operatorCode, out var fast))
                        {
                            bip.LeftTypes |= Chunk.BinaryTypeFlags.Int;
                            PushValue(fast);
                        }
                        else
                        {
                            var a = av.ToScriptVar();
                            bip.LeftTypes |= TypeFlagOf(a);
                            Push(a.MathsOp(ScriptVar.FromInt(intValue), operatorCode));
                        }
                        break;
                    }
                    case OpCode.Shift:
                    {
                        var operatorCode = (ScriptLex.LexTypes)ReadOperand(code, ref ip);
                        var b = Pop();
                        var a = Pop();
                        Push(ApplyShift(a, b, operatorCode));
                        break;
                    }
                    case OpCode.Negate:
                    {
                        var a = Pop();
                        // numeric fast path avoids the MathsOp dispatch + a temp;
                        // fall back to MathsOp for the (rare) non-numeric cases
                        // Negating integer 0 yields IEEE -0.0 (a double), so 1/(-0) is
                        // -Infinity and Object.is(-0, 0) is false, matching JS.
                        if (a.IsAnyInt) Push(a.Long == 0 ? ScriptVar.FromDouble(-0.0) : IntOrDouble(-a.Long));
                        else if (a.IsDouble) Push(ScriptVar.FromDouble(-a.Float));
                        else if (a.IsBigInt) Push(ScriptVar.CreateBigInt(-a.BigIntData));
                        else Push(Zero.MathsOp(a, (ScriptLex.LexTypes)'-'));
                        break;
                    }
                    case OpCode.Not:
                    {
                        var a = Pop();
                        Push(a.Bool ? SharedFalse : SharedTrue);
                        break;
                    }
                    case OpCode.BitNot:
                    {
                        var a = Pop();
                        if (a.IsBigInt) Push(ScriptVar.CreateBigInt(~a.BigIntData));
                        else Push(ScriptVar.FromInt(~a.Int));
                        break;
                    }
                    case OpCode.ToNumber:
                    {
                        var a = Pop();
                        Push(CoerceToNumber(a));
                        break;
                    }
                    case OpCode.Typeof:
                    {
                        var a = Pop();
                        Push(ScriptVar.FromString(a.GetObjectType()));
                        break;
                    }

                    case OpCode.NewObject:
                        Push(ScriptVar.CreateObject());
                        break;
                    case OpCode.NewArray:
                        Push(ScriptVar.CreateArray());
                        break;
                    case OpCode.InitProp:
                    {
                        var name = chunk.Names[ReadOperand(code, ref ip)];
                        var value = Pop();
                        Peek().AddChild(name, value); // object kept on stack
                        break;
                    }
                    case OpCode.InitPropOverwrite:
                    {
                        // After a spread, the key may already exist — overwrite so the
                        // later literal key wins instead of appending a shadowed dup.
                        var name = chunk.Names[ReadOperand(code, ref ip)];
                        var value = Pop();
                        Peek().AddChildNoDup(name, value); // object kept on stack
                        break;
                    }
                    case OpCode.DefineGetter:
                    {
                        var name = chunk.Names[ReadOperand(code, ref ip)];
                        var getter = Pop();
                        var obj = Peek();
                        var link = obj.FindChild(name) ?? obj.AddChild(name, ScriptVar.CreateUndefined());
                        link.Getter = getter;
                        break;
                    }
                    case OpCode.DefineSetter:
                    {
                        var name = chunk.Names[ReadOperand(code, ref ip)];
                        var setter = Pop();
                        var obj = Peek();
                        var link = obj.FindChild(name) ?? obj.AddChild(name, ScriptVar.CreateUndefined());
                        link.Setter = setter;
                        break;
                    }
                    case OpCode.InitElem:
                    {
                        var index = ReadOperand(code, ref ip);
                        var value = Pop();
                        Peek().SetArrayIndex(index, value); // array kept on stack
                        break;
                    }
                    case OpCode.SetPropDynamic:
                    {
                        var value = Pop();
                        var key = Pop();
                        // Use KeyName so a Symbol computed key ({ [sym]: v }) stores under
                        // its unique identity key — the same key o[sym] reads — and an int
                        // key uses the array index name. key.String alone mismatched both.
                        Peek().AddChildNoDup(KeyName(key), value); // object kept on stack
                        break;
                    }

                    case OpCode.Jump:
                    {
                        var target = ReadOperand(code, ref ip);
                        // ip now equals the fallthrough offset (op + 5 bytes).
                        // A target before that is a loop back-edge.
                        if (target < ip)
                        {
                            chunk.BackEdgeCount++;

                            // On-stack replacement: a loop in a frame that is already
                            // running (and so will never tier up on invocation count)
                            // gets compiled and resumed in JIT code at the loop header.
                            // Live locals are shared through env; the operand stack is
                            // empty at a structured back-edge, so nothing else migrates.
                            if (genObj == null && gsState == null && !bypassJit
                                && chunk.BackEdgeCount >= JitThresholds.OsrBackEdgeThreshold
                                && JitRegistry.Current is IOsrCompiler osr
                                && !chunk.OsrDeclinedOffsets.Contains(target))
                            {
                                if (!chunk.OsrEntries.TryGetValue(target, out var osrEntry))
                                {
                                    try { osrEntry = osr.CompileOsr(chunk, target); }
                                    catch { osrEntry = null; }
                                    if (osrEntry != null) chunk.OsrEntries[target] = osrEntry;
                                    else chunk.OsrDeclinedOffsets.Add(target);
                                }
                                // Hand off: the OSR entry runs the rest of the function
                                // and its result is this invocation's result. The Execute
                                // finally block still runs on this return path.
                                if (osrEntry != null)
                                    return osrEntry(this, System.Array.Empty<ScriptVar>(), env);
                            }
                        }
                        ip = target;
                        break;
                    }
                    case OpCode.JumpIfFalse:
                    {
                        var target = ReadOperand(code, ref ip);
                        if (!Pop().Bool) ip = target;
                        break;
                    }
                    case OpCode.JumpIfTrue:
                    {
                        var target = ReadOperand(code, ref ip);
                        if (Pop().Bool) ip = target;
                        break;
                    }
                    case OpCode.JumpIfFalseOrPop:
                    {
                        var target = ReadOperand(code, ref ip);
                        if (!Peek().Bool) ip = target; else Pop();
                        break;
                    }
                    case OpCode.JumpIfTrueOrPop:
                    {
                        var target = ReadOperand(code, ref ip);
                        if (Peek().Bool) ip = target; else Pop();
                        break;
                    }
                    case OpCode.JumpIfDefined:
                    {
                        var target = ReadOperand(code, ref ip);
                        var val = Pop();
                        if (!val.IsUndefined) ip = target;
                        break;
                    }
                    case OpCode.JumpIfNullOrUndefined:
                    {
                        var target = ReadOperand(code, ref ip);
                        var val = Pop();
                        if (val.IsNull || val.IsUndefined)
                        {
                            Push(SharedUndefined);
                            ip = target;
                        }
                        else
                        {
                            Push(val);
                        }
                        break;
                    }

                    case OpCode.MakeClosure:
                    {
                        var fnChunk = chunk.Functions[ReadOperand(code, ref ip)];
                        var fn = ScriptVar.CreateFunction();
                        fn.SetData(new VmFunction(fnChunk, env));
                        Push(fn);
                        break;
                    }
                    case OpCode.Call:
                    {
                        var site = ip;
                        var argc = ReadOperand(code, ref ip);
                        var callee = stack[sp - argc - 1].ToScriptVar();
                        RecordCallSite(callProf, site, callee);
                        if (callee != null && callee.IsFunction && !callee.IsNative)
                        {
                            // Fast path: VM function — bind args directly, no ScriptVar[].
                            var result = InvokeVmFunctionFromStack(callee, null, argc);
                            sp--; // discard the callee left below the (already popped) args
                            Push(result);
                        }
                        else
                        {
                            var args = PopArgs(argc);
                            Push(InvokeCallable(Pop(), null, args));
                        }
                        break;
                    }
                    case OpCode.CallMethod:
                    {
                        var site = ip;
                        var argc = ReadOperand(code, ref ip);
                        var callee = stack[sp - argc - 1].ToScriptVar();
                        RecordCallSite(callProf, site, callee);
                        if (callee != null && callee.IsFunction && !callee.IsNative)
                        {
                            // Fast path: VM function — bind args directly, no ScriptVar[].
                            var receiver = stack[sp - argc - 2].ToScriptVar();
                            var result = InvokeVmFunctionFromStack(callee, receiver, argc);
                            sp -= 2; // discard callee and receiver below the args
                            Push(result);
                        }
                        else
                        {
                            var args = PopArgs(argc);
                            var c = Pop();
                            var receiver = Pop();
                            Push(InvokeCallable(c, receiver, args));
                        }
                        break;
                    }
                    case OpCode.TailCall:
                    {
                        // Tail-position direct call.  For synchronous VM functions we
                        // signal the InvokeVmFunctionFromStack trampoline (set the
                        // pending fields, return null) so the callee executes without
                        // adding a C# frame — enabling unbounded tail recursion.
                        var site = ip;
                        var argc = ReadOperand(code, ref ip);
                        var callee = stack[sp - argc - 1].ToScriptVar();
                        RecordCallSite(callProf, site, callee);
                        if (callee != null && callee.IsFunction && !callee.IsNative)
                        {
                            var vmfn = (VmFunction)callee.GetData();
                            if (!vmfn.Body.IsAsync && !vmfn.Body.IsGenerator)
                            {
                                var argBase = sp - argc;
                                var tArgs = new ScriptVar[argc];
                                for (var j = 0; j < argc; j++) tArgs[j] = stack[argBase + j].ToScriptVar();
                                sp = argBase - 1; // discard args + callee
                                _pendingTailCallFn   = vmfn;
                                _pendingTailCallArgs = tArgs;
                                _pendingTailCallThis = null;
                                return null; // trampoline signal
                            }
                            // Async/generator: normal path (they return iterator/promise immediately)
                            var result = InvokeVmFunctionFromStack(callee, null, argc);
                            sp--;
                            return result ?? SharedUndefined;
                        }
                        else
                        {
                            var args = PopArgs(argc);
                            return InvokeCallable(Pop(), null, args) ?? SharedUndefined;
                        }
                    }
                    case OpCode.TailCallMethod:
                    {
                        // Tail-position method call — same trampoline logic as TailCall.
                        var site = ip;
                        var argc = ReadOperand(code, ref ip);
                        var callee = stack[sp - argc - 1].ToScriptVar();
                        RecordCallSite(callProf, site, callee);
                        if (callee != null && callee.IsFunction && !callee.IsNative)
                        {
                            var vmfn = (VmFunction)callee.GetData();
                            if (!vmfn.Body.IsAsync && !vmfn.Body.IsGenerator)
                            {
                                var argBase = sp - argc;
                                var tArgs = new ScriptVar[argc];
                                for (var j = 0; j < argc; j++) tArgs[j] = stack[argBase + j].ToScriptVar();
                                var tThis = stack[sp - argc - 2].ToScriptVar();
                                sp = argBase - 2; // discard args + callee + receiver
                                _pendingTailCallFn   = vmfn;
                                _pendingTailCallArgs = tArgs;
                                _pendingTailCallThis = tThis;
                                return null; // trampoline signal
                            }
                            var receiver = stack[sp - argc - 2].ToScriptVar();
                            var result = InvokeVmFunctionFromStack(callee, receiver, argc);
                            sp -= 2;
                            return result ?? SharedUndefined;
                        }
                        else
                        {
                            var args = PopArgs(argc);
                            var c = Pop();
                            var receiver = Pop();
                            return InvokeCallable(c, receiver, args) ?? SharedUndefined;
                        }
                    }
                    case OpCode.Yield:
                    {
                        var yieldVal = Pop();
                        if (gsState != null)
                        {
                            // Stackless path: save ip (already advanced past Yield) and exit the loop.
                            gsState.YieldedValue = yieldVal;
                            gsState.SavedIp = ip;
                            generatorYielded = true;
                            break; // exits switch; while condition !generatorYielded exits loop
                        }
                        if (genObj == null)
                            throw new ScriptException("yield used outside generator");
                        var resumeVal = genObj.Yield(yieldVal);
                        Push(resumeVal);
                        break;
                    }
                    case OpCode.GetIterator:
                    {
                        var iterable = Pop();
                        // If it already has .next, it's an iterator — pass through
                        var nextLink = iterable.FindChild("next");
                        if (nextLink != null)
                        {
                            Push(iterable);
                            break;
                        }
                        // Check for [Symbol.iterator] method
                        var symIterLink = iterable.FindChild(WellKnownSymbols.Iterator.GetSymbolKey());
                        if (symIterLink != null && symIterLink.Var.IsFunction)
                        {
                            var iterator = InvokeCallable(symIterLink.Var, iterable, System.Array.Empty<ScriptVar>());
                            Push(iterator ?? ScriptVar.CreateObject());
                            break;
                        }
                        // If it's an array, use a lightweight native iterator that
                        // avoids allocating a closure, a next-function, and a
                        // {done,value} object on every iteration.
                        if (iterable.IsArray)
                        {
                            Push(ScriptVar.CreateNativeArrayIterator(iterable));
                            break;
                        }
                        // Strings iterate by Unicode code point (surrogate-pair-aware),
                        // matching the JS string iterator spec.
                        if (iterable.IsString)
                        {
                            var s = iterable.String;
                            var codePointArr = ScriptVar.CreateArray();
                            for (var si = 0; si < s.Length;)
                            {
                                var adv = char.IsHighSurrogate(s[si]) && si + 1 < s.Length
                                          && char.IsLowSurrogate(s[si + 1]) ? 2 : 1;
                                codePointArr.AppendArrayElement(ScriptVar.FromString(s.Substring(si, adv)));
                                si += adv;
                            }
                            Push(ScriptVar.CreateNativeArrayIterator(codePointArr));
                            break;
                        }
                        // Unknown — return an immediately-done iterator
                        {
                            var doneIter = ScriptVar.CreateObject();
                            var doneFn = ScriptVar.CreateNativeFunction();
                            doneFn.SetCallback((scope, _) =>
                            {
                                var result = ScriptVar.CreateObject();
                                result.AddChild("value", ScriptVar.CreateUndefined());
                                result.AddChild("done", ScriptVar.FromBool(true));
                                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(result);
                            }, null);
                            doneIter.AddChild("next", doneFn);
                            Push(doneIter);
                        }
                        break;
                    }
                    case OpCode.ForOfStep:
                    {
                        var exitOffset = ReadOperand(code, ref ip);
                        var iter = Pop();

                        // Fast path: native array iterator — no function call, no {done,value} allocation
                        if (iter.IsNativeArrayIterator)
                        {
                            var idx = iter.NativeIterIndex;
                            if (idx >= iter.NativeIterArray.GetArrayLength())
                            {
                                ip = exitOffset;
                            }
                            else
                            {
                                iter.NativeIterIndex = idx + 1;
                                Push(iter.NativeIterArray.GetArrayIndex(idx));
                            }
                            break;
                        }

                        // Slow path: general iterator protocol
                        var nextLink = iter.FindChild("next");
                        ScriptVar result;
                        if (nextLink != null)
                            result = InvokeCallable(nextLink.Var, iter, Array.Empty<ScriptVar>());
                        else
                            result = null;
                        var done = result?.FindChild("done")?.Var.Bool ?? true;
                        if (done) { ip = exitOffset; break; }
                        Push(result.FindChild("value")?.Var ?? ScriptVar.CreateUndefined());
                        break;
                    }
                    case OpCode.GetAsyncIterator:
                    {
                        var iterable = Pop();
                        // Check for [Symbol.asyncIterator] first
                        var asyncIterKey = WellKnownSymbols.AsyncIterator?.GetSymbolKey();
                        if (asyncIterKey != null)
                        {
                            var asyncIterLink = iterable.FindChild(asyncIterKey);
                            if (asyncIterLink != null && asyncIterLink.Var.IsFunction)
                            {
                                var iterator = InvokeCallable(asyncIterLink.Var, iterable, System.Array.Empty<ScriptVar>());
                                Push(iterator ?? ScriptVar.CreateObject());
                                break;
                            }
                        }
                        // Fall back to [Symbol.iterator]
                        var nextLink2 = iterable.FindChild("next");
                        if (nextLink2 != null) { Push(iterable); break; }
                        var symIterLink2 = iterable.FindChild(WellKnownSymbols.Iterator.GetSymbolKey());
                        if (symIterLink2 != null && symIterLink2.Var.IsFunction)
                        {
                            var iterator = InvokeCallable(symIterLink2.Var, iterable, System.Array.Empty<ScriptVar>());
                            Push(iterator ?? ScriptVar.CreateObject());
                            break;
                        }
                        if (iterable.IsArray)
                        {
                            var idx2 = new[] { 0 };
                            var len2 = iterable.GetArrayLength();
                            var iterObj2 = ScriptVar.CreateObject();
                            var nextFn2 = ScriptVar.CreateNativeFunction();
                            nextFn2.SetCallback((scope, _) =>
                            {
                                var r2 = ScriptVar.CreateObject();
                                if (idx2[0] < len2)
                                {
                                    r2.AddChild("value", iterable.GetArrayIndex(idx2[0]++));
                                    r2.AddChild("done", ScriptVar.FromBool(false));
                                }
                                else
                                {
                                    r2.AddChild("value", ScriptVar.CreateUndefined());
                                    r2.AddChild("done", ScriptVar.FromBool(true));
                                }
                                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(r2);
                            }, null);
                            iterObj2.AddChild("next", nextFn2);
                            Push(iterObj2);
                            break;
                        }
                        Push(ScriptVar.CreateObject()); // empty done iterator
                        break;
                    }
                    case OpCode.ForAwaitOfStep:
                    {
                        var exitOffset = ReadOperand(code, ref ip);
                        if (genObj == null)
                            throw new ScriptException("for await...of used outside async function");
                        var iter = Pop();
                        var nextLink = iter.FindChild("next");
                        ScriptVar nextResult;
                        if (nextLink != null)
                            nextResult = InvokeCallable(nextLink.Var, iter, Array.Empty<ScriptVar>());
                        else
                            nextResult = ScriptVar.CreateObject();
                        // Yield the Promise so the async drive loop awaits it
                        var resolved = genObj.Yield(nextResult);
                        var done = resolved?.FindChild("done")?.Var.Bool ?? true;
                        if (done) { ip = exitOffset; break; }
                        Push(resolved?.FindChild("value")?.Var ?? ScriptVar.CreateUndefined());
                        break;
                    }
                    case OpCode.PushImportMeta:
                    {
                        var meta = ScriptVar.CreateObject();
                        var modulePath = engine.CurrentModulePath ?? string.Empty;
                        meta.AddChild("url", ScriptVar.FromString(modulePath));
                        meta.AddChild("filename", ScriptVar.FromString(modulePath));
                        var dir = modulePath.Length > 0
                            ? System.IO.Path.GetDirectoryName(modulePath) ?? string.Empty
                            : string.Empty;
                        meta.AddChild("dirname", ScriptVar.FromString(dir));
                        Push(meta);
                        break;
                    }
                    case OpCode.DynamicImport:
                    {
                        var specifier = Pop();
                        ScriptVar importedExports;
                        try
                        {
                            var globalVars = env.Global().Vars;
                            var requireFn = globalVars.FindChild("require")?.Var;
                            if (requireFn == null)
                                throw new ScriptException($"Cannot dynamic import: require not available");
                            importedExports = InvokeCallable(requireFn, null, new[] { specifier });
                        }
                        catch (ScriptException ex)
                        {
                            var errorVar = ScriptVar.FromString(ex.Message);
                            Push(PromiseObject.Rejected(errorVar).ToScriptVar(this));
                            break;
                        }
                        Push(PromiseObject.Resolved(importedExports).ToScriptVar(this));
                        break;
                    }
                    case OpCode.New:
                    {
                        var argc = ReadOperand(code, ref ip);
                        var ctor = stack[sp - argc - 1].ToScriptVar();
                        // Fast path: compiled constructor with args on the stack —
                        // bind them directly into the call frame (no ScriptVar[]).
                        if (ctor != null && ctor.IsFunction && !ctor.IsNative)
                        {
                            var instance = ScriptVar.CreateShapeTracked();
                            instance.AddChild(ScriptVar.PrototypeClassName, ctor);
                            var result = InvokeVmFunctionFromStack(ctor, instance, argc);
                            sp--; // discard the constructor left below the (popped) args
                            // a constructor that returns an object replaces the instance
                            Push(result != null && result.IsObject ? result : instance);
                        }
                        else
                        {
                            var args = PopArgs(argc);
                            Push(Construct(Pop(), args));
                        }
                        break;
                    }

                    case OpCode.PushSpread:
                    {
                        var spreadArr = Pop();
                        var arr = Peek(); // the array being built stays on stack
                        var elems = CollectSpreadElements(spreadArr);
                        for (var si = 0; si < elems.Length; si++)
                            arr.AppendArrayElement(elems[si]); // O(1) per element
                        break;
                    }
                    case OpCode.AppendElem:
                    {
                        var value = Pop();
                        Peek().AppendArrayElement(value); // arr stays on stack
                        break;
                    }
                    case OpCode.MergeObject:
                    {
                        var source = Pop();
                        var target = Peek(); // stays on stack
                        var member = source.FirstChild;
                        while (member != null)
                        {
                            if (member.Name != ScriptVar.PrototypeClassName)
                                SetMember(target, member.Name, member.Var);
                            member = member.Next;
                        }
                        break;
                    }
                    case OpCode.CallSpread:
                    {
                        var argsArr = Pop();
                        var callee = Pop();
                        var args = CollectSpreadElements(argsArr);
                        Push(InvokeCallable(callee, null, args));
                        break;
                    }
                    case OpCode.CallMethodSpread:
                    {
                        var argsArr = Pop();
                        var callee = Pop();
                        var receiver = Pop();
                        var args = CollectSpreadElements(argsArr);
                        Push(InvokeCallable(callee, receiver, args));
                        break;
                    }
                    case OpCode.NewSpread:
                    {
                        var argsArr = Pop();
                        var ctor = Pop();
                        var args = CollectSpreadElements(argsArr);
                        Push(Construct(ctor, args));
                        break;
                    }

                    case OpCode.TaggedTemplate:
                    {
                        var numStrings = ReadOperand(code, ref ip);
                        var numExprs   = ReadOperand(code, ref ip);

                        var exprs = new ScriptVar[numExprs];
                        for (var k = numExprs - 1; k >= 0; k--)
                            exprs[k] = Pop();

                        var rawArr = ScriptVar.CreateArray();
                        for (var k = numStrings - 1; k >= 0; k--)
                            rawArr.SetArrayIndex(k, Pop());

                        var cookedArr = ScriptVar.CreateArray();
                        for (var k = numStrings - 1; k >= 0; k--)
                            cookedArr.SetArrayIndex(k, Pop());

                        cookedArr.AddChild("raw", rawArr);

                        var tag = Pop();

                        var callArgs = new ScriptVar[1 + numExprs];
                        callArgs[0] = cookedArr;
                        for (var k = 0; k < numExprs; k++)
                            callArgs[k + 1] = exprs[k];

                        Push(InvokeCallable(tag, null, callArgs));
                        break;
                    }

                    case OpCode.Throw:
                    {
                        var ex = new JITException(Pop());
                        if (!DispatchException(ex, ref ip, env, chunk, savedTryDepth)) throw ex;
                        break;
                    }

                    case OpCode.EnterTry:
                    {
                        var catchPC     = ReadOperand(code, ref ip);
                        var finallyPC   = ReadOperand(code, ref ip);
                        var catchVarIdx = ReadOperand(code, ref ip);
                        tryStack.Push(new TryFrame
                        {
                            CatchPC     = catchPC,
                            FinallyPC   = finallyPC,
                            CatchVarIdx = catchVarIdx,
                            StackDepth  = sp,
                        });
                        break;
                    }
                    case OpCode.LeaveTry:
                    {
                        // Normal exit from try body: pop the handler frame and jump
                        // to the finally block (or straight to after if no finally).
                        var destPC = ReadOperand(code, ref ip);
                        tryStack.Pop();
                        ip = destPC;
                        break;
                    }
                    case OpCode.LeaveCatch:
                    {
                        // Normal exit from catch body: pop the catch-protecting frame
                        // (pushed by DispatchException to guard against exceptions
                        // inside the catch body) and jump to finally or after.
                        var destPC = ReadOperand(code, ref ip);
                        if (tryStack.Count > savedTryDepth)
                        {
                            var top = tryStack.Peek();
                            if (top.CatchPC == -1) // it is a catch-protecting frame
                                tryStack.Pop();
                        }
                        ip = destPC;
                        break;
                    }
                    case OpCode.LeaveFinally:
                    {
                        if (pendingException != null)
                        {
                            var ex = pendingException;
                            pendingException = null;
                            if (!DispatchException(ex, ref ip, env, chunk, savedTryDepth)) throw ex;
                            break;
                        }
                        if (hasPendingReturn)
                        {
                            // Chain through any further enclosing finally blocks.
                            int nextFinally = -1;
                            while (tryStack.Count > savedTryDepth)
                            {
                                var frame = tryStack.Pop();
                                if (frame.FinallyPC >= 0) { nextFinally = frame.FinallyPC; break; }
                            }
                            if (nextFinally >= 0) { ip = nextFinally; break; }
                            // No more finally blocks — perform the actual return.
                            hasPendingReturn = false;
                            return pendingReturnValue;
                        }
                        // Normal completion: ip is already past the finally block.
                        break;
                    }
                    case OpCode.SaveReturn:
                        pendingReturnValue = Pop();
                        hasPendingReturn = true;
                        break;

                    case OpCode.EnterBlock:
                    {
                        blockEnvStack ??= new Stack<Environment>();
                        blockEnvStack.Push(env);
                        env = new Environment(ScriptVar.CreateObject(), env, isBlockScope: true);
                        break;
                    }
                    case OpCode.LeaveBlock:
                    {
                        env = blockEnvStack.Pop();
                        break;
                    }

                    case OpCode.Return:
                    {
                        var retVal = Pop();
                        if (gsState != null) gsState.Done = true;
                        return retVal;
                    }
                    case OpCode.Halt:
                        if (gsState != null) gsState.Done = true;
                        return null;

                    // --- Narrow (2-byte) forms: opcode + 1-byte index --------

                    case OpCode.ConstantN:
                        Push(chunk.Constants[code[ip++]].Materialize());
                        break;

                    case OpCode.GetVarN:
                    {
                        var site = ip;
                        var nameIdx = code[ip++];
                        var link = ResolveCached(cache, chunk, site, env, nameIdx);
                        if (link != null) { Push(link.Var); break; }
                        if (chunk.Names[nameIdx] == "globalThis") { Push(env.Global().Vars); break; }
                        Push(SharedUndefined);
                        break;
                    }
                    case OpCode.SetVarN:
                    {
                        var site = ip;
                        var nameIdx = code[ip++];
                        var value = Pop();
                        var link = ResolveCached(cache, chunk, site, env, nameIdx);
                        if (link != null)
                        {
                            link.ReplaceWith(value);
                        }
                        else
                        {
                            if (chunk.IsStrict)
                                throw new ScriptException($"ReferenceError: '{chunk.Names[nameIdx]}' is not defined");
                            var global = env.Global();
                            global.Vars.AddChildNoDup(chunk.Names[nameIdx], value);
                            global.Version++;
                        }
                        Push(value);
                        break;
                    }
                    case OpCode.DeclareVarN:
                    {
                        var name = chunk.Names[code[ip++]];
                        var declEnv = env;
                        while (declEnv.IsBlockScope && declEnv.Parent != null)
                            declEnv = declEnv.Parent;
                        declEnv.Vars.FindChildOrCreate(name);
                        declEnv.Version++;
                        break;
                    }
                    case OpCode.DeclareConstN:
                    {
                        var name = chunk.Names[code[ip++]];
                        env.Vars.FindChildOrCreate(name, ScriptVar.Flags.Undefined, readOnly: true);
                        env.Version++;
                        break;
                    }
                    case OpCode.DeclareLocalN:
                    {
                        var name = chunk.Names[code[ip++]];
                        env.Vars.FindChildOrCreate(name);
                        env.Version++;
                        break;
                    }
                    case OpCode.GetPropN:
                    {
                        var nameIdx = code[ip++];
                        var name = chunk.Names[nameIdx];
                        var obj = Pop();

                        var cacheSlot = (int)((uint)nameIdx * 2654435761u >> 24);
                        ref var ce = ref _propCache[cacheSlot];
                        if (ReferenceEquals(ce.Name, name))
                        {
                            if (ce.ShapeId > 0)
                            {
                                var shapedObj = obj as ShapedScriptVar;
                                var shp = shapedObj?._shape;
                                if (shp != null && ce.ShapeId == shp.Id)
                                {
                                    var sl = shapedObj._shapeRoot;
                                    for (int _i = 0; _i < ce.SlotIndex; _i++) sl = sl?.Next;
                                    if (sl != null)
                                    {
                                        Push(sl.Getter != null
                                            ? InvokeCallable(sl.Getter, obj, System.Array.Empty<ScriptVar>())
                                            : sl.Var);
                                        break;
                                    }
                                }
                            }
                            else if (ReferenceEquals(ce.Object, obj) &&
                                     ce.ShapeVersion == obj.ShapeVersion &&
                                     ce.Link != null)
                            {
                                Push(ce.Link.Getter != null
                                    ? InvokeCallable(ce.Link.Getter, obj, System.Array.Empty<ScriptVar>())
                                    : ce.Link.Var);
                                break;
                            }
                        }

                        if (obj.IsProxy) { Push(GetMember(obj, name)); break; }

                        {
                            var shapedObj = obj as ShapedScriptVar;
                            var shp = shapedObj?._shape;
                            if (shp != null && shp.Slots.TryGetValue(name, out var slotIdx))
                            {
                                var sl = shapedObj._shapeRoot;
                                for (int _i = 0; _i < slotIdx; _i++) sl = sl?.Next;
                                if (sl != null)
                                {
                                    var propResult = sl.Getter != null
                                        ? InvokeCallable(sl.Getter, obj, System.Array.Empty<ScriptVar>())
                                        : sl.Var;
                                    ce.Name = name; ce.ShapeId = shp.Id; ce.SlotIndex = slotIdx;
                                    ce.Object = null; ce.Link = null;
                                    Push(propResult);
                                    break;
                                }
                            }
                        }

                        var link = obj.FindChild(name);
                        if (link == null && engine != null) link = engine.FindInParentClasses(obj, name);

                        ScriptVar propResult2;
                        if (link != null)
                        {
                            if (link.Getter != null)
                                propResult2 = InvokeCallable(link.Getter, obj, System.Array.Empty<ScriptVar>());
                            else
                                propResult2 = link.Var;
                            if (obj.IsObject || obj.IsArray)
                            {
                                ce.Name = name; ce.ShapeId = 0;
                                ce.Object = obj; ce.ShapeVersion = obj.ShapeVersion; ce.Link = link;
                            }
                        }
                        else
                        {
                            if (obj.IsArray && name == "length") propResult2 = ScriptVar.FromInt(obj.GetArrayLength());
                            else if (obj.IsString && name == "length") propResult2 = ScriptVar.FromInt(obj.String.Length);
                            else if (name == "size" && obj.GetData() is INativeContainer container2) propResult2 = ScriptVar.FromInt(container2.GetSize());
                            else propResult2 = ScriptVar.CreateUndefined();
                        }
                        Push(propResult2);
                        break;
                    }
                    case OpCode.SetPropN:
                    {
                        var name = chunk.Names[code[ip++]];
                        var value = Pop();
                        var obj = Pop();
                        SetMember(obj, name, value, chunk.IsStrict);
                        Push(value);
                        break;
                    }
                    case OpCode.InitPropN:
                    {
                        var name = chunk.Names[code[ip++]];
                        var value = Pop();
                        Peek().AddChild(name, value);
                        break;
                    }

                    // --- Superinstructions ----------------------------------------

                    case OpCode.SetVarPop:
                    {
                        var site = ip;
                        var nameIdx = ReadOperand(code, ref ip);
                        var value = Pop();
                        var link = ResolveCached(cache, chunk, site, env, nameIdx);
                        if (link != null)
                        {
                            link.ReplaceWith(value);
                        }
                        else
                        {
                            if (chunk.IsStrict)
                                throw new ScriptException($"ReferenceError: '{chunk.Names[nameIdx]}' is not defined");
                            var global = env.Global();
                            global.Vars.AddChildNoDup(chunk.Names[nameIdx], value);
                            global.Version++;
                        }
                        break;
                    }
                    case OpCode.SetVarPopN:
                    {
                        var site = ip;
                        var nameIdx = code[ip++];
                        var value = Pop();
                        var link = ResolveCached(cache, chunk, site, env, nameIdx);
                        if (link != null)
                        {
                            link.ReplaceWith(value);
                        }
                        else
                        {
                            if (chunk.IsStrict)
                                throw new ScriptException($"ReferenceError: '{chunk.Names[nameIdx]}' is not defined");
                            var global = env.Global();
                            global.Vars.AddChildNoDup(chunk.Names[nameIdx], value);
                            global.Version++;
                        }
                        break;
                    }
                    case OpCode.SetPropPop:
                    {
                        var name = chunk.Names[ReadOperand(code, ref ip)];
                        var value = Pop();
                        var obj = Pop();
                        SetMember(obj, name, value, chunk.IsStrict);
                        break;
                    }
                    case OpCode.SetPropPopN:
                    {
                        var name = chunk.Names[code[ip++]];
                        var value = Pop();
                        var obj = Pop();
                        SetMember(obj, name, value, chunk.IsStrict);
                        break;
                    }
                    case OpCode.GetVarGetProp:
                    {
                        var site = ip;
                        var varIdx  = ReadOperand(code, ref ip);
                        var propIdx = ReadOperand(code, ref ip);

                        ScriptVar obj;
                        {
                            var varLink = ResolveCached(cache, chunk, site, env, varIdx);
                            if (varLink != null)
                                obj = varLink.Var;
                            else if (chunk.Names[varIdx] == "globalThis")
                                obj = env.Global().Vars;
                            else
                                obj = SharedUndefined;
                        }

                        var propName = chunk.Names[propIdx];
                        var cacheSlot = (int)((uint)propIdx * 2654435761u >> 24);
                        ref var ce = ref _propCache[cacheSlot];
                        if (ReferenceEquals(ce.Name, propName))
                        {
                            if (ce.ShapeId > 0)
                            {
                                var shapedObj = obj as ShapedScriptVar;
                                var shp = shapedObj?._shape;
                                if (shp != null && ce.ShapeId == shp.Id)
                                {
                                    var sl = shapedObj._shapeRoot;
                                    for (int _i = 0; _i < ce.SlotIndex; _i++) sl = sl?.Next;
                                    if (sl != null)
                                    {
                                        Push(sl.Getter != null
                                            ? InvokeCallable(sl.Getter, obj, System.Array.Empty<ScriptVar>())
                                            : sl.Var);
                                        break;
                                    }
                                }
                            }
                            else if (ReferenceEquals(ce.Object, obj) &&
                                     ce.ShapeVersion == obj.ShapeVersion &&
                                     ce.Link != null)
                            {
                                Push(ce.Link.Getter != null
                                    ? InvokeCallable(ce.Link.Getter, obj, System.Array.Empty<ScriptVar>())
                                    : ce.Link.Var);
                                break;
                            }
                        }

                        if (obj.IsProxy) { Push(GetMember(obj, propName)); break; }

                        {
                            var shapedObj = obj as ShapedScriptVar;
                            var shp = shapedObj?._shape;
                            if (shp != null && shp.Slots.TryGetValue(propName, out var slotIdx))
                            {
                                var sl = shapedObj._shapeRoot;
                                for (int _i = 0; _i < slotIdx; _i++) sl = sl?.Next;
                                if (sl != null)
                                {
                                    var propResult = sl.Getter != null
                                        ? InvokeCallable(sl.Getter, obj, System.Array.Empty<ScriptVar>())
                                        : sl.Var;
                                    ce.Name = propName; ce.ShapeId = shp.Id; ce.SlotIndex = slotIdx;
                                    ce.Object = null; ce.Link = null;
                                    Push(propResult);
                                    break;
                                }
                            }
                        }

                        var propLink = obj.FindChild(propName);
                        if (propLink == null && engine != null) propLink = engine.FindInParentClasses(obj, propName);

                        ScriptVar propResult2;
                        if (propLink != null)
                        {
                            if (propLink.Getter != null)
                                propResult2 = InvokeCallable(propLink.Getter, obj, System.Array.Empty<ScriptVar>());
                            else
                                propResult2 = propLink.Var;
                            if (obj.IsObject || obj.IsArray)
                            {
                                ce.Name = propName; ce.ShapeId = 0;
                                ce.Object = obj; ce.ShapeVersion = obj.ShapeVersion; ce.Link = propLink;
                            }
                        }
                        else
                        {
                            if (obj.IsArray && propName == "length")         propResult2 = ScriptVar.FromInt(obj.GetArrayLength());
                            else if (obj.IsString && propName == "length")   propResult2 = ScriptVar.FromInt(obj.String.Length);
                            else if (propName == "size" && obj.GetData() is INativeContainer ctnr) propResult2 = ScriptVar.FromInt(ctnr.GetSize());
                            else propResult2 = ScriptVar.CreateUndefined();
                        }
                        Push(propResult2);
                        break;
                    }
                    case OpCode.GetVarGetPropN:
                    {
                        var site = ip;
                        var varIdx  = code[ip++];
                        var propIdx = code[ip++];

                        ScriptVar obj;
                        {
                            var varLink = ResolveCached(cache, chunk, site, env, varIdx);
                            if (varLink != null)
                                obj = varLink.Var;
                            else if (chunk.Names[varIdx] == "globalThis")
                                obj = env.Global().Vars;
                            else
                                obj = SharedUndefined;
                        }

                        var propName = chunk.Names[propIdx];
                        var cacheSlot = (int)((uint)propIdx * 2654435761u >> 24);
                        ref var ce = ref _propCache[cacheSlot];
                        if (ReferenceEquals(ce.Name, propName))
                        {
                            if (ce.ShapeId > 0)
                            {
                                var shapedObj = obj as ShapedScriptVar;
                                var shp = shapedObj?._shape;
                                if (shp != null && ce.ShapeId == shp.Id)
                                {
                                    var sl = shapedObj._shapeRoot;
                                    for (int _i = 0; _i < ce.SlotIndex; _i++) sl = sl?.Next;
                                    if (sl != null)
                                    {
                                        Push(sl.Getter != null
                                            ? InvokeCallable(sl.Getter, obj, System.Array.Empty<ScriptVar>())
                                            : sl.Var);
                                        break;
                                    }
                                }
                            }
                            else if (ReferenceEquals(ce.Object, obj) &&
                                     ce.ShapeVersion == obj.ShapeVersion &&
                                     ce.Link != null)
                            {
                                Push(ce.Link.Getter != null
                                    ? InvokeCallable(ce.Link.Getter, obj, System.Array.Empty<ScriptVar>())
                                    : ce.Link.Var);
                                break;
                            }
                        }

                        if (obj.IsProxy) { Push(GetMember(obj, propName)); break; }

                        {
                            var shapedObj = obj as ShapedScriptVar;
                            var shp = shapedObj?._shape;
                            if (shp != null && shp.Slots.TryGetValue(propName, out var slotIdx))
                            {
                                var sl = shapedObj._shapeRoot;
                                for (int _i = 0; _i < slotIdx; _i++) sl = sl?.Next;
                                if (sl != null)
                                {
                                    var propResult = sl.Getter != null
                                        ? InvokeCallable(sl.Getter, obj, System.Array.Empty<ScriptVar>())
                                        : sl.Var;
                                    ce.Name = propName; ce.ShapeId = shp.Id; ce.SlotIndex = slotIdx;
                                    ce.Object = null; ce.Link = null;
                                    Push(propResult);
                                    break;
                                }
                            }
                        }

                        var propLink = obj.FindChild(propName);
                        if (propLink == null && engine != null) propLink = engine.FindInParentClasses(obj, propName);

                        ScriptVar propResult2;
                        if (propLink != null)
                        {
                            if (propLink.Getter != null)
                                propResult2 = InvokeCallable(propLink.Getter, obj, System.Array.Empty<ScriptVar>());
                            else
                                propResult2 = propLink.Var;
                            if (obj.IsObject || obj.IsArray)
                            {
                                ce.Name = propName; ce.ShapeId = 0;
                                ce.Object = obj; ce.ShapeVersion = obj.ShapeVersion; ce.Link = propLink;
                            }
                        }
                        else
                        {
                            if (obj.IsArray && propName == "length")         propResult2 = ScriptVar.FromInt(obj.GetArrayLength());
                            else if (obj.IsString && propName == "length")   propResult2 = ScriptVar.FromInt(obj.String.Length);
                            else if (propName == "size" && obj.GetData() is INativeContainer ctnr2) propResult2 = ScriptVar.FromInt(ctnr2.GetSize());
                            else propResult2 = ScriptVar.CreateUndefined();
                        }
                        Push(propResult2);
                        break;
                    }
                    case OpCode.GetVarGetVarBinary:
                    {
                        var site = ip;
                        var operatorCode = (ScriptLex.LexTypes)ReadOperand(code, ref ip);
                        var var1Site = ip;
                        var var1Idx  = ReadOperand(code, ref ip);
                        var var2Site = ip;
                        var var2Idx  = ReadOperand(code, ref ip);
                        var link1 = ResolveCached(cache, chunk, var1Site, env, var1Idx);
                        var link2 = ResolveCached(cache, chunk, var2Site, env, var2Idx);
                        var a = link1 != null ? link1.Var : SharedUndefined;
                        var b = link2 != null ? link2.Var : SharedUndefined;
                        RecordBinaryOpTypes(binProf, site, a, b);
                        if (a.IsAnyInt && b.IsAnyInt && IntBinary(a.Long, b.Long, operatorCode, out var fast))
                            Push(fast);
                        else
                            Push(a.MathsOp(b, operatorCode));
                        break;
                    }
                    case OpCode.GetVarGetVarBinaryN:
                    {
                        var site = ip;
                        var operatorCode = (ScriptLex.LexTypes)code[ip++];
                        var var1Site = ip;
                        var var1Idx  = code[ip++];
                        var var2Site = ip;
                        var var2Idx  = code[ip++];
                        var link1 = ResolveCached(cache, chunk, var1Site, env, var1Idx);
                        var link2 = ResolveCached(cache, chunk, var2Site, env, var2Idx);
                        var a = link1 != null ? link1.Var : SharedUndefined;
                        var b = link2 != null ? link2.Var : SharedUndefined;
                        RecordBinaryOpTypes(binProf, site, a, b);
                        if (a.IsAnyInt && b.IsAnyInt && IntBinary(a.Long, b.Long, operatorCode, out var fast))
                            Push(fast);
                        else
                            Push(a.MathsOp(b, operatorCode));
                        break;
                    }

                    case OpCode.GetPropMethod:
                    {
                        var nameIdx = ReadOperand(code, ref ip);
                        var name = chunk.Names[nameIdx];
                        var obj = Peek(); // keep receiver on stack for CallMethod

                        var cacheSlot = (int)((uint)nameIdx * 2654435761u >> 24);
                        ref var ce = ref _propCache[cacheSlot];
                        if (ReferenceEquals(ce.Object, obj) &&
                            ce.ShapeVersion == obj.ShapeVersion &&
                            ReferenceEquals(ce.Name, name) &&
                            ce.Link != null)
                        {
                            Push(ce.Link.Getter != null
                                ? InvokeCallable(ce.Link.Getter, obj, System.Array.Empty<ScriptVar>())
                                : ce.Link.Var);
                            break;
                        }

                        if (obj.IsProxy) { Push(GetMember(obj, name)); break; }

                        var link = obj.FindChild(name);
                        if (link == null && engine != null) link = engine.FindInParentClasses(obj, name);

                        ScriptVar methodResult;
                        if (link != null)
                        {
                            if (link.Getter != null)
                                methodResult = InvokeCallable(link.Getter, obj, System.Array.Empty<ScriptVar>());
                            else
                                methodResult = link.Var;
                            if (obj.IsObject || obj.IsArray)
                            {
                                ce.Object = obj; ce.ShapeVersion = obj.ShapeVersion;
                                ce.Name = name; ce.Link = link;
                            }
                        }
                        else
                        {
                            methodResult = ScriptVar.CreateUndefined();
                        }
                        Push(methodResult);
                        break;
                    }
                    case OpCode.GetPropCall0:
                    {
                        var nameIdx = ReadOperand(code, ref ip);
                        var name = chunk.Names[nameIdx];
                        var obj = Pop();

                        var cacheSlot = (int)((uint)nameIdx * 2654435761u >> 24);
                        ref var ce = ref _propCache[cacheSlot];
                        ScriptVar callTarget;
                        if (ReferenceEquals(ce.Object, obj) &&
                            ce.ShapeVersion == obj.ShapeVersion &&
                            ReferenceEquals(ce.Name, name) &&
                            ce.Link != null)
                        {
                            callTarget = ce.Link.Getter != null
                                ? InvokeCallable(ce.Link.Getter, obj, System.Array.Empty<ScriptVar>())
                                : ce.Link.Var;
                        }
                        else
                        {
                            if (obj.IsProxy) { Push(InvokeCallable(GetMember(obj, name), obj, System.Array.Empty<ScriptVar>())); break; }
                            var link = obj.FindChild(name);
                            if (link == null && engine != null) link = engine.FindInParentClasses(obj, name);
                            if (link != null)
                            {
                                if (link.Getter != null)
                                    callTarget = InvokeCallable(link.Getter, obj, System.Array.Empty<ScriptVar>());
                                else
                                    callTarget = link.Var;
                                if (obj.IsObject || obj.IsArray)
                                {
                                    ce.Object = obj; ce.ShapeVersion = obj.ShapeVersion;
                                    ce.Name = name; ce.Link = link;
                                }
                            }
                            else
                            {
                                callTarget = ScriptVar.CreateUndefined();
                            }
                        }
                        Push(InvokeCallable(callTarget, obj, System.Array.Empty<ScriptVar>()));
                        break;
                    }
                    case OpCode.GetPropMethodN:
                    {
                        var nameIdx = code[ip++];
                        var name = chunk.Names[nameIdx];
                        var obj = Peek();

                        var cacheSlot = (int)((uint)nameIdx * 2654435761u >> 24);
                        ref var ce = ref _propCache[cacheSlot];
                        if (ReferenceEquals(ce.Object, obj) &&
                            ce.ShapeVersion == obj.ShapeVersion &&
                            ReferenceEquals(ce.Name, name) &&
                            ce.Link != null)
                        {
                            Push(ce.Link.Getter != null
                                ? InvokeCallable(ce.Link.Getter, obj, System.Array.Empty<ScriptVar>())
                                : ce.Link.Var);
                            break;
                        }

                        if (obj.IsProxy) { Push(GetMember(obj, name)); break; }

                        var link = obj.FindChild(name);
                        if (link == null && engine != null) link = engine.FindInParentClasses(obj, name);

                        ScriptVar methodResult;
                        if (link != null)
                        {
                            if (link.Getter != null)
                                methodResult = InvokeCallable(link.Getter, obj, System.Array.Empty<ScriptVar>());
                            else
                                methodResult = link.Var;
                            if (obj.IsObject || obj.IsArray)
                            {
                                ce.Object = obj; ce.ShapeVersion = obj.ShapeVersion;
                                ce.Name = name; ce.Link = link;
                            }
                        }
                        else
                        {
                            methodResult = ScriptVar.CreateUndefined();
                        }
                        Push(methodResult);
                        break;
                    }
                    case OpCode.GetPropCall0N:
                    {
                        var nameIdx = code[ip++];
                        var name = chunk.Names[nameIdx];
                        var obj = Pop();

                        var cacheSlot = (int)((uint)nameIdx * 2654435761u >> 24);
                        ref var ce = ref _propCache[cacheSlot];
                        ScriptVar callTarget;
                        if (ReferenceEquals(ce.Object, obj) &&
                            ce.ShapeVersion == obj.ShapeVersion &&
                            ReferenceEquals(ce.Name, name) &&
                            ce.Link != null)
                        {
                            callTarget = ce.Link.Getter != null
                                ? InvokeCallable(ce.Link.Getter, obj, System.Array.Empty<ScriptVar>())
                                : ce.Link.Var;
                        }
                        else
                        {
                            if (obj.IsProxy) { Push(InvokeCallable(GetMember(obj, name), obj, System.Array.Empty<ScriptVar>())); break; }
                            var link = obj.FindChild(name);
                            if (link == null && engine != null) link = engine.FindInParentClasses(obj, name);
                            if (link != null)
                            {
                                if (link.Getter != null)
                                    callTarget = InvokeCallable(link.Getter, obj, System.Array.Empty<ScriptVar>());
                                else
                                    callTarget = link.Var;
                                if (obj.IsObject || obj.IsArray)
                                {
                                    ce.Object = obj; ce.ShapeVersion = obj.ShapeVersion;
                                    ce.Name = name; ce.Link = link;
                                }
                            }
                            else
                            {
                                callTarget = ScriptVar.CreateUndefined();
                            }
                        }
                        Push(InvokeCallable(callTarget, obj, System.Array.Empty<ScriptVar>()));
                        break;
                    }

                    default:
                        throw new ScriptException($"VM opcode not yet implemented: {op}");
                }
                } // end inner try
                catch (JITException ex)
                {
                    if (!DispatchException(ex, ref ip, env, chunk, savedTryDepth))
                    {
                        var (jitLine, jitCol) = chunk.GetLineAndColForOffset(instrIp);
                        ex.PushFrame(chunk.Name, jitLine, jitCol);
                        throw;
                    }
                    // handled: continue dispatch loop at new ip
                }
                catch (ScriptException ex)
                {
                    // Each Execute() frame adds itself to the script stack trace as
                    // the exception climbs through the call chain.
                    var (seLine, seCol) = chunk.GetLineAndColForOffset(instrIp);
                    ex.PushFrame(chunk.Name, seLine, seCol);
                    throw;
                }
            }

            // Normal completion (loop exhausted or Return/Halt): mark generator done.
            if (gsState != null && !generatorYielded)
                gsState.Done = true;

            return null;

            } // end outer try
            finally
            {
                // When a stackless generator yields, this Execute() call will be resumed later.
                // Leave all state (tryStack depth, callFrame) intact so the next call can continue.
                if (!generatorYielded)
                {
                    // Normal completion or exception: clean up handler frames and debugger frame.
                    while (tryStack.Count > savedTryDepth) tryStack.Pop();
                    hasPendingReturn = false;
                    if (callFrame != null) _callFrames.RemoveAt(_callFrames.Count - 1);
                }
            }
        }

        // Walk the handler stack looking for a catch or finally that covers the
        // exception. Returns true and updates `ip` when a handler is found;
        // returns false when the exception must propagate to the caller.
        // `floorDepth` is the savedTryDepth of the current Execute() invocation —
        // we must not pop frames that belong to an outer invocation.
        private bool DispatchException(JITException ex, ref int ip, Environment env, Chunk chunk, int floorDepth)
        {
            while (tryStack.Count > floorDepth)
            {
                var frame = tryStack.Pop();
                sp = frame.StackDepth; // unwind the value stack

                if (frame.CatchPC >= 0)
                {
                    // Bind the exception variable into the current environment.
                    if (frame.CatchVarIdx >= 0)
                    {
                        var name = chunk.Names[frame.CatchVarIdx];
                        var link = env.Vars.FindChildOrCreate(name);
                        link.ReplaceWith(ex.VarObj ?? ScriptVar.CreateUndefined());
                        env.Version++;
                    }
                    // Push a catch-protecting frame so that any exception thrown
                    // inside the catch body still triggers the finally block.
                    tryStack.Push(new TryFrame
                    {
                        CatchPC     = -1,
                        FinallyPC   = frame.FinallyPC,
                        CatchVarIdx = -1,
                        StackDepth  = sp,
                    });
                    pendingException = null;
                    ip = frame.CatchPC;
                    return true;
                }

                if (frame.FinallyPC >= 0)
                {
                    // No catch, but there is a finally. Record the exception as
                    // pending; LeaveFinally will re-throw it when the body ends.
                    pendingException = ex;
                    ip = frame.FinallyPC;
                    return true;
                }

                // Frame has neither catch nor finally (shouldn't happen in well-formed
                // bytecode, but continue unwinding rather than silently eating the exception).
            }
            return false; // no handler found in this Execute() frame
        }

        private ScriptVar[] PopArgs(int count)
        {
            var args = new ScriptVar[count];
            for (var i = count - 1; i >= 0; i--)
            {
                args[i] = Pop();
            }
            return args;
        }

        /// <summary>
        /// Invoke a function value with the given receiver (<c>this</c>) and
        /// arguments. Handles both native callbacks (preserving the
        /// <see cref="ScriptEngine.ScriptCallbackCB"/> ABI) and compiled
        /// functions (run on this VM with a fresh environment whose lexical
        /// parent is the function's captured environment).
        /// </summary>
        // Build a JS-catchable TypeError value (a plain { name, message, stack } object,
        // matching the Error builtins) so calling a non-function throws an error that
        // script-level try/catch can handle, as it does in a real engine.
        private static JITException TypeError(string message)
        {
            var obj = ScriptVar.CreateObject();
            obj.AddChild("name", ScriptVar.FromString("TypeError"));
            obj.AddChild("message", ScriptVar.FromString(message));
            obj.AddChild("stack", ScriptVar.FromString("TypeError: " + message));
            return new JITException(obj);
        }

        public ScriptVar InvokeCallable(ScriptVar callee, ScriptVar thisArg, ScriptVar[] args)
        {
            if (callee == null || (!callee.IsFunction && !callee.IsProxy))
            {
                throw TypeError("Value is not a function");
            }

            // Proxy [[Apply]] trap
            if (callee.IsProxy)
            {
                var handler = callee.ProxyHandler;
                var applyTrap = handler?.FindChild("apply")?.Var;
                if (applyTrap != null && applyTrap.IsFunction)
                {
                    var argsArr = ArgsToArray(args);
                    return InvokeCallable(applyTrap, handler, new[] { callee.ProxyTarget, thisArg ?? ScriptVar.CreateUndefined(), argsArr });
                }
                callee = callee.ProxyTarget ?? callee;
                if (callee == null || !callee.IsFunction)
                    throw TypeError("Value is not a function");
            }

            if (callee.IsNative)
            {
                var scope = BorrowNativeScope();
                if (thisArg != null) scope.AddChildNoDup("this", thisArg);

                var p = callee.FirstChild;
                var i = 0;
                while (p != null)
                {
                    // A "..." prefix marks a rest parameter: gather the remaining
                    // arguments into an array bound under the (un-prefixed) name.
                    if (p.Name.Length > 3 && p.Name[0] == '.' && p.Name[1] == '.' && p.Name[2] == '.')
                    {
                        var restArr = ScriptVar.CreateArray();
                        var ri = 0;
                        for (; i < args.Length; i++)
                            restArr.SetArrayIndex(ri++, BindArg(args, i));
                        scope.AddChild(p.Name.Substring(3), restArr);
                        p = p.Next;
                        continue;
                    }
                    scope.AddChild(p.Name, BindArg(args, i));
                    i++;
                    p = p.Next;
                }
                // Bind extra args (beyond declared params) by numeric index so
                // varargs native functions can access them via GetParameter("1") etc.
                for (; i < args.Length; i++)
                    scope.AddChild(i.ToString(), BindArg(args, i));

                var returnLink = scope.AddChild(ScriptVar.ReturnVarName, null);
                var nativeName = callee.FindChild("name")?.Var?.String;
                _profiler?.Enter(string.IsNullOrEmpty(nativeName) ? "(native)" : nativeName, "(native)", 0, 0);
                try { callee.GetCallback()?.Invoke(scope, callee.GetCallbackUserData()); }
                finally { _profiler?.Leave(); }
                var nativeResult = returnLink.Var;
                ReturnNativeScope(scope);
                return nativeResult;
            }

            var vmfn = (VmFunction)callee.GetData();

            // Async generator function: return an async iterator object
            if (vmfn.Body.IsAsync && vmfn.Body.IsGenerator)
            {
                var asyncGenCallEnv = BuildCallEnvironment(vmfn, thisArg, args);
                return CreateAsyncGeneratorIterator(vmfn, asyncGenCallEnv);
            }

            // Generator function: return an iterator object instead of executing the body
            if (vmfn.Body.IsGenerator)
            {
                var genCallEnv = BuildCallEnvironment(vmfn, thisArg, args);
                return CreateGeneratorIterator(vmfn, genCallEnv);
            }

            // Async function: return a Promise that resolves with the function's return value
            if (vmfn.Body.IsAsync)
            {
                var asyncCallEnv = BuildCallEnvironment(vmfn, thisArg, args);
                return CreateAsyncPromise(vmfn, asyncCallEnv);
            }

            var recyclable = vmfn.Body.RecyclableFrame;
            var vars = recyclable ? BorrowFrameVars() : ScriptVar.CreateObject();
            var callEnv = new Environment(vars, vmfn.Captured);
            InitSlotFrame(vmfn.Body, callEnv);
            if (thisArg != null)
                vars.AddChildNoDup("this", thisArg);
            else if (vmfn.Body.IsStrict)
                vars.AddChildNoDup("this", SharedUndefined);

            var parameters = vmfn.Body.Parameters;
            var restIdx2 = vmfn.Body.RestParamIndex;
            var paramLimit2 = restIdx2 >= 0 ? restIdx2 : parameters.Count;
            var slots2 = callEnv.Slots;
            for (var j = 0; j < paramLimit2; j++)
            {
                var pv = BindArg(args, j);
                vars.AddChild(parameters[j], pv);
                if (slots2 != null) slots2[j] = pv; // param slot (Lever A); unread when the param is name-based
            }

            // Handle rest parameter
            if (restIdx2 >= 0)
            {
                var restArr = ScriptVar.CreateArray();
                var restLen = 0;
                for (var j = restIdx2; j < (args?.Length ?? 0); j++)
                {
                    restArr.SetArrayIndex(restLen++, BindArg(args, j));
                }
                vars.AddChild(parameters[restIdx2], restArr);
            }

            // Bind arguments object for non-arrow functions
            if (!vmfn.Body.IsArrow && vmfn.Body.UsesArguments)
            {
                var argObj = ScriptVar.CreateArray();
                for (var j = 0; j < (args?.Length ?? 0); j++)
                    argObj.SetArrayIndex(j, BindArg(args, j));
                if (vmfn.Body.IsStrict) AddStrictArgumentsPoisonPills(argObj);
                vars.AddChild("arguments", argObj);
            }

            if (++_callDepth > _maxCallDepth) { _callDepth--; throw new ScriptException("Maximum call stack size exceeded"); }
            _profiler?.Enter(vmfn.Body.Name, vmfn.Body.Name, 0, 0);
            if (recyclable)
            {
                try { return Execute(vmfn.Body, callEnv) ?? ScriptVar.CreateUndefined(); }
                finally
                {
                    ReturnFrameVars(vars);
                    _profiler?.Leave();
                    _callDepth--;
                }
            }

            try { return Execute(vmfn.Body, callEnv) ?? ScriptVar.CreateUndefined(); }
            finally { _profiler?.Leave(); _callDepth--; }
        }

        // Allocation-free fast paths for 1/2/3-argument callbacks (map/filter/reduce/
        // sort comparator). For VM functions: push args directly onto the operand stack
        // and call InvokeVmFunctionFromStack — zero ScriptVar[] allocation. For native
        // and proxy callables: fall back to InvokeCallable with a fixed-size array.
        internal ScriptVar InvokeCallable1(ScriptVar callee, ScriptVar thisArg, ScriptVar arg1)
        {
            if (callee != null && callee.IsFunction && !callee.IsNative)
            {
                Push(arg1);
                return InvokeVmFunctionFromStack(callee, thisArg, 1) ?? SharedUndefined;
            }
            return InvokeCallable(callee, thisArg, [arg1]) ?? SharedUndefined;
        }

        internal ScriptVar InvokeCallable2(ScriptVar callee, ScriptVar thisArg, ScriptVar arg1, ScriptVar arg2)
        {
            if (callee != null && callee.IsFunction && !callee.IsNative)
            {
                Push(arg1);
                Push(arg2);
                return InvokeVmFunctionFromStack(callee, thisArg, 2) ?? SharedUndefined;
            }
            return InvokeCallable(callee, thisArg, [arg1, arg2]) ?? SharedUndefined;
        }

        internal ScriptVar InvokeCallable3(ScriptVar callee, ScriptVar thisArg, ScriptVar arg1, ScriptVar arg2, ScriptVar arg3)
        {
            if (callee != null && callee.IsFunction && !callee.IsNative)
            {
                Push(arg1);
                Push(arg2);
                Push(arg3);
                return InvokeVmFunctionFromStack(callee, thisArg, 3) ?? SharedUndefined;
            }
            return InvokeCallable(callee, thisArg, [arg1, arg2, arg3]) ?? SharedUndefined;
        }

        // Invoke a compiled (non-native) function whose arguments are sitting on
        // the operand stack at [sp-argc .. sp-1]. Binds them straight into the new
        // call frame and pops them, avoiding the per-call ScriptVar[] that the
        // general InvokeCallable path needs. The caller is responsible for popping
        // the callee (and receiver) that remain below the args. Mirrors the
        // compiled-function branch of InvokeCallable exactly.
        private ScriptVar InvokeVmFunctionFromStack(ScriptVar callee, ScriptVar thisArg, int argc)
        {
            var argBase = sp - argc;

            var vmfn = (VmFunction)callee.GetData();

            // Async generator function: materialise args, return async iterator
            if (vmfn.Body.IsAsync && vmfn.Body.IsGenerator)
            {
                var asyncGenArgs = new ScriptVar[argc];
                for (var j = 0; j < argc; j++)
                    asyncGenArgs[j] = stack[argBase + j].ToScriptVar();
                sp = argBase;
                var asyncGenCallEnv = BuildCallEnvironment(vmfn, thisArg, asyncGenArgs);
                return CreateAsyncGeneratorIterator(vmfn, asyncGenCallEnv);
            }

            // Generator function: materialise args from stack, then return iterator
            if (vmfn.Body.IsGenerator)
            {
                var genArgs = new ScriptVar[argc];
                for (var j = 0; j < argc; j++)
                    genArgs[j] = stack[argBase + j].ToScriptVar();
                sp = argBase;
                var genCallEnv = BuildCallEnvironment(vmfn, thisArg, genArgs);
                return CreateGeneratorIterator(vmfn, genCallEnv);
            }

            // Async function: materialise args from stack, then return a Promise
            if (vmfn.Body.IsAsync)
            {
                var asyncArgs = new ScriptVar[argc];
                for (var j = 0; j < argc; j++)
                    asyncArgs[j] = stack[argBase + j].ToScriptVar();
                sp = argBase;
                var asyncCallEnv = BuildCallEnvironment(vmfn, thisArg, asyncArgs);
                return CreateAsyncPromise(vmfn, asyncCallEnv);
            }

            var recyclable = vmfn.Body.RecyclableFrame;
            var vars = recyclable ? BorrowFrameVars() : ScriptVar.CreateObject();
            var callEnv = new Environment(vars, vmfn.Captured);
            InitSlotFrame(vmfn.Body, callEnv);
            if (thisArg != null)
                vars.AddChildNoDup("this", thisArg);
            else if (vmfn.Body.IsStrict)
                vars.AddChildNoDup("this", SharedUndefined);

            var parameters = vmfn.Body.Parameters;
            var restIdx = vmfn.Body.RestParamIndex;
            var paramLimit = restIdx >= 0 ? restIdx : parameters.Count;
            var slots = callEnv.Slots;
            for (var j = 0; j < paramLimit; j++)
            {
                var arg = j < argc ? stack[argBase + j].ToScriptVar() : null;
                var pv = BindArgValue(arg);
                vars.AddChild(parameters[j], pv);
                if (slots != null) slots[j] = pv; // param slot (Lever A); unread when the param is name-based
            }

            // Handle rest parameter
            if (restIdx >= 0)
            {
                var restArr = ScriptVar.CreateArray();
                var restLen = 0;
                for (var j = restIdx; j < argc; j++)
                {
                    restArr.SetArrayIndex(restLen++, BindArgValue(stack[argBase + j].ToScriptVar()));
                }
                vars.AddChild(parameters[restIdx], restArr);
            }

            // Bind arguments object for non-arrow functions
            if (!vmfn.Body.IsArrow && vmfn.Body.UsesArguments)
            {
                var argObj = ScriptVar.CreateArray();
                for (var j = 0; j < argc; j++)
                    argObj.SetArrayIndex(j, BindArgValue(stack[argBase + j].ToScriptVar()));
                if (vmfn.Body.IsStrict) AddStrictArgumentsPoisonPills(argObj);
                vars.AddChild("arguments", argObj);
            }

            // Pop the arguments now that they are bound; the args' slots are free
            // for the callee's own use of the shared operand stack. Their values
            // stay alive via the call frame's child links.
            sp = argBase;

            if (++_callDepth > _maxCallDepth) { _callDepth--; throw new ScriptException("Maximum call stack size exceeded"); }
            _profiler?.Enter(vmfn.Body.Name, vmfn.Body.Name, 0, 0);
            try
            {
                var result = Execute(vmfn.Body, callEnv);
                // Trampoline: if a TailCall/TailCallMethod handler signalled a pending
                // call, re-execute the callee here instead of on a new C# stack frame.
                while (_pendingTailCallFn != null)
                {
                    var nextFn   = _pendingTailCallFn;
                    var nextArgs = _pendingTailCallArgs;
                    var nextThis = _pendingTailCallThis;
                    _pendingTailCallFn   = null;
                    _pendingTailCallArgs = null;
                    _pendingTailCallThis = null;

                    if (recyclable) ReturnFrameVars(vars);
                    _profiler?.Leave();

                    vmfn       = nextFn;
                    recyclable = vmfn.Body.RecyclableFrame;
                    vars       = recyclable ? BorrowFrameVars() : ScriptVar.CreateObject();
                    callEnv    = new Environment(vars, vmfn.Captured);
                    InitSlotFrame(vmfn.Body, callEnv);
                    BindArgsArrayToVars(vmfn, vars, callEnv, nextArgs, nextThis);

                    _profiler?.Enter(vmfn.Body.Name, vmfn.Body.Name, 0, 0);
                    result = Execute(vmfn.Body, callEnv);
                }
                return result ?? SharedUndefined;
            }
            finally
            {
                if (recyclable) ReturnFrameVars(vars);
                _profiler?.Leave();
                _callDepth--;
            }
        }

        // Bind a ScriptVar[] argument array into an already-allocated vars object.
        // Used by the InvokeVmFunctionFromStack trampoline for tail-call iterations.
        private static void BindArgsArrayToVars(VmFunction vmfn, ScriptVar vars, Environment env, ScriptVar[] args, ScriptVar thisArg)
        {
            if (thisArg != null)
                vars.AddChildNoDup("this", thisArg);
            else if (vmfn.Body.IsStrict)
                vars.AddChildNoDup("this", SharedUndefined);

            var parameters = vmfn.Body.Parameters;
            var restIdx    = vmfn.Body.RestParamIndex;
            var argc       = args.Length;
            var paramLimit = restIdx >= 0 ? restIdx : parameters.Count;
            var slots      = env.Slots;
            for (var j = 0; j < paramLimit; j++)
            {
                var arg = j < argc ? args[j] : null;
                var pv = BindArgValue(arg);
                vars.AddChild(parameters[j], pv);
                if (slots != null) slots[j] = pv; // param slot (Lever A); unread when the param is name-based
            }
            if (restIdx >= 0)
            {
                var restArr = ScriptVar.CreateArray();
                var restLen = 0;
                for (var j = restIdx; j < argc; j++)
                    restArr.SetArrayIndex(restLen++, BindArgValue(args[j]));
                vars.AddChild(parameters[restIdx], restArr);
            }
            if (!vmfn.Body.IsArrow && vmfn.Body.UsesArguments)
            {
                var argObj = ScriptVar.CreateArray();
                for (var j = 0; j < argc; j++)
                    argObj.SetArrayIndex(j, BindArgValue(args[j]));
                if (vmfn.Body.IsStrict) AddStrictArgumentsPoisonPills(argObj);
                vars.AddChild("arguments", argObj);
            }
        }

        // Build a call environment for a VmFunction from a ScriptVar[] args array.
        // Used by the generator path to capture args before starting the body thread.
        private static Environment BuildCallEnvironment(VmFunction vmfn, ScriptVar thisArg, ScriptVar[] args)
        {
            var vars = ScriptVar.CreateObject(); // generators never recycle frames
            var env = new Environment(vars, vmfn.Captured);
            if (thisArg != null) vars.AddChildNoDup("this", thisArg);

            var parameters = vmfn.Body.Parameters;
            var restIdx = vmfn.Body.RestParamIndex;
            var paramLimit = restIdx >= 0 ? restIdx : parameters.Count;
            for (var j = 0; j < paramLimit; j++)
                vars.AddChild(parameters[j], BindArg(args, j));

            if (restIdx >= 0)
            {
                var restArr = ScriptVar.CreateArray();
                var restLen = 0;
                for (var j = restIdx; j < (args?.Length ?? 0); j++)
                    restArr.SetArrayIndex(restLen++, BindArg(args, j));
                vars.AddChild(parameters[restIdx], restArr);
            }

            // Bind arguments object for non-arrow functions
            if (!vmfn.Body.IsArrow && vmfn.Body.UsesArguments)
            {
                var argObj = ScriptVar.CreateArray();
                for (var j = 0; j < (args?.Length ?? 0); j++)
                    argObj.SetArrayIndex(j, BindArg(args, j));
                vars.AddChild("arguments", argObj);
            }

            return env;
        }

        // Create an iterator object for a generator function. Routes to the stackless
        // path for simple generators (no try/catch), falling back to the thread-based
        // GeneratorObject path for generators that use try/catch or await.
        private ScriptVar CreateGeneratorIterator(VmFunction vmfn, Environment callEnv)
        {
            if (vmfn.Body.IsSimpleGenerator())
                return CreateStacklessGeneratorIterator(vmfn, callEnv);

            var genObj = new GeneratorObject();

            var iterObj = ScriptVar.CreateObject();

            var nextFn = ScriptVar.CreateNativeFunction();
            // Declare a "value" parameter so callers can pass a resume value via .next(v)
            nextFn.AddChild("value", ScriptVar.CreateUndefined());
            nextFn.SetCallback((scope, _) =>
            {
                var inputLink = scope.FindChild("value");
                var input = inputLink?.Var ?? ScriptVar.CreateUndefined();

                var (value, done) = genObj.Next(input, g =>
                {
                    var result = Execute(vmfn.Body, callEnv, g);
                    g.Complete(result ?? ScriptVar.CreateUndefined());
                });

                var resultObj = ScriptVar.CreateObject();
                resultObj.AddChild("value", value);
                resultObj.AddChild("done", ScriptVar.FromBool(done));
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(resultObj);
            }, null);

            iterObj.AddChild("next", nextFn);
            return iterObj;
        }

        // Stackless generator: runs the body synchronously on THIS vm (no OS thread).
        // Each .next() call resumes Execute() from the saved instruction pointer;
        // Execute() returns when it hits Yield (via the generatorYielded break) or
        // when the body completes normally.
        private ScriptVar CreateStacklessGeneratorIterator(VmFunction vmfn, Environment callEnv)
        {
            var gs = new GeneratorState();
            var genVm = new VirtualMachine(engine);

            var iterObj = ScriptVar.CreateObject();
            var nextFn = ScriptVar.CreateNativeFunction();
            nextFn.AddChild("value", ScriptVar.CreateUndefined());
            nextFn.SetCallback((scope, _) =>
            {
                var inputLink = scope.FindChild("value");
                var input = inputLink?.Var ?? ScriptVar.CreateUndefined();

                var resultObj = ScriptVar.CreateObject();
                if (gs.Done)
                {
                    resultObj.AddChild("value", ScriptVar.CreateUndefined());
                    resultObj.AddChild("done", ScriptVar.FromBool(true));
                }
                else
                {
                    if (gs.Started)
                        gs.ResumeValue = input; // pushed by Execute at resume start
                    gs.Started = true;
                    genVm.Execute(vmfn.Body, callEnv, null, gs);
                    if (!gs.Done)
                    {
                        resultObj.AddChild("value", gs.YieldedValue ?? ScriptVar.CreateUndefined());
                        resultObj.AddChild("done", ScriptVar.FromBool(false));
                    }
                    else
                    {
                        resultObj.AddChild("value", ScriptVar.CreateUndefined());
                        resultObj.AddChild("done", ScriptVar.FromBool(true));
                    }
                }
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(resultObj);
            }, null);
            iterObj.AddChild("next", nextFn);
            return iterObj;
        }

        // Create a Promise that runs an async function body using the generator
        // thread. The async function body compiles `await expr` as `yield expr`,
        // so we drive it like a generator: each yielded value is treated as a
        // Promise to await; when it resolves we resume the generator.
        private ScriptVar CreateAsyncPromise(VmFunction vmfn, Environment callEnv)
        {
            var outerPromise = new PromiseObject();
            var genObj = new GeneratorObject();

            // Drive the async body: start it, then for each yielded value
            // treat it as a Promise and chain resumption through Then().
            void DriveNext(ScriptVar resumeValue)
            {
                try
                {
                    var (yielded, done) = genObj.Next(resumeValue, g =>
                    {
                        var result = Execute(vmfn.Body, callEnv, g);
                        g.Complete(result ?? ScriptVar.CreateUndefined());
                    });

                    if (done)
                    {
                        outerPromise.Resolve(yielded);
                        return;
                    }

                    // The generator yielded a value (from `await expr`).
                    // Wrap it as a Promise and chain resumption.
                    var awaitedPromise = PromiseObject.Wrap(yielded);
                    awaitedPromise.Then(
                        resolved => MicroTaskQueue.Enqueue(() => DriveNext(resolved)),
                        rejected => MicroTaskQueue.Enqueue(() => DriveError(rejected))
                    );
                }
                catch (Exception ex)
                {
                    var msg = ex is JITException jit
                        ? (jit.VarObj ?? ScriptVar.FromString(ex.Message))
                        : ScriptVar.FromString(ex.Message);
                    outerPromise.Reject(msg);
                }
            }

            void DriveError(ScriptVar reason)
            {
                // For now: reject the outer promise on any error during resume
                outerPromise.Reject(reason);
            }

            // Start the async function immediately (first .Next call starts the thread).
            MicroTaskQueue.Enqueue(() => DriveNext(ScriptVar.CreateUndefined()));

            return outerPromise.ToScriptVar(this);
        }

        // Creates an async generator iterator. Each .next() call returns a Promise
        // that resolves to {value, done}. The generator body can use both yield
        // (to produce values) and await (internally compiled as yield too).
        // The drive loop distinguishes them: an awaited value that is a Promise
        // is re-awaited before resuming; a yield produces the next {value, done}.
        private ScriptVar CreateAsyncGeneratorIterator(VmFunction vmfn, Environment callEnv)
        {
            var genObj = new GeneratorObject();
            var iterObj = ScriptVar.CreateObject();

            ScriptVar MakeNextPromise(ScriptVar inputValue)
            {
                var promise = new PromiseObject();

                void DriveNext(ScriptVar resume)
                {
                    try
                    {
                        var (yielded, done) = genObj.Next(resume, g =>
                        {
                            var result = Execute(vmfn.Body, callEnv, g);
                            g.Complete(result ?? ScriptVar.CreateUndefined());
                        });

                        if (done)
                        {
                            // Generator finished: resolve with {value: returnVal, done: true}
                            var finalObj = ScriptVar.CreateObject();
                            finalObj.AddChild("value", yielded);
                            finalObj.AddChild("done", ScriptVar.FromBool(true));
                            promise.Resolve(finalObj);
                            return;
                        }

                        // Yielded value could be an awaited Promise (from `await` inside the body)
                        // or a user yield. We can't distinguish here without additional tagging.
                        // Wrap and check: if it resolves to a {value,done} it was a yield;
                        // otherwise it was an await value — re-drive to get the next yield.
                        // Simple approach: always treat as a user yield ({value, done:false}).
                        var resultObj = ScriptVar.CreateObject();
                        resultObj.AddChild("value", yielded);
                        resultObj.AddChild("done", ScriptVar.FromBool(false));
                        promise.Resolve(resultObj);
                    }
                    catch (Exception ex)
                    {
                        var msg = ex is JITException jit
                            ? (jit.VarObj ?? ScriptVar.FromString(ex.Message))
                            : ScriptVar.FromString(ex.Message);
                        promise.Reject(msg);
                    }
                }

                MicroTaskQueue.Enqueue(() => DriveNext(inputValue));
                return promise.ToScriptVar(this);
            }

            var nextFn = ScriptVar.CreateNativeFunction();
            nextFn.AddChild("value", ScriptVar.CreateUndefined());
            nextFn.SetCallback((scope, _) =>
            {
                var inputLink = scope.FindChild("value");
                var input = inputLink?.Var ?? ScriptVar.CreateUndefined();
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(MakeNextPromise(input));
            }, null);
            iterObj.AddChild("next", nextFn);

            // [Symbol.asyncIterator]() { return this; }
            var asyncIterSelf = ScriptVar.CreateNativeFunction();
            asyncIterSelf.SetCallback((scope, _) =>
            {
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(iterObj);
            }, null);
            iterObj.AddChild(WellKnownSymbols.AsyncIterator.GetSymbolKey(), asyncIterSelf);

            return iterObj;
        }

        // Installs poison-pill `callee` and `caller` accessors on the arguments object
        // for strict-mode functions (ES5 §10.6).
        private static void AddStrictArgumentsPoisonPills(ScriptVar argObj)
        {
            var poison = ScriptVar.CreateNativeFunction();
            poison.SetCallback((_, _) =>
                throw new ScriptException("TypeError: 'callee' and 'caller' are not accessible in strict mode"), null);

            var calleeLink = argObj.AddChild("callee", ScriptVar.CreateUndefined());
            calleeLink.Getter = poison;
            calleeLink.Setter = poison;
            var callerLink = argObj.AddChild("caller", ScriptVar.CreateUndefined());
            callerLink.Getter = poison;
            callerLink.Setter = poison;
        }

        // primitives are passed by value, objects/functions by reference
        private static ScriptVar BindArg(ScriptVar[] args, int index)
        {
            if (args == null || index >= args.Length)
                return SharedUndefined;

            return BindArgValue(args[index]);
        }

        // Bind a single argument value into a call frame. Every value binds by
        // reference — including primitives. A primitive is never mutated in place
        // under the VM: assignment and ++/-- compile to a load/compute/store whose
        // store is ReplaceWith, which swaps the binding's link to a *fresh* value
        // rather than mutating the shared ScriptVar (the in-place CopyValue used by
        // the old tree-walker is only ever applied to freshly-created locals and
        // return slots, never to a bound argument). So a callee reassigning a
        // parameter can never disturb the caller's binding, and the defensive
        // DeepCopy this path used to do for ref-counted primitives was pure
        // allocation overhead on every call that passed a variable. Interned
        // singletons (small ints, booleans) are safe to share too: Dispose is a
        // no-op on them and the bind/teardown Ref/UnRef pair stays balanced.
        private static ScriptVar BindArgValue(ScriptVar value)
        {
            return value ?? SharedUndefined;
        }

        private ScriptVar Construct(ScriptVar ctor, ScriptVar[] args)
        {
            // Proxy [[Construct]] trap
            if (ctor.IsProxy)
            {
                var handler = ctor.ProxyHandler;
                var constructTrap = handler?.FindChild("construct")?.Var;
                if (constructTrap != null && constructTrap.IsFunction)
                {
                    var argsArr = ArgsToArray(args);
                    return InvokeCallable(constructTrap, handler, new[] { ctor.ProxyTarget, argsArr, ctor });
                }
                ctor = ctor.ProxyTarget ?? ctor;
            }

            var instance = ScriptVar.CreateObject();

            // link the instance to its constructor so shared members resolve
            instance.AddChild(ScriptVar.PrototypeClassName, ctor);

            if (ctor.IsFunction)
            {
                var result = InvokeCallable(ctor, instance, args);
                // a constructor that returns an object replaces the instance
                if (result != null && result.IsObject)
                {
                    // Propagate the prototype link so instanceof still works
                    // against the constructor (and its ancestor chain).
                    if (result.FindChild(ScriptVar.PrototypeClassName) == null)
                        result.AddChildNoDup(ScriptVar.PrototypeClassName, ctor);
                    return result;
                }
            }

            return instance;
        }

        private static ScriptVar ArgsToArray(ScriptVar[] args)
        {
            var arr = ScriptVar.CreateArray();
            for (var i = 0; i < args.Length; i++)
                arr.AddChild(ScriptVar.IndexName(i), args[i]);
            arr.AddChild("length", ScriptVar.FromInt(args.Length));
            return arr;
        }

        private ScriptVar GetMember(ScriptVar obj, string name)
        {
            // Proxy [[Get]] trap
            if (obj.IsProxy)
            {
                var handler = obj.ProxyHandler;
                var getTrap = handler?.FindChild("get")?.Var;
                if (getTrap != null && getTrap.IsFunction)
                    return InvokeCallable(getTrap, handler, new[] { obj.ProxyTarget, ScriptVar.FromString(name), obj });
                obj = obj.ProxyTarget ?? obj;
            }

            var link = obj.FindChild(name);
            if (link == null && engine != null)
            {
                link = engine.FindInParentClasses(obj, name);
            }
            if (link != null)
            {
                if (link.Getter != null)
                    return InvokeCallable(link.Getter, obj, System.Array.Empty<ScriptVar>());
                return link.Var;
            }

            if (obj.IsArray && name == "length") return ScriptVar.FromInt(obj.GetArrayLength());
            if (obj.IsString && name == "length") return ScriptVar.FromInt(obj.String.Length);
            if (obj.IsFunction)
            {
                if (name == "name")
                {
                    if (obj.IsNative) return ScriptVar.FromString(obj.FindChild("name")?.Var?.String ?? "");
                    var vmfn2 = (VmFunction)obj.GetData();
                    return ScriptVar.FromString(vmfn2?.Body?.Name ?? "");
                }
                if (name == "length")
                {
                    if (obj.IsNative)
                    {
                        var cnt = 0;
                        var p2 = obj.FirstChild;
                        while (p2 != null) { cnt++; p2 = p2.Next; }
                        return ScriptVar.FromInt(cnt);
                    }
                    var vmfn3 = (VmFunction)obj.GetData();
                    return ScriptVar.FromInt(vmfn3?.Body?.Parameters?.Count ?? 0);
                }
            }

            // Dynamic size property for native containers (Set, Map, …).
            // INativeContainer is implemented by the native object stored in the
            // ScriptVar's data field; this avoids a static registered property that
            // would always return the count at registration time (0).
            if (name == "size" && obj.GetData() is INativeContainer container)
                return ScriptVar.FromInt(container.GetSize());

            // TypedArray element read: numeric index routed to the byte buffer.
            if (name.Length > 0 && (uint)(name[0] - '0') <= 9u
                && obj.GetData() is ITypedArrayAccess taMem
                && int.TryParse(name, System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture, out int taMemIdx))
                return taMemIdx >= 0 && taMemIdx < taMem.Length
                    ? taMem.GetElement(taMemIdx)
                    : ScriptVar.CreateUndefined();

            return ScriptVar.CreateUndefined();
        }

        private void SetMember(ScriptVar obj, string name, ScriptVar value, bool strict = false)
        {
            // Proxy [[Set]] trap
            if (obj.IsProxy)
            {
                var handler = obj.ProxyHandler;
                var setTrap = handler?.FindChild("set")?.Var;
                if (setTrap != null && setTrap.IsFunction)
                {
                    InvokeCallable(setTrap, handler, new[] { obj.ProxyTarget, ScriptVar.FromString(name), value, obj });
                    return;
                }
                obj = obj.ProxyTarget ?? obj;
            }

            // TypedArray element write: numeric index routed to the byte buffer.
            // Must come before FindChild so no stray child is created.
            if (name.Length > 0 && (uint)(name[0] - '0') <= 9u
                && obj.GetData() is ITypedArrayAccess taSet
                && int.TryParse(name, System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture, out int taSetIdx))
            {
                if (taSetIdx >= 0 && taSetIdx < taSet.Length)
                    taSet.SetElement(taSetIdx, value);
                return; // out-of-bounds writes silently ignored (per spec)
            }

            // Route array numeric-index writes through SetArrayIndex so the dense
            // backing store (_elements) stays in sync with the child linked list.
            if (obj.IsArray && name.Length > 0 && (uint)(name[0] - '0') <= 9u
                && int.TryParse(name, System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture, out int arrSetIdx)
                && arrSetIdx >= 0)
            {
                obj.SetArrayIndex(arrSetIdx, value);
                return;
            }

            var link = obj.FindChild(name);
            if (link != null)
            {
                if (link.Setter != null)
                {
                    InvokeCallable(link.Setter, obj, new[] { value });
                    return;
                }
                if (!link.Writable)
                {
                    if (strict) throw new ScriptException($"TypeError: Cannot assign to read-only property '{name}'");
                    return;
                }
                link.ReplaceWith(value);
            }
            else
            {
                // Check prototype chain for a setter — but skip the walk if we have
                // already confirmed there is no setter for this (prototype, name) pair.
                if (engine != null)
                {
                    var proto = obj.FindChild(ScriptVar.PrototypeClassName);
                    if (proto != null)
                    {
                        _noSetterCache ??= new System.Collections.Generic.Dictionary<(ScriptVar, string), bool>();
                        var cacheKey = (proto.Var, name);
                        if (!_noSetterCache.TryGetValue(cacheKey, out bool noSetter) || !noSetter)
                        {
                            var protoLink = engine.FindInParentClasses(obj, name);
                            if (protoLink?.Setter != null)
                            {
                                InvokeCallable(protoLink.Setter, obj, new[] { value });
                                return;
                            }
                            if (protoLink != null && !protoLink.Writable) return;
                            // No accessor found: record negative result for all future
                            // instances using the same prototype object.
                            if (protoLink == null) _noSetterCache[cacheKey] = true;
                        }
                    }
                }
                if (!obj.IsExtensible) return;
                obj.AddChild(name, value);
            }
        }

        private void DeleteMember(ScriptVar obj, string name)
        {
            // Proxy [[Delete]] trap
            if (obj.IsProxy)
            {
                var handler = obj.ProxyHandler;
                var deleteTrap = handler?.FindChild("deleteProperty")?.Var;
                if (deleteTrap != null && deleteTrap.IsFunction)
                {
                    InvokeCallable(deleteTrap, handler, new[] { obj.ProxyTarget, ScriptVar.FromString(name) });
                    return;
                }
                obj = obj.ProxyTarget ?? obj;
            }

            var link = obj.FindChild(name);
            if (link != null)
            {
                obj.RemoveLink(link);
                link.Dispose();
            }
        }

        private static bool IsInstanceOf(ScriptVar value, ScriptVar ctor)
        {
            var proto = value.FindChild(ScriptVar.PrototypeClassName);
            while (proto != null)
            {
                if (ReferenceEquals(proto.Var, ctor)) return true;
                proto = proto.Var.FindChild(ScriptVar.PrototypeClassName);
            }
            return false;
        }

        // Resolve a computed [] key to its property-name string. Integer keys go
        // through ScriptVar's cached index names, so array element access does not
        // allocate a fresh Int.ToString() string on every get/set/delete.
        // Symbol keys use their internal @@symbol:<id> representation.
        private static string KeyName(ScriptVar key)
        {
            if (key.IsSymbol) return key.GetSymbolKey();
            return key.IsInt ? ScriptVar.IndexName(key.Int) : key.String;
        }

        // Single-pass O(n) extraction of array elements into a flat ScriptVar[].
        // Replaces the previous per-element FindChild(IndexName(i)) pattern which
        // was O(n²) because each FindChild walk scans from the head of the list.
        // Collect the values produced by spreading `v`. Arrays and strings use the
        // fast path; any other value with an iterator protocol (generators, Map/Set
        // iterators, custom [Symbol.iterator]) is driven via next() until done, so
        // [...gen], f(...gen) and new C(...gen) all work like in JS.
        private ScriptVar[] CollectSpreadElements(ScriptVar v)
        {
            if (v.IsArray || v.IsString) return ExtractArrayElements(v);

            // Resolve an iterator: v itself if it already has next(), else [Symbol.iterator]().
            ScriptVar iter = null;
            if (v.FindChild("next") != null)
            {
                iter = v;
            }
            else
            {
                var symIter = v.FindChild(WellKnownSymbols.Iterator.GetSymbolKey());
                if (symIter != null && symIter.Var.IsFunction)
                    iter = InvokeCallable(symIter.Var, v, Array.Empty<ScriptVar>());
            }

            var nextLink = iter?.FindChild("next");
            if (nextLink == null || !nextLink.Var.IsFunction) return Array.Empty<ScriptVar>();

            var items = new List<ScriptVar>();
            while (true)
            {
                var result = InvokeCallable(nextLink.Var, iter, Array.Empty<ScriptVar>());
                if (result == null || (result.FindChild("done")?.Var.Bool ?? true)) break;
                items.Add(result.FindChild("value")?.Var ?? ScriptVar.CreateUndefined());
            }
            return items.ToArray();
        }

        private static ScriptVar[] ExtractArrayElements(ScriptVar arr)
        {
            // Spreading a string iterates its code points (so [..."😀"] is one element,
            // not two), matching JS's string iterator.
            if (arr.IsString)
            {
                var s = arr.String;
                var chars = new List<ScriptVar>(s.Length);
                for (var i = 0; i < s.Length;)
                {
                    var adv = char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
                    chars.Add(ScriptVar.FromString(s.Substring(i, adv)));
                    i += adv;
                }
                return chars.ToArray();
            }

            var len = arr.IsArray ? arr.GetArrayLength() : 0;
            if (len == 0) return Array.Empty<ScriptVar>();
            var result = new ScriptVar[len];
            var child = arr.FirstChild;
            while (child != null)
            {
                if (int.TryParse(child.Name,
                                 System.Globalization.NumberStyles.None,
                                 System.Globalization.CultureInfo.InvariantCulture,
                                 out var idx)
                    && (uint)idx < (uint)len)
                    result[idx] = child.Var;
                child = child.Next;
            }
            for (var i = 0; i < len; i++)
                result[i] ??= ScriptVar.CreateUndefined();
            return result;
        }

        // Record the callee seen at a call site, advancing its morphism class:
        //   Uninitialized → Monomorphic  (first callee stored in Callee0)
        //   Monomorphic   → Bimorphic    (second distinct callee stored in Callee1)
        //   Bimorphic     → Megamorphic  (three or more distinct callees)
        //   Megamorphic   stays Megamorphic
        // Spread calls (CallSpread / CallMethodSpread) are not profiled here because
        // the callee is pop'd before a site index is available; they can be added later.
        private static void RecordCallSite(Chunk.CallSiteProfile[] profiles, int site, ScriptVar callee)
        {
            ref var cp = ref profiles[site];
            switch (cp.State)
            {
                case Chunk.CallSiteMorphism.Uninitialized:
                    cp.Callee0 = callee;
                    cp.State   = Chunk.CallSiteMorphism.Monomorphic;
                    break;
                case Chunk.CallSiteMorphism.Monomorphic:
                    if (!ReferenceEquals(cp.Callee0, callee))
                    {
                        cp.Callee1 = callee;
                        cp.State   = Chunk.CallSiteMorphism.Bimorphic;
                    }
                    break;
                case Chunk.CallSiteMorphism.Bimorphic:
                    if (!ReferenceEquals(cp.Callee0, callee) && !ReferenceEquals(cp.Callee1, callee))
                        cp.State = Chunk.CallSiteMorphism.Megamorphic;
                    break;
                // Megamorphic: no further transitions
            }
        }

        private static Chunk.BinaryTypeFlags TypeFlagOf(ScriptVar v)
        {
            // Both int32 and LargeInt (int64) are integers in DScript and share the
            // same arithmetic path (VirtualMachine.IntBinary), so profile them alike —
            // otherwise an accumulator that grows past int32 is misclassified as
            // "Other" and defeats every integer speculation tier.
            if (v.IsAnyInt) return Chunk.BinaryTypeFlags.Int;
            if (v.IsDouble) return Chunk.BinaryTypeFlags.Double;
            if (v.IsString) return Chunk.BinaryTypeFlags.String;
            return Chunk.BinaryTypeFlags.Other;
        }

        private static void RecordBinaryOpTypes(Chunk.BinaryOpProfile[] profiles, int site,
            ScriptVar left, ScriptVar right)
        {
            ref var bp = ref profiles[site];
            bp.LeftTypes  |= TypeFlagOf(left);
            bp.RightTypes |= TypeFlagOf(right);
        }

        // Resolve a variable name through the inline cache. A site (identified by
        // its operand offset) that re-resolves the same name against the same,
        // unchanged environment reuses the cached link without walking the scope
        // chain. Only successful resolutions are cached; misses always re-resolve
        // (so a later-declared binding is picked up).
        private static ScriptVarLink ResolveCached(Chunk.InlineCacheEntry[] cache, Chunk chunk, int site, Environment env, int nameIdx)
        {
            ref var entry = ref cache[site];
            if (ReferenceEquals(entry.Env, env) && entry.Version == env.Version)
            {
                return entry.Link;
            }

            var link = env.Resolve(chunk.Names[nameIdx]);
            if (link != null)
            {
                entry.Env = env;
                entry.Version = env.Version;
                entry.Link = link;
            }
            return link;
        }

        // Direct int-op-int result for the BinaryConst fast path. Mirrors the int
        // branch of ScriptVar.MathsOp exactly. Returns false for operators handled
        // only by the general path (e.g. ===), so the caller falls back.
        // Abandon a bailed speculative JIT run and re-execute the chunk on the
        // interpreter, returning its result. Safe because the speculative tier only
        // compiles pure (call-free) functions, so no side effect occurred before the
        // failing guard. After DeoptThreshold deopts the speculation is given up: the
        // compiled delegate is cleared and the chunk is marked to recompile with the
        // conservative tier. internal so JIT-compiled code can call it.
        // Deopt path for a speculative unboxed-int chunk that overflowed 32 bits.
        // The interpreter promotes to double and the conservative tier routes +,-,*
        // through IntBinary (also overflow-safe), so prefer it and never re-speculate
        // this chunk. Safe because speculative tiers only compile pure chunks.
        private ScriptVar DeoptimizeOverflow(Chunk chunk, Environment env, GeneratorObject genObj, GeneratorState gsState)
        {
            chunk.PreferConservativeTier = true;
            chunk.CompiledDelegate = null;
            chunk.JitState = Chunk.JitStatus.Cold;
            return Execute(chunk, env, genObj, gsState, bypassJit: true) ?? ScriptVar.CreateUndefined();
        }

        internal ScriptVar Deoptimize(DeoptFrame frame)
        {
            var chunk = frame.Chunk;
            chunk.DeoptCount++;
            if (chunk.DeoptCount >= JitThresholds.DeoptThreshold)
            {
                chunk.PreferConservativeTier = true;
                chunk.CompiledDelegate = null;
                chunk.JitState = Chunk.JitStatus.Cold;
            }
            return Execute(chunk, frame.Env, bypassJit: true) ?? ScriptVar.CreateUndefined();
        }

        // Fused zero-argument method call for JIT-compiled code, mirroring the
        // GetPropCall0 opcode: resolve obj.name (through the inline cache, getters and
        // prototype chain) and invoke it with obj as the receiver and no arguments.
        internal ScriptVar JitGetPropCall0(ScriptVar obj, string name, PropCacheCell cell)
            => InvokeCallable(JitGetPropCached(obj, name, cell), obj, System.Array.Empty<ScriptVar>());

        // Assign a variable for JIT-compiled code, mirroring the SetVar(Pop) opcode:
        // resolve through the scope chain and ReplaceWith; in non-strict mode a missing
        // binding creates a global. internal static (no VM state needed).
        internal static void JitSetVar(Environment env, string name, ScriptVar value, bool strict)
        {
            var link = env.Resolve(name);
            if (link != null) { link.ReplaceWith(value); return; }
            if (strict) throw new ScriptException($"ReferenceError: '{name}' is not defined");
            var global = env.Global();
            global.Vars.AddChildNoDup(name, value);
            global.Version++;
        }

        // Set a property for JIT-compiled code, mirroring SetProp(Pop) (delegates to
        // SetMember; getters/setters/proxies/shape bumps handled there).
        internal void JitSetProp(ScriptVar obj, string name, ScriptVar value, bool strict)
            => SetMember(obj, name, value, strict);

        // Slow path / cache-refresh for the SetProp inline cache (Lever 2b). The caller
        // (emitted JIT code or the closure node) has already missed the per-site cache,
        // so this does the full SetMember write and then refreshes the cell so subsequent
        // writes hit the fast path. Only own, non-accessor data properties of plain
        // objects are cached.
        //
        // Write-safety invariant: a SetProp site's PropCacheCell is populated ONLY here,
        // with the link returned by obj.FindChild(name) — which is always an OWN property
        // of obj (FindChild never walks the prototype chain). So both the shape-keyed and
        // identity-keyed entries in a SetProp cell are own data properties, and the inline
        // fast path may overwrite them in place without ever writing through a prototype.
        internal void JitSetPropCached(ScriptVar obj, string name, ScriptVar value, PropCacheCell cell, bool strict)
        {
            SetMember(obj, name, value, strict);

            if (obj.IsObject && !obj.IsProxy)
            {
                var link = obj.FindChild(name);
                if (link != null && link.Getter == null && link.Setter == null)
                    cell.Insert(obj, link);
            }
        }

        // Enter a block scope for JIT-compiled code, mirroring EnterBlock: return a new
        // block-scope environment whose parent is the current one. LeaveBlock is just
        // `current = current.Parent`, so no explicit stack is needed.
        internal static Environment JitEnterBlock(Environment env)
            => new(ScriptVar.CreateObject(), env, isBlockScope: true);

        // Leave a block scope for JIT-compiled code, mirroring LeaveBlock: restore the
        // parent environment saved by the matching JitEnterBlock.
        internal static Environment JitLeaveBlock(Environment env) => env.Parent;

        // Allocate a function's positional slot frame on its call environment when the
        // chunk uses slots, initialising every slot to undefined (matching the
        // name-based path, where a local reads undefined before assignment). No-op for
        // non-slotted chunks (including generators/async, which are never promoted).
        private static void InitSlotFrame(Chunk body, Environment env)
        {
            if (!body.UsesSlots) return;
            var slots = new ScriptVar[body.SlotCount];
            for (var i = 0; i < slots.Length; i++) slots[i] = SharedUndefined;
            env.Slots = slots;
        }

        // Variable declarations for JIT-compiled code, mirroring the Declare* opcodes.
        internal static void JitDeclareVar(Environment env, string name)
        {
            var declEnv = env;
            while (declEnv.IsBlockScope && declEnv.Parent != null)
                declEnv = declEnv.Parent;
            declEnv.Vars.FindChildOrCreate(name);
            declEnv.Version++;
        }

        internal static void JitDeclareLocal(Environment env, string name)
        {
            env.Vars.FindChildOrCreate(name);
            env.Version++;
        }

        internal static void JitDeclareConst(Environment env, string name)
        {
            env.Vars.FindChildOrCreate(name, ScriptVar.Flags.Undefined, readOnly: true);
            env.Version++;
        }

        // Read a property for JIT-compiled code through a per-site inline cache,
        // mirroring the GetProp opcode (cache + miss path) exactly: a hit reuses the
        // cached link (invoking its getter if any); a miss does the full resolve —
        // proxy [[Get]], own child, prototype-chain walk, getter, built-in
        // length/size — and refreshes the cell for object/array shapes. internal so
        // the JIT emitter / closure back-end can call it.
        internal ScriptVar JitGetPropCached(ScriptVar obj, string name, PropCacheCell cell)
        {
            var cached = cell.Lookup(obj);
            if (cached != null)
                return cached.Getter != null
                    ? InvokeCallable(cached.Getter, obj, System.Array.Empty<ScriptVar>())
                    : cached.Var;

            if (obj.IsProxy) return GetMember(obj, name);

            var link = obj.FindChild(name);
            if (link == null && engine != null)
                link = engine.FindInParentClasses(obj, name);

            if (link != null)
            {
                if (obj.IsObject || obj.IsArray)
                    cell.Insert(obj, link);
                return link.Getter != null
                    ? InvokeCallable(link.Getter, obj, System.Array.Empty<ScriptVar>())
                    : link.Var;
            }

            if (obj.IsArray && name == "length") return ScriptVar.FromInt(obj.GetArrayLength());
            if (obj.IsString && name == "length") return ScriptVar.FromInt(obj.String.Length);
            if (name == "size" && obj.GetData() is INativeContainer container)
                return ScriptVar.FromInt(container.GetSize());
            if (name.Length > 0 && (uint)(name[0] - '0') <= 9u
                && obj.GetData() is ITypedArrayAccess taJit
                && int.TryParse(name, System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture, out int taJitIdx))
                return taJitIdx >= 0 && taJitIdx < taJit.Length
                    ? taJit.GetElement(taJitIdx)
                    : ScriptVar.CreateUndefined();
            return ScriptVar.CreateUndefined();
        }

        // Resolve a DATA property value for the speculative tiers' guarded field reads
        // (Lever 2c). Returns the property value, or null to signal the caller must deopt
        // — a proxy, an accessor (getter), or an absent property, none of which the
        // unboxed numeric fast path may handle. Never invokes a getter, so the read is
        // side-effect-free and safe to repeat under the tiers' re-execution-on-deopt
        // model. Caches resolved data-property links in the per-site cell.
        internal ScriptVar JitReadDataField(ScriptVar obj, string name, PropCacheCell cell)
        {
            var link = cell.Lookup(obj);
            if (link != null) return link.Getter != null ? null : link.Var;
            if (obj.IsProxy) return null;
            link = obj.FindChild(name);
            if (link == null && engine != null) link = engine.FindInParentClasses(obj, name);
            if (link == null || link.Getter != null) return null;
            if (obj.IsObject || obj.IsArray) cell.Insert(obj, link);
            return link.Var;
        }

        // Resolve a variable for JIT-compiled code, mirroring the GetVar opcode
        // handler exactly: walk the lexical scope chain, fall back to the virtual
        // globalThis binding, then to undefined. internal so the JIT emitter in
        // DScript.Jit can call it directly.
        internal static ScriptVar JitGetVar(Environment env, string name)
        {
            var link = env.Resolve(name);
            if (link != null) return link.Var;
            if (name == "globalThis") return env.Global().Vars;
            return SharedUndefined;
        }

        // ── extra opcode helpers for JIT-compiled code (mirror the opcode handlers) ──

        internal ScriptVar JitGetIndex(ScriptVar obj, ScriptVar key)
        {
            if (key.IsAnyInt && obj.GetData() is ITypedArrayAccess ta)
            {
                var idx = key.Int;
                return idx >= 0 && idx < ta.Length ? ta.GetElement(idx) : ScriptVar.CreateUndefined();
            }
            return GetMember(obj, KeyName(key));
        }

        // void, mirroring JitSetProp: the emitter re-pushes the value when the
        // expression form needs it.
        internal void JitSetIndex(ScriptVar obj, ScriptVar key, ScriptVar value, bool strict)
        {
            if (key.IsAnyInt && obj.GetData() is ITypedArrayAccess ta)
            {
                var idx = key.Int;
                if (idx >= 0 && idx < ta.Length) ta.SetElement(idx, value);
                return;
            }
            SetMember(obj, KeyName(key), value, strict);
        }

        internal static ScriptVar JitNegate(ScriptVar a)
        {
            if (a.IsAnyInt) return IntOrDouble(-a.Long);
            if (a.IsDouble) return ScriptVar.FromDouble(-a.Float);
            if (a.IsBigInt) return ScriptVar.CreateBigInt(-a.BigIntData);
            return Zero.MathsOp(a, (ScriptLex.LexTypes)'-');
        }

        internal static ScriptVar JitBitNot(ScriptVar a)
            => a.IsBigInt ? ScriptVar.CreateBigInt(~a.BigIntData) : ScriptVar.FromInt(~a.Int);

        internal static ScriptVar JitTypeof(ScriptVar a) => ScriptVar.FromString(a.GetObjectType());

        internal static ScriptVar JitToNumber(ScriptVar a) => CoerceToNumber(a);

        internal static ScriptVar JitShift(ScriptVar a, ScriptVar b, ScriptLex.LexTypes op) => ApplyShift(a, b, op);

        // Object/array literal construction for JIT-compiled code, mirroring the
        // NewObject/NewArray/InitProp/InitElem opcodes. Init* take the object/array
        // (kept on the stack by the interpreter via Peek) plus the value, mutate it,
        // and return it so the emitter can thread the single instance through the
        // remaining initialisers without re-creating it.
        internal static ScriptVar JitNewObject() => ScriptVar.CreateObject();

        internal static ScriptVar JitNewArray() => ScriptVar.CreateArray();

        internal static ScriptVar JitInitProp(ScriptVar obj, ScriptVar value, string name)
        {
            obj.AddChild(name, value);
            return obj;
        }

        internal static ScriptVar JitInitElem(ScriptVar arr, ScriptVar value, int index)
        {
            arr.SetArrayIndex(index, value);
            return arr;
        }

        // Constructor calls and spread for JIT-compiled code.

        // `new Ctor(...args)` — mirrors the fast path in the New opcode handler.
        internal ScriptVar JitNew(ScriptVar ctor, ScriptVar[] args)
        {
            if (ctor != null && ctor.IsFunction && !ctor.IsNative)
            {
                var instance = ScriptVar.CreateObject();
                instance.AddChild(ScriptVar.PrototypeClassName, ctor);
                var result = InvokeCallable(ctor, instance, args);
                return result != null && result.IsObject ? result : instance;
            }
            return Construct(ctor, args);
        }

        // Object spread — mirrors MergeObject: copy own non-prototype properties.
        internal ScriptVar JitMergeObject(ScriptVar target, ScriptVar source)
        {
            var member = source.FirstChild;
            while (member != null)
            {
                if (member.Name != ScriptVar.PrototypeClassName)
                    SetMember(target, member.Name, member.Var, false);
                member = member.Next;
            }
            return target;
        }

        // Property overwrite (object spread property) — mirrors InitPropOverwrite.
        internal static ScriptVar JitInitPropOverwrite(ScriptVar obj, string name, ScriptVar value)
        {
            obj.AddChildNoDup(name, value);
            return obj;
        }

        // Array spread element append — mirrors AppendElem.
        internal static ScriptVar JitAppendElem(ScriptVar arr, ScriptVar value)
        {
            arr.AppendArrayElement(value);
            return arr;
        }

        // MakeClosure opcode: create a function object that captures the current
        // environment as its defining scope — identical to the interpreter's handler.
        internal static ScriptVar JitMakeClosure(Environment env, Chunk fnChunk)
        {
            var fn = ScriptVar.CreateFunction();
            fn.SetData(new VmFunction(fnChunk, env));
            return fn;
        }

        // Full binary-operator semantics for JIT back-ends that do not inline
        // arithmetic (e.g. the closure-threaded compiler): identical to the Binary
        // opcode handler — integer fast path, else MathsOp.
        internal static ScriptVar JitBinary(ScriptVar a, ScriptVar b, ScriptLex.LexTypes op)
        {
            if (a.IsAnyInt && b.IsAnyInt && IntBinary(a.Long, b.Long, op, out var fast))
                return fast;
            return a.MathsOp(b, op);
        }

        // internal (not private) so the JIT emitter in DScript.Jit can call it
        // directly for the integer fast path it does not inline.
        // A 64-bit arithmetic result as an int32 when it fits, LargeInt for wider
        // int64 values, and double only for true floating-point results. Keeping
        // results as integers avoids promoting hot accumulator loops to double.
        private static ScriptVar IntOrDouble(long value)
            => value >= int.MinValue && value <= int.MaxValue
                ? ScriptVar.FromInt((int)value)
                : ScriptVar.FromLong(value);

        // Phase 1b (Lever 1): classify a ScriptVar for the operand stack, unboxing the
        // full integer range (int32 and LargeInt) into a Value.Int(long). Doubles and
        // everything else are carried as a Ref so the exact instance round-trips without
        // a fresh allocation (doubles gain nothing from unboxing until the double fast
        // path lands, and re-boxing the shared null/undefined would allocate).
        private static Value ToValue(ScriptVar sv)
        {
            if (sv != null && sv.IsAnyInt) return Value.Int(sv.Long);
            return Value.Ref(sv);
        }

        // The Value-native analogue of IntOrDouble. Value.Int carries a 64-bit payload, so
        // any integer result stays unboxed; ToScriptVar uses FromLong, exactly matching
        // IntOrDouble's int32/LargeInt promotion.
        private static Value IntOrDoubleValue(long value) => Value.Int(value);

        // Extract an integer operand from a Value without allocating: a raw Value.Int, or
        // a boxed int32/LargeInt ScriptVar carried as a Ref (e.g. pushed via Push(ScriptVar)).
        private static bool TryLong(in Value v, out long value)
        {
            if (v.IsInt) { value = v.AsLong; return true; }
            if (v.IsRef) { var sv = v.AsRef; if (sv != null && sv.IsAnyInt) { value = sv.Long; return true; } }
            value = 0;
            return false;
        }

        // Value-native mirror of IntBinary: identical operator semantics (int64 +,-,*
        // with int32/LargeInt promotion via IntOrDoubleValue; '/' real-or-even-int; ToInt32
        // bitwise; '%'; 0/1 comparisons) but producing an unboxed Value with no ScriptVar
        // allocation on the common int32 path.
        internal static bool IntBinaryValue(long a, long b, ScriptLex.LexTypes op, out Value result)
        {
            switch ((char)op)
            {
                case '+': result = IntOrDoubleValue(a + b); return true;
                case '-': result = IntOrDoubleValue(a - b); return true;
                case '*': result = IntOrDoubleValue(a * b); return true;
                case '/':
                    if (b == 0) { result = Value.Double((double)a / b); return true; }
                    if (b == -1) { result = IntOrDoubleValue(-a); return true; }
                    if (a % b == 0) { result = IntOrDoubleValue(a / b); return true; }
                    result = Value.Double((double)a / b); return true;
                case '&': result = Value.Int((int)a & (int)b); return true;
                case '|': result = Value.Int((int)a | (int)b); return true;
                case '^': result = Value.Int((int)a ^ (int)b); return true;
                case '%':
                    if (b == 0) { result = Value.Double(double.NaN); return true; }
                    if (b == -1) { result = Value.Int(0); return true; }
                    result = IntOrDoubleValue(a % b); return true;
                case (char)ScriptLex.LexTypes.Equal:  result = Value.Bool(a == b); return true;
                case (char)ScriptLex.LexTypes.NEqual: result = Value.Bool(a != b); return true;
                case '<':                              result = Value.Bool(a <  b); return true;
                case (char)ScriptLex.LexTypes.LEqual: result = Value.Bool(a <= b); return true;
                case '>':                              result = Value.Bool(a >  b); return true;
                case (char)ScriptLex.LexTypes.GEqual: result = Value.Bool(a >= b); return true;
                default: result = default; return false;
            }
        }

        internal static bool IntBinary(long a, long b, ScriptLex.LexTypes op, out ScriptVar result)
        {
            switch ((char)op)
            {
                // JS numbers are doubles; +, -, * stay int64 while they fit. Only
                // promote to LargeInt or double when the result leaves int32 range.
                case '+': result = IntOrDouble(a + b); return true;
                case '-': result = IntOrDouble(a - b); return true;
                case '*': result = IntOrDouble(a * b); return true;
                // JS '/' is always real division (1/3 -> 0.333…). Keep an int result
                // only when it divides evenly, so 10/2 stays an int fast path.
                case '/':
                    if (b == 0) { result = ScriptVar.FromDouble((double)a / b); return true; }
                    if (b == -1) { result = IntOrDouble(-a); return true; } // avoids MinValue/-1 overflow
                    if (a % b == 0) { result = IntOrDouble(a / b); return true; }
                    result = ScriptVar.FromDouble((double)a / b); return true;
                // Bitwise ops always apply JS ToInt32 coercion.
                case '&': result = ScriptVar.FromInt((int)a & (int)b); return true;
                case '|': result = ScriptVar.FromInt((int)a | (int)b); return true;
                case '^': result = ScriptVar.FromInt((int)a ^ (int)b); return true;
                case '%':
                    if (b == 0) { result = ScriptVar.FromDouble(double.NaN); return true; }
                    if (b == -1) { result = ScriptVar.FromInt(0); return true; } // avoids MinValue%-1 overflow
                    result = IntOrDouble(a % b); return true;
                case (char)ScriptLex.LexTypes.Equal:  result = a == b ? SharedTrue : SharedFalse; return true;
                case (char)ScriptLex.LexTypes.NEqual: result = a != b ? SharedTrue : SharedFalse; return true;
                case '<':                              result = a <  b ? SharedTrue : SharedFalse; return true;
                case (char)ScriptLex.LexTypes.LEqual: result = a <= b ? SharedTrue : SharedFalse; return true;
                case '>':                              result = a >  b ? SharedTrue : SharedFalse; return true;
                case (char)ScriptLex.LexTypes.GEqual: result = a >= b ? SharedTrue : SharedFalse; return true;
                default: result = null; return false;
            }
        }

        private static int ReadOperand(byte[] code, ref int ip)
        {
            // Little-endian read straight from the contiguous code array, skipping
            // the Chunk.ReadInt property indirection (and its CodeBytes re-fetch)
            // that would otherwise run on every operand in the dispatch loop.
            var value = code[ip]
                        | (code[ip + 1] << 8)
                        | (code[ip + 2] << 16)
                        | (code[ip + 3] << 24);
            ip += 4;
            return value;
        }

        private static ScriptVar ApplyShift(ScriptVar a, ScriptVar b, ScriptLex.LexTypes op)
        {
            var shift = b.Int;
            switch (op)
            {
                case ScriptLex.LexTypes.LShift: return ScriptVar.FromInt(a.Int << shift);
                case ScriptLex.LexTypes.RShift: return ScriptVar.FromInt(a.Int >> shift);
                case ScriptLex.LexTypes.RShiftUnsigned: return ScriptVar.FromInt(a.Int >>> shift);
                default: throw new ScriptException("Unsupported shift operator");
            }
        }

        private static ScriptVar CoerceToNumber(ScriptVar value)
        {
            // Already numeric: return the value directly (avoid an allocation on the
            // common path where the operand is already the right type).
            if (value.IsInt) return value;
            if (value.IsDouble) return value;
            if (value.IsNull) return SharedFalse; // 0

            if (value.IsString &&
                double.TryParse(value.String, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return ScriptVar.FromDouble(parsed);
            }

            return ScriptVar.FromDouble(double.NaN);
        }
    }
}
