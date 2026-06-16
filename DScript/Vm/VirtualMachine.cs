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
        private readonly List<ScriptVar> stack = [];

        public VirtualMachine() { }

        public VirtualMachine(ScriptEngine engine)
        {
            this.engine = engine;
        }

        private void Push(ScriptVar value) => stack.Add(value);

        private ScriptVar Pop()
        {
            var top = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            return top;
        }

        private ScriptVar Peek() => stack[stack.Count - 1];

        /// <summary>
        /// Execute a top-level chunk and return the produced value (the operand
        /// of a <see cref="OpCode.Return"/>, or undefined when the chunk halts).
        /// </summary>
        public ScriptVar Run(Chunk chunk)
        {
            return Run(chunk, new Environment(new ScriptVar(null, ScriptVar.Flags.Object), null));
        }

        public ScriptVar Run(Chunk chunk, Environment env)
        {
            var startDepth = stack.Count;
            var result = Execute(chunk, env);

            // discard anything the chunk left behind to keep the stack balanced
            while (stack.Count > startDepth)
            {
                Pop();
            }

            return result ?? new ScriptVar(null, ScriptVar.Flags.Undefined);
        }

        private ScriptVar Execute(Chunk chunk, Environment env)
        {
            var code = chunk.Code;
            var ip = 0;

            while (ip < code.Count)
            {
                var op = (OpCode)code[ip];
                ip++;

                switch (op)
                {
                    case OpCode.Constant:
                        Push(chunk.Constants[ReadOperand(chunk, ref ip)].Materialize());
                        break;
                    case OpCode.PushUndefined:
                        Push(new ScriptVar(null, ScriptVar.Flags.Undefined));
                        break;
                    case OpCode.PushNull:
                        Push(new ScriptVar(null, ScriptVar.Flags.Null));
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
                        var b = stack[stack.Count - 1];
                        var a = stack[stack.Count - 2];
                        Push(a);
                        Push(b);
                        break;
                    }
                    case OpCode.EnumKeys:
                    {
                        var obj = Pop();
                        var keys = new ScriptVar(null, ScriptVar.Flags.Array);
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
                        var name = chunk.Names[ReadOperand(chunk, ref ip)];
                        var link = env.Resolve(name);
                        Push(link != null ? link.Var : new ScriptVar(null, ScriptVar.Flags.Undefined));
                        break;
                    }
                    case OpCode.SetVar:
                    {
                        var name = chunk.Names[ReadOperand(chunk, ref ip)];
                        var value = Pop();
                        var link = env.Resolve(name);
                        if (link != null)
                        {
                            link.ReplaceWith(value);
                        }
                        else
                        {
                            env.Global().Vars.AddChildNoDup(name, value);
                        }
                        Push(value); // assignment is an expression
                        break;
                    }
                    case OpCode.DeclareVar:
                    {
                        var name = chunk.Names[ReadOperand(chunk, ref ip)];
                        env.Vars.FindChildOrCreate(name);
                        break;
                    }
                    case OpCode.DeclareConst:
                    {
                        var name = chunk.Names[ReadOperand(chunk, ref ip)];
                        env.Vars.FindChildOrCreate(name, ScriptVar.Flags.Undefined, readOnly: true);
                        break;
                    }

                    case OpCode.GetProp:
                    {
                        var name = chunk.Names[ReadOperand(chunk, ref ip)];
                        var obj = Pop();
                        Push(GetMember(obj, name));
                        break;
                    }
                    case OpCode.SetProp:
                    {
                        var name = chunk.Names[ReadOperand(chunk, ref ip)];
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
                        Push(GetMember(obj, key.String));
                        break;
                    }
                    case OpCode.SetIndex:
                    {
                        var value = Pop();
                        var key = Pop();
                        var obj = Pop();
                        SetMember(obj, key.String, value);
                        Push(value);
                        break;
                    }
                    case OpCode.DeleteProp:
                    {
                        var name = chunk.Names[ReadOperand(chunk, ref ip)];
                        DeleteMember(Pop(), name);
                        Push(new ScriptVar(true));
                        break;
                    }
                    case OpCode.DeleteIndex:
                    {
                        var key = Pop();
                        DeleteMember(Pop(), key.String);
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
                        var operatorCode = (ScriptLex.LexTypes)ReadOperand(chunk, ref ip);
                        var b = Pop();
                        var a = Pop();
                        Push(a.MathsOp(b, operatorCode));
                        break;
                    }
                    case OpCode.Shift:
                    {
                        var operatorCode = (ScriptLex.LexTypes)ReadOperand(chunk, ref ip);
                        var b = Pop();
                        var a = Pop();
                        Push(ApplyShift(a, b, operatorCode));
                        break;
                    }
                    case OpCode.Negate:
                    {
                        var a = Pop();
                        Push(new ScriptVar(0).MathsOp(a, (ScriptLex.LexTypes)'-'));
                        break;
                    }
                    case OpCode.Not:
                    {
                        var a = Pop();
                        Push(a.MathsOp(new ScriptVar(0), ScriptLex.LexTypes.Equal));
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
                        Push(new ScriptVar(null, ScriptVar.Flags.Object));
                        break;
                    case OpCode.NewArray:
                        Push(new ScriptVar(null, ScriptVar.Flags.Array));
                        break;
                    case OpCode.InitProp:
                    {
                        var name = chunk.Names[ReadOperand(chunk, ref ip)];
                        var value = Pop();
                        Peek().AddChild(name, value); // object kept on stack
                        break;
                    }
                    case OpCode.InitElem:
                    {
                        var index = ReadOperand(chunk, ref ip);
                        var value = Pop();
                        Peek().SetArrayIndex(index, value); // array kept on stack
                        break;
                    }

                    case OpCode.Jump:
                        ip = ReadOperand(chunk, ref ip);
                        break;
                    case OpCode.JumpIfFalse:
                    {
                        var target = ReadOperand(chunk, ref ip);
                        if (!Pop().Bool) ip = target;
                        break;
                    }
                    case OpCode.JumpIfTrue:
                    {
                        var target = ReadOperand(chunk, ref ip);
                        if (Pop().Bool) ip = target;
                        break;
                    }
                    case OpCode.JumpIfFalseOrPop:
                    {
                        var target = ReadOperand(chunk, ref ip);
                        if (!Peek().Bool) ip = target; else Pop();
                        break;
                    }
                    case OpCode.JumpIfTrueOrPop:
                    {
                        var target = ReadOperand(chunk, ref ip);
                        if (Peek().Bool) ip = target; else Pop();
                        break;
                    }

                    case OpCode.MakeClosure:
                    {
                        var fnChunk = chunk.Functions[ReadOperand(chunk, ref ip)];
                        var fn = new ScriptVar(null, ScriptVar.Flags.Function);
                        fn.SetData(new VmFunction(fnChunk, env));
                        Push(fn);
                        break;
                    }
                    case OpCode.Call:
                    {
                        var args = PopArgs(ReadOperand(chunk, ref ip));
                        var callee = Pop();
                        Push(InvokeCallable(callee, null, args));
                        break;
                    }
                    case OpCode.CallMethod:
                    {
                        var args = PopArgs(ReadOperand(chunk, ref ip));
                        var callee = Pop();
                        var receiver = Pop();
                        Push(InvokeCallable(callee, receiver, args));
                        break;
                    }
                    case OpCode.New:
                    {
                        var args = PopArgs(ReadOperand(chunk, ref ip));
                        var ctor = Pop();
                        Push(Construct(ctor, args));
                        break;
                    }

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
                var scope = new ScriptVar(null, ScriptVar.Flags.Function);
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
            var callEnv = new Environment(new ScriptVar(null, ScriptVar.Flags.Object), vmfn.Captured);
            if (thisArg != null) callEnv.Vars.AddChildNoDup("this", thisArg);

            var parameters = vmfn.Body.Parameters;
            for (var j = 0; j < parameters.Count; j++)
            {
                callEnv.Vars.AddChild(parameters[j], BindArg(args, j));
            }

            return Execute(vmfn.Body, callEnv) ?? new ScriptVar(null, ScriptVar.Flags.Undefined);
        }

        // primitives are passed by value, objects/functions by reference
        private static ScriptVar BindArg(ScriptVar[] args, int index)
        {
            if (args == null || index >= args.Length || args[index] == null)
            {
                return new ScriptVar(null, ScriptVar.Flags.Undefined);
            }

            var value = args[index];
            return value.IsBasic ? value.DeepCopy() : value;
        }

        private ScriptVar Construct(ScriptVar ctor, ScriptVar[] args)
        {
            var instance = new ScriptVar(null, ScriptVar.Flags.Object);

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

            return new ScriptVar(null, ScriptVar.Flags.Undefined);
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

        private static int ReadOperand(Chunk chunk, ref int ip)
        {
            var value = chunk.ReadInt(ip);
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
