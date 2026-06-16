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
using System.Globalization;

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

        public VirtualMachine() { }

        public VirtualMachine(ScriptEngine engine)
        {
            this.engine = engine;
        }

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

        private ScriptVar Execute(Chunk chunk, Environment env)
        {
            var code = chunk.CodeBytes;
            // Hoist the inline-cache array once so each GetVar/SetVar avoids the
            // property's lazy null-check on every resolution.
            var cache = chunk.InlineCache;
            var ip = 0;

            while (ip < code.Length)
            {
                var op = (OpCode)code[ip];
                ip++;

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
                        env.Vars.FindChildOrCreate(name);
                        env.Version++; // a new binding may shadow a cached outer resolution
                        break;
                    }
                    case OpCode.DeclareConst:
                    {
                        var name = chunk.Names[ReadOperand(code, ref ip)];
                        env.Vars.FindChildOrCreate(name, ScriptVar.Flags.Undefined, readOnly: true);
                        env.Version++; // a new binding may shadow a cached outer resolution
                        break;
                    }

                    case OpCode.GetProp:
                    {
                        var name = chunk.Names[ReadOperand(code, ref ip)];
                        var obj = Pop();
                        Push(GetMember(obj, name));
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
                        Push(a.MathsOp(b, operatorCode));
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
                    case OpCode.New:
                    {
                        var args = PopArgs(ReadOperand(code, ref ip));
                        var ctor = Pop();
                        Push(Construct(ctor, args));
                        break;
                    }

                    case OpCode.Throw:
                        throw new JITException(Pop());
                    case OpCode.Try:
                        ExecuteTry(chunk, code, env, ref ip);
                        break;

                    case OpCode.Return:
                        return Pop();
                    case OpCode.Halt:
                        return null;

                    default:
                        throw new ScriptException($"VM opcode not yet implemented: {op}");
                }
            }

            return null;
        }

        // Runs a try/catch/finally whose three bodies are compiled as nested
        // chunks. `throw` surfaces as a JITException that propagates across VM
        // frames (C# call stack), so the C# try/catch/finally here mirrors the
        // tree-walking engine's behaviour exactly.
        private void ExecuteTry(Chunk chunk, byte[] code, Environment env, ref int ip)
        {
            var tryIndex = ReadOperand(code, ref ip);
            var catchIndex = ReadOperand(code, ref ip);
            var finallyIndex = ReadOperand(code, ref ip);
            var catchParamIndex = ReadOperand(code, ref ip);

            var tryChunk = chunk.Functions[tryIndex];
            var catchChunk = catchIndex >= 0 ? chunk.Functions[catchIndex] : null;
            var finallyChunk = finallyIndex >= 0 ? chunk.Functions[finallyIndex] : null;

            JITException pending = null;
            try
            {
                Execute(tryChunk, env);
            }
            catch (JITException ex)
            {
                if (catchChunk != null)
                {
                    var catchEnv = new Environment(new ScriptVar(ScriptVar.Flags.Object), env);
                    if (catchParamIndex >= 0)
                    {
                        catchEnv.Vars.AddChild(chunk.Names[catchParamIndex], ex.VarObj);
                    }
                    Execute(catchChunk, catchEnv);
                }
                else
                {
                    pending = ex; // no catch: rethrow after finally
                }
            }
            finally
            {
                if (finallyChunk != null)
                {
                    Execute(finallyChunk, env);
                }
            }

            if (pending != null) throw pending;
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
            var callEnv = new Environment(new ScriptVar(ScriptVar.Flags.Object), vmfn.Captured);
            if (thisArg != null) callEnv.Vars.AddChildNoDup("this", thisArg);

            var parameters = vmfn.Body.Parameters;
            for (var j = 0; j < parameters.Count; j++)
            {
                callEnv.Vars.AddChild(parameters[j], BindArg(args, j));
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
            var callEnv = new Environment(new ScriptVar(ScriptVar.Flags.Object), vmfn.Captured);
            if (thisArg != null) callEnv.Vars.AddChildNoDup("this", thisArg);

            var parameters = vmfn.Body.Parameters;
            for (var j = 0; j < parameters.Count; j++)
            {
                var arg = j < argc ? stack[argBase + j] : null;
                callEnv.Vars.AddChild(parameters[j], BindArgValue(arg));
            }

            // Pop the arguments now that they are bound; the args' slots are free
            // for the callee's own use of the shared operand stack. Their values
            // stay alive via the call frame's child links.
            sp = argBase;

            return Execute(vmfn.Body, callEnv) ?? new ScriptVar(ScriptVar.Flags.Undefined);
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
