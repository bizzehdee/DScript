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
        private ScriptVar[] stack = new ScriptVar[64];
        private int sp;

        // Shared read-only operand for unary 0-based ops. MathsOp only reads its
        // operands (results are always freshly allocated), so sharing is safe and
        // avoids allocating a throwaway zero on every Negate/Not.
        private static readonly ScriptVar Zero = new(0);

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

        // Pool of call-frame binding containers. A function whose frame cannot
        // escape (no closure captures it — see Chunk.RecyclableFrame) returns its
        // bindings var here on exit and the next such call reuses it, avoiding a
        // ScriptVar allocation per call. Reuse detaches (does not dispose) the old
        // bindings, preserving the lifetime of any value that escaped the frame.
        // Bounded to prevent unbounded memory growth in recursive workloads.
        private const int FramePoolMaxSize = 64;
        private readonly Stack<ScriptVar> frameVarsPool = new();

        private ScriptVar BorrowFrameVars()
        {
            return frameVarsPool.Count > 0 ? frameVarsPool.Pop() : new ScriptVar(ScriptVar.Flags.Object);
        }

        private void ReturnFrameVars(ScriptVar vars)
        {
            if (frameVarsPool.Count >= FramePoolMaxSize) return; // drop to GC instead of growing pool
            vars.ResetForReuse();
            frameVarsPool.Push(vars);
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
            public ScriptVar Object;         // reference equality
            public int ShapeVersion;
            public string Name;              // interned string — ReferenceEquals is valid
            public ScriptVarLink Link;       // points into the object's child list
        }
        private readonly PropCacheEntry[] _propCache = new PropCacheEntry[256];

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

        public VirtualMachine() { }

        public VirtualMachine(ScriptEngine engine)
        {
            this.engine = engine;
        }

        /// <summary>
        /// Drain all pending micro-tasks. Call after running async code.
        /// </summary>
        public static void DrainMicroTasks() => MicroTaskQueue.DrainAll();

        private void Push(ScriptVar value)
        {
            if (sp == stack.Length)
            {
                System.Array.Resize(ref stack, stack.Length * 2);
            }
            stack[sp++] = value;
        }

        private ScriptVar Pop() => stack[--sp];

        private ScriptVar Peek() => stack[sp - 1];

        /// <summary>
        /// Execute a top-level chunk and return the produced value (the operand
        /// of a <see cref="OpCode.Return"/>, or undefined when the chunk halts).
        /// </summary>
        public ScriptVar Run(Chunk chunk)
        {
            return Run(chunk, new Environment(new ScriptVar(ScriptVar.Flags.Object), null));
        }

        public ScriptVar Run(Chunk chunk, Environment env)
        {
            var startDepth = sp;
            var result = Execute(chunk, env);

            // discard anything the chunk left behind to keep the stack balanced
            sp = startDepth;

            return result ?? new ScriptVar(ScriptVar.Flags.Undefined);
        }

        private ScriptVar Execute(Chunk chunk, Environment env, GeneratorObject genObj = null)
        {
            var code = chunk.CodeBytes;
            // Hoist the inline-cache array once so each GetVar/SetVar avoids the
            // property's lazy null-check on every resolution.
            var cache = chunk.InlineCache;
            var ip = 0;

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

            try
            {

            while (ip < code.Length)
            {
                var instrIp = ip; // capture before advancing — used for line lookup on error
                var op = (OpCode)code[ip];
                ip++;

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
                        Push(chunk.Constants[ReadOperand(code, ref ip)].Materialize());
                        break;
                    case OpCode.PushUndefined:
                        Push(new ScriptVar(ScriptVar.Flags.Undefined));
                        break;
                    case OpCode.PushNull:
                        Push(new ScriptVar(ScriptVar.Flags.Null));
                        break;
                    case OpCode.PushTrue:
                        Push(new ScriptVar(1));
                        break;
                    case OpCode.PushFalse:
                        Push(new ScriptVar(0));
                        break;

                    case OpCode.Pop:
                        Pop();
                        break;
                    case OpCode.Dup:
                        Push(Peek());
                        break;
                    case OpCode.Dup2:
                    {
                        var b = stack[sp - 1];
                        var a = stack[sp - 2];
                        Push(a);
                        Push(b);
                        break;
                    }
                    case OpCode.EnumKeys:
                    {
                        var obj = Pop();
                        var keys = new ScriptVar(ScriptVar.Flags.Array);
                        var index = 0;
                        var member = obj.FirstChild;
                        while (member != null)
                        {
                            if (member.Name != ScriptVar.PrototypeClassName)
                            {
                                keys.SetArrayIndex(index++, new ScriptVar(member.Name));
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
                        Push(link != null ? link.Var : new ScriptVar(ScriptVar.Flags.Undefined));
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
                            // New global binding: bump the global scope's version so
                            // any cached resolutions of this name re-validate.
                            var global = env.Global();
                            global.Vars.AddChildNoDup(chunk.Names[nameIdx], value);
                            global.Version++;
                        }
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

                        // Inline property cache: 256-slot direct-mapped, hashed via
                        // Fibonacci/multiplicative hashing so that names whose indices
                        // share the same low byte are spread across distinct slots rather
                        // than evicting each other on every access.
                        var cacheSlot = (int)((uint)nameIdx * 2654435761u >> 24);
                        ref var ce = ref _propCache[cacheSlot];
                        if (ReferenceEquals(ce.Object, obj) &&
                            ce.ShapeVersion == obj.ShapeVersion &&
                            ReferenceEquals(ce.Name, name) &&
                            ce.Link != null)
                        {
                            Push(ce.Link.Var);
                            break;
                        }

                        // Cache miss: full lookup via GetMember.
                        var link = obj.FindChild(name);
                        if (link == null && engine != null)
                            link = engine.FindInParentClasses(obj, name);

                        ScriptVar propResult;
                        if (link != null)
                        {
                            propResult = link.Var;
                            // Cache the link for objects/arrays (not for primitives or
                            // values that fall back to built-ins like .length).
                            if (obj.IsObject || obj.IsArray)
                            {
                                ce.Object = obj;
                                ce.ShapeVersion = obj.ShapeVersion;
                                ce.Name = name;
                                ce.Link = link;
                            }
                        }
                        else
                        {
                            // Built-in virtual properties: not cached (rare fast path).
                            if (obj.IsArray && name == "length")
                                propResult = new ScriptVar(obj.GetArrayLength());
                            else if (obj.IsString && name == "length")
                                propResult = new ScriptVar(obj.String.Length);
                            else
                                propResult = new ScriptVar(ScriptVar.Flags.Undefined);
                        }

                        Push(propResult);
                        break;
                    }
                    case OpCode.SetProp:
                    {
                        var name = chunk.Names[ReadOperand(code, ref ip)];
                        var value = Pop();
                        var obj = Pop();
                        SetMember(obj, name, value);
                        Push(value);
                        break;
                    }
                    case OpCode.GetIndex:
                    {
                        var key = Pop();
                        var obj = Pop();
                        Push(GetMember(obj, KeyName(key)));
                        break;
                    }
                    case OpCode.SetIndex:
                    {
                        var value = Pop();
                        var key = Pop();
                        var obj = Pop();
                        SetMember(obj, KeyName(key), value);
                        Push(value);
                        break;
                    }
                    case OpCode.DeleteProp:
                    {
                        var name = chunk.Names[ReadOperand(code, ref ip)];
                        DeleteMember(Pop(), name);
                        Push(new ScriptVar(true));
                        break;
                    }
                    case OpCode.DeleteIndex:
                    {
                        var key = Pop();
                        DeleteMember(Pop(), KeyName(key));
                        Push(new ScriptVar(true));
                        break;
                    }

                    case OpCode.In:
                    {
                        var obj = Pop();
                        var key = Pop();
                        var exists = obj.FindChild(key.String) != null ||
                                     (engine != null && engine.FindInParentClasses(obj, key.String) != null);
                        Push(new ScriptVar(exists));
                        break;
                    }
                    case OpCode.InstanceOf:
                    {
                        var ctor = Pop();
                        var value = Pop();
                        Push(new ScriptVar(IsInstanceOf(value, ctor)));
                        break;
                    }

                    case OpCode.Binary:
                    {
                        var operatorCode = (ScriptLex.LexTypes)ReadOperand(code, ref ip);
                        var b = Pop();
                        var a = Pop();
                        // int-vs-int fast path (e.g. `s + i` between two variables):
                        // compute directly, skipping MathsOp's flag checks + dispatch.
                        if (a.IsInt && b.IsInt && IntBinary(a.Int, b.Int, operatorCode, out var fast))
                        {
                            Push(fast);
                        }
                        else
                        {
                            Push(a.MathsOp(b, operatorCode));
                        }
                        break;
                    }
                    case OpCode.BinaryConst:
                    {
                        // Fused Constant + Binary: the right operand is a literal,
                        // read from the constant pool instead of the stack.
                        var operatorCode = (ScriptLex.LexTypes)ReadOperand(code, ref ip);
                        var constant = chunk.Constants[ReadOperand(code, ref ip)];
                        var a = Pop();
                        // Int-vs-int-literal fast path: compute directly, skipping
                        // both the constant ScriptVar materialization and MathsOp.
                        if (constant.Kind == ConstantKind.Int && a.IsInt &&
                            IntBinary(a.Int, constant.IntValue, operatorCode, out var fast))
                        {
                            Push(fast);
                        }
                        else
                        {
                            Push(a.MathsOp(constant.Materialize(), operatorCode));
                        }
                        break;
                    }
                    case OpCode.BinaryIntConst:
                    {
                        // Like BinaryConst but the integer value is stored inline in
                        // the instruction stream, skipping the constant-pool lookup.
                        // Emitted when the right operand is a known integer literal.
                        var operatorCode = (ScriptLex.LexTypes)ReadOperand(code, ref ip);
                        var intValue = ReadOperand(code, ref ip);
                        var a = Pop();
                        if (a.IsInt && IntBinary(a.Int, intValue, operatorCode, out var fast))
                            Push(fast);
                        else
                            Push(a.MathsOp(new ScriptVar(intValue), operatorCode));
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
                        if (a.IsInt) Push(new ScriptVar(-a.Int));
                        else if (a.IsDouble) Push(new ScriptVar(-a.Float));
                        else Push(Zero.MathsOp(a, (ScriptLex.LexTypes)'-'));
                        break;
                    }
                    case OpCode.Not:
                    {
                        var a = Pop();
                        Push(a.MathsOp(Zero, ScriptLex.LexTypes.Equal));
                        break;
                    }
                    case OpCode.BitNot:
                    {
                        var a = Pop();
                        Push(new ScriptVar(~a.Int));
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
                        Push(new ScriptVar(a.GetObjectType()));
                        break;
                    }

                    case OpCode.NewObject:
                        Push(new ScriptVar(ScriptVar.Flags.Object));
                        break;
                    case OpCode.NewArray:
                        Push(new ScriptVar(ScriptVar.Flags.Array));
                        break;
                    case OpCode.InitProp:
                    {
                        var name = chunk.Names[ReadOperand(code, ref ip)];
                        var value = Pop();
                        Peek().AddChild(name, value); // object kept on stack
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
                        Peek().AddChild(key.String, value); // object kept on stack
                        break;
                    }

                    case OpCode.Jump:
                        ip = ReadOperand(code, ref ip);
                        break;
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
                            Push(new ScriptVar(ScriptVar.Flags.Undefined));
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
                        var fn = new ScriptVar(ScriptVar.Flags.Function);
                        fn.SetData(new VmFunction(fnChunk, env));
                        Push(fn);
                        break;
                    }
                    case OpCode.Call:
                    {
                        var argc = ReadOperand(code, ref ip);
                        // Fast path: a compiled function called with its args already
                        // on the operand stack. Bind them directly into the call
                        // frame instead of materializing a ScriptVar[] per call.
                        var callee = stack[sp - argc - 1];
                        if (callee != null && callee.IsFunction && !callee.IsNative)
                        {
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
                        var argc = ReadOperand(code, ref ip);
                        var callee = stack[sp - argc - 1];
                        if (callee != null && callee.IsFunction && !callee.IsNative)
                        {
                            var receiver = stack[sp - argc - 2];
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
                        // Tail-position direct call: execute the callee and return
                        // its result immediately, so no further opcodes run in this
                        // frame. This keeps the bytecode clean (no dead Return after
                        // the call) and limits C# frame depth by one level.
                        var argc = ReadOperand(code, ref ip);
                        var callee = stack[sp - argc - 1];
                        if (callee != null && callee.IsFunction && !callee.IsNative)
                        {
                            var result = InvokeVmFunctionFromStack(callee, null, argc);
                            sp--; // discard the callee slot
                            return result ?? new ScriptVar(ScriptVar.Flags.Undefined);
                        }
                        else
                        {
                            var args = PopArgs(argc);
                            return InvokeCallable(Pop(), null, args) ?? new ScriptVar(ScriptVar.Flags.Undefined);
                        }
                    }
                    case OpCode.TailCallMethod:
                    {
                        // Tail-position method call: same as TailCall but with a receiver.
                        var argc = ReadOperand(code, ref ip);
                        var callee = stack[sp - argc - 1];
                        if (callee != null && callee.IsFunction && !callee.IsNative)
                        {
                            var receiver = stack[sp - argc - 2];
                            var result = InvokeVmFunctionFromStack(callee, receiver, argc);
                            sp -= 2; // discard callee and receiver slots
                            return result ?? new ScriptVar(ScriptVar.Flags.Undefined);
                        }
                        else
                        {
                            var args = PopArgs(argc);
                            var c = Pop();
                            var receiver = Pop();
                            return InvokeCallable(c, receiver, args) ?? new ScriptVar(ScriptVar.Flags.Undefined);
                        }
                    }
                    case OpCode.Yield:
                    {
                        if (genObj == null)
                            throw new ScriptException("yield used outside generator");
                        var yieldVal = Pop();
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
                        // If it's an array, wrap it in an index-based iterator
                        if (iterable.IsArray)
                        {
                            var idx = new[] { 0 };
                            var len = iterable.GetArrayLength();
                            var iterObj = new ScriptVar(ScriptVar.Flags.Object);
                            var nextFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
                            nextFn.SetCallback((scope, _) =>
                            {
                                var result = new ScriptVar(ScriptVar.Flags.Object);
                                if (idx[0] < len)
                                {
                                    result.AddChild("value", iterable.GetArrayIndex(idx[0]++));
                                    result.AddChild("done", new ScriptVar(false));
                                }
                                else
                                {
                                    result.AddChild("value", new ScriptVar());
                                    result.AddChild("done", new ScriptVar(true));
                                }
                                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(result);
                            }, null);
                            iterObj.AddChild("next", nextFn);
                            Push(iterObj);
                            break;
                        }
                        // Unknown — return an immediately-done iterator
                        {
                            var doneIter = new ScriptVar(ScriptVar.Flags.Object);
                            var doneFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
                            doneFn.SetCallback((scope, _) =>
                            {
                                var result = new ScriptVar(ScriptVar.Flags.Object);
                                result.AddChild("value", new ScriptVar());
                                result.AddChild("done", new ScriptVar(true));
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
                        var nextLink = iter.FindChild("next");
                        ScriptVar result;
                        if (nextLink != null)
                            result = InvokeCallable(nextLink.Var, iter, Array.Empty<ScriptVar>());
                        else
                            result = null;
                        var done = result?.FindChild("done")?.Var.Bool ?? true;
                        if (done) { ip = exitOffset; break; }
                        Push(result.FindChild("value")?.Var ?? new ScriptVar(ScriptVar.Flags.Undefined));
                        break;
                    }
                    case OpCode.New:
                    {
                        var argc = ReadOperand(code, ref ip);
                        var ctor = stack[sp - argc - 1];
                        // Fast path: compiled constructor with args on the stack —
                        // bind them directly into the call frame (no ScriptVar[]).
                        if (ctor != null && ctor.IsFunction && !ctor.IsNative)
                        {
                            var instance = new ScriptVar(ScriptVar.Flags.Object);
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
                        var elems = ExtractArrayElements(spreadArr);
                        var existingLen = arr.IsArray ? arr.GetArrayLength() : 0;
                        for (var si = 0; si < elems.Length; si++)
                            arr.SetArrayIndex(existingLen + si, elems[si]);
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
                        var args = ExtractArrayElements(argsArr);
                        Push(InvokeCallable(callee, null, args));
                        break;
                    }
                    case OpCode.CallMethodSpread:
                    {
                        var argsArr = Pop();
                        var callee = Pop();
                        var receiver = Pop();
                        var args = ExtractArrayElements(argsArr);
                        Push(InvokeCallable(callee, receiver, args));
                        break;
                    }
                    case OpCode.NewSpread:
                    {
                        var argsArr = Pop();
                        var ctor = Pop();
                        var args = ExtractArrayElements(argsArr);
                        Push(Construct(ctor, args));
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
                        env = new Environment(new ScriptVar(ScriptVar.Flags.Object), env, isBlockScope: true);
                        break;
                    }
                    case OpCode.LeaveBlock:
                    {
                        env = blockEnvStack.Pop();
                        break;
                    }

                    case OpCode.Return:
                        return Pop();
                    case OpCode.Halt:
                        return null;

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

            return null;

            } // end outer try
            finally
            {
                // Clean up any handler frames belonging to this Execute() invocation
                // that were not closed normally (e.g. `return` inside try without finally).
                while (tryStack.Count > savedTryDepth) tryStack.Pop();
                // Clear pending-return state if this frame is unwinding due to an exception
                // so it does not bleed into an enclosing Execute() invocation.
                hasPendingReturn = false;
                // Pop the debugger call frame.
                if (callFrame != null) _callFrames.RemoveAt(_callFrames.Count - 1);
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
                        link.ReplaceWith(ex.VarObj ?? new ScriptVar(ScriptVar.Flags.Undefined));
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
        public ScriptVar InvokeCallable(ScriptVar callee, ScriptVar thisArg, ScriptVar[] args)
        {
            if (callee == null || !callee.IsFunction)
            {
                throw new ScriptException("Value is not a function");
            }

            if (callee.IsNative)
            {
                var scope = new ScriptVar(ScriptVar.Flags.Function);
                if (thisArg != null) scope.AddChildNoDup("this", thisArg);

                var p = callee.FirstChild;
                var i = 0;
                while (p != null)
                {
                    scope.AddChild(p.Name, BindArg(args, i));
                    i++;
                    p = p.Next;
                }

                var returnLink = scope.AddChild(ScriptVar.ReturnVarName, null);
                callee.GetCallback()?.Invoke(scope, callee.GetCallbackUserData());
                return returnLink.Var;
            }

            var vmfn = (VmFunction)callee.GetData();

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
            var vars = recyclable ? BorrowFrameVars() : new ScriptVar(ScriptVar.Flags.Object);
            var callEnv = new Environment(vars, vmfn.Captured);
            if (thisArg != null) vars.AddChildNoDup("this", thisArg);

            var parameters = vmfn.Body.Parameters;
            var restIdx2 = vmfn.Body.RestParamIndex;
            var paramLimit2 = restIdx2 >= 0 ? restIdx2 : parameters.Count;
            for (var j = 0; j < paramLimit2; j++)
            {
                vars.AddChild(parameters[j], BindArg(args, j));
            }

            // Handle rest parameter
            if (restIdx2 >= 0)
            {
                var restArr = new ScriptVar(ScriptVar.Flags.Array);
                var restLen = 0;
                for (var j = restIdx2; j < (args?.Length ?? 0); j++)
                {
                    restArr.SetArrayIndex(restLen++, BindArg(args, j));
                }
                vars.AddChild(parameters[restIdx2], restArr);
            }

            if (recyclable)
            {
                try { return Execute(vmfn.Body, callEnv) ?? new ScriptVar(ScriptVar.Flags.Undefined); }
                finally { ReturnFrameVars(vars); }
            }

            return Execute(vmfn.Body, callEnv) ?? new ScriptVar(ScriptVar.Flags.Undefined);
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

            // Generator function: materialise args from stack, then return iterator
            if (vmfn.Body.IsGenerator)
            {
                var genArgs = new ScriptVar[argc];
                for (var j = 0; j < argc; j++)
                    genArgs[j] = stack[argBase + j];
                sp = argBase;
                var genCallEnv = BuildCallEnvironment(vmfn, thisArg, genArgs);
                return CreateGeneratorIterator(vmfn, genCallEnv);
            }

            // Async function: materialise args from stack, then return a Promise
            if (vmfn.Body.IsAsync)
            {
                var asyncArgs = new ScriptVar[argc];
                for (var j = 0; j < argc; j++)
                    asyncArgs[j] = stack[argBase + j];
                sp = argBase;
                var asyncCallEnv = BuildCallEnvironment(vmfn, thisArg, asyncArgs);
                return CreateAsyncPromise(vmfn, asyncCallEnv);
            }

            var recyclable = vmfn.Body.RecyclableFrame;
            var vars = recyclable ? BorrowFrameVars() : new ScriptVar(ScriptVar.Flags.Object);
            var callEnv = new Environment(vars, vmfn.Captured);
            if (thisArg != null) vars.AddChildNoDup("this", thisArg);

            var parameters = vmfn.Body.Parameters;
            var restIdx = vmfn.Body.RestParamIndex;
            var paramLimit = restIdx >= 0 ? restIdx : parameters.Count;
            for (var j = 0; j < paramLimit; j++)
            {
                var arg = j < argc ? stack[argBase + j] : null;
                vars.AddChild(parameters[j], BindArgValue(arg));
            }

            // Handle rest parameter
            if (restIdx >= 0)
            {
                var restArr = new ScriptVar(ScriptVar.Flags.Array);
                var restLen = 0;
                for (var j = restIdx; j < argc; j++)
                {
                    restArr.SetArrayIndex(restLen++, BindArgValue(stack[argBase + j]));
                }
                vars.AddChild(parameters[restIdx], restArr);
            }

            // Pop the arguments now that they are bound; the args' slots are free
            // for the callee's own use of the shared operand stack. Their values
            // stay alive via the call frame's child links.
            sp = argBase;

            if (recyclable)
            {
                try { return Execute(vmfn.Body, callEnv) ?? new ScriptVar(ScriptVar.Flags.Undefined); }
                finally { ReturnFrameVars(vars); }
            }

            return Execute(vmfn.Body, callEnv) ?? new ScriptVar(ScriptVar.Flags.Undefined);
        }

        // Build a call environment for a VmFunction from a ScriptVar[] args array.
        // Used by the generator path to capture args before starting the body thread.
        private Environment BuildCallEnvironment(VmFunction vmfn, ScriptVar thisArg, ScriptVar[] args)
        {
            var vars = new ScriptVar(ScriptVar.Flags.Object); // generators never recycle frames
            var env = new Environment(vars, vmfn.Captured);
            if (thisArg != null) vars.AddChildNoDup("this", thisArg);

            var parameters = vmfn.Body.Parameters;
            var restIdx = vmfn.Body.RestParamIndex;
            var paramLimit = restIdx >= 0 ? restIdx : parameters.Count;
            for (var j = 0; j < paramLimit; j++)
                vars.AddChild(parameters[j], BindArg(args, j));

            if (restIdx >= 0)
            {
                var restArr = new ScriptVar(ScriptVar.Flags.Array);
                var restLen = 0;
                for (var j = restIdx; j < (args?.Length ?? 0); j++)
                    restArr.SetArrayIndex(restLen++, BindArg(args, j));
                vars.AddChild(parameters[restIdx], restArr);
            }

            return env;
        }

        // Create an iterator object for a generator function. The iterator has a
        // .next() native method that drives the generator body on a background thread,
        // suspending/resuming via GeneratorObject.
        private ScriptVar CreateGeneratorIterator(VmFunction vmfn, Environment callEnv)
        {
            var genObj = new GeneratorObject();

            var iterObj = new ScriptVar(ScriptVar.Flags.Object);

            var nextFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            // Declare a "value" parameter so callers can pass a resume value via .next(v)
            nextFn.AddChild("value", new ScriptVar());
            nextFn.SetCallback((scope, _) =>
            {
                var inputLink = scope.FindChild("value");
                var input = inputLink?.Var ?? new ScriptVar();

                var (value, done) = genObj.Next(input, g =>
                {
                    var result = Execute(vmfn.Body, callEnv, g);
                    g.Complete(result ?? new ScriptVar());
                });

                var resultObj = new ScriptVar(ScriptVar.Flags.Object);
                resultObj.AddChild("value", value);
                resultObj.AddChild("done", new ScriptVar(done));
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
                        g.Complete(result ?? new ScriptVar(ScriptVar.Flags.Undefined));
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
                        ? (jit.VarObj ?? new ScriptVar(ex.Message))
                        : new ScriptVar(ex.Message);
                    outerPromise.Reject(msg);
                }
            }

            void DriveError(ScriptVar reason)
            {
                // For now: reject the outer promise on any error during resume
                outerPromise.Reject(reason);
            }

            // Start the async function immediately (first .Next call starts the thread).
            MicroTaskQueue.Enqueue(() => DriveNext(new ScriptVar(ScriptVar.Flags.Undefined)));

            return outerPromise.ToScriptVar(this);
        }

        // primitives are passed by value, objects/functions by reference
        private static ScriptVar BindArg(ScriptVar[] args, int index)
        {
            if (args == null || index >= args.Length)
            {
                return new ScriptVar(ScriptVar.Flags.Undefined);
            }

            return BindArgValue(args[index]);
        }

        // Bind a single argument value into a call frame.
        private static ScriptVar BindArgValue(ScriptVar value)
        {
            if (value == null)
            {
                return new ScriptVar(ScriptVar.Flags.Undefined);
            }

            // Objects/arrays/functions are passed by reference.
            if (!value.IsBasic) return value;

            // Primitives are passed by value, so a shared one (held by a variable,
            // hence ref-counted) must be copied to prevent the callee mutating the
            // caller's binding. But a value with no refs is an unaliased temporary
            // freshly produced on the operand stack (e.g. the result of `n - 1` in
            // `fib(n - 1)`): nothing else can observe it, so binding it directly is
            // safe and skips a DeepCopy allocation on the hot call path.
            return value.GetRefs() == 0 ? value : value.DeepCopy();
        }

        private ScriptVar Construct(ScriptVar ctor, ScriptVar[] args)
        {
            var instance = new ScriptVar(ScriptVar.Flags.Object);

            // link the instance to its constructor so shared members resolve
            instance.AddChild(ScriptVar.PrototypeClassName, ctor);

            if (ctor.IsFunction)
            {
                var result = InvokeCallable(ctor, instance, args);
                // a constructor that returns an object replaces the instance
                if (result != null && result.IsObject) return result;
            }

            return instance;
        }

        private ScriptVar GetMember(ScriptVar obj, string name)
        {
            var link = obj.FindChild(name);
            if (link == null && engine != null)
            {
                link = engine.FindInParentClasses(obj, name);
            }
            if (link != null) return link.Var;

            if (obj.IsArray && name == "length") return new ScriptVar(obj.GetArrayLength());
            if (obj.IsString && name == "length") return new ScriptVar(obj.String.Length);

            return new ScriptVar(ScriptVar.Flags.Undefined);
        }

        private static void SetMember(ScriptVar obj, string name, ScriptVar value)
        {
            // Assignment always targets an OWN property (FindChild searches own
            // members only), so inherited members are shadowed, never mutated.
            var link = obj.FindChild(name);
            if (link != null)
            {
                link.ReplaceWith(value);
            }
            else
            {
                obj.AddChild(name, value);
            }
        }

        private static void DeleteMember(ScriptVar obj, string name)
        {
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
        private static string KeyName(ScriptVar key)
        {
            return key.IsInt ? ScriptVar.IndexName(key.Int) : key.String;
        }

        // Single-pass O(n) extraction of array elements into a flat ScriptVar[].
        // Replaces the previous per-element FindChild(IndexName(i)) pattern which
        // was O(n²) because each FindChild walk scans from the head of the list.
        private static ScriptVar[] ExtractArrayElements(ScriptVar arr)
        {
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
                result[i] ??= new ScriptVar(ScriptVar.Flags.Undefined);
            return result;
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
        private static bool IntBinary(int a, int b, ScriptLex.LexTypes op, out ScriptVar result)
        {
            switch ((char)op)
            {
                case '+': result = new ScriptVar(a + b); return true;
                case '-': result = new ScriptVar(a - b); return true;
                case '*': result = new ScriptVar(a * b); return true;
                case '/': result = b == 0 ? new ScriptVar((double)a / b) : new ScriptVar(a / b); return true;
                case '&': result = new ScriptVar(a & b); return true;
                case '|': result = new ScriptVar(a | b); return true;
                case '^': result = new ScriptVar(a ^ b); return true;
                case '%': result = b == 0 ? new ScriptVar(double.NaN) : new ScriptVar(a % b); return true;
                case (char)ScriptLex.LexTypes.Equal: result = new ScriptVar(a == b); return true;
                case (char)ScriptLex.LexTypes.NEqual: result = new ScriptVar(a != b); return true;
                case '<': result = new ScriptVar(a < b); return true;
                case (char)ScriptLex.LexTypes.LEqual: result = new ScriptVar(a <= b); return true;
                case '>': result = new ScriptVar(a > b); return true;
                case (char)ScriptLex.LexTypes.GEqual: result = new ScriptVar(a >= b); return true;
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
                case ScriptLex.LexTypes.LShift: return new ScriptVar(a.Int << shift);
                case ScriptLex.LexTypes.RShift: return new ScriptVar(a.Int >> shift);
                case ScriptLex.LexTypes.RShiftUnsigned: return new ScriptVar(a.Int >>> shift);
                default: throw new ScriptException("Unsupported shift operator");
            }
        }

        private static ScriptVar CoerceToNumber(ScriptVar value)
        {
            if (value.IsInt) return new ScriptVar(value.Int);
            if (value.IsDouble) return new ScriptVar(value.Float);
            if (value.IsNull) return new ScriptVar(0);

            if (value.IsString &&
                double.TryParse(value.String, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return new ScriptVar(parsed);
            }

            return new ScriptVar(double.NaN);
        }
    }
}
