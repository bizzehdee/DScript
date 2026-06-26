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
    public sealed class ClosureThreadedJitCompiler : IJitCompiler
    {
        public JitDelegate Compile(Chunk chunk)
        {
            var instrs = JitDecoder.Decode(chunk);
            if (instrs == null)
                return null; // declined by the shared front-end

            // The closure back-end models expressions, not arbitrary control flow;
            // decline any chunk containing branches (handled by the Reflection.Emit
            // back-end only).
            foreach (var instr in instrs)
                if (instr.Kind is JitOpKind.Jump or JitOpKind.JumpIfFalse or JitOpKind.JumpIfTrue
                    or JitOpKind.JumpIfFalseOrPop or JitOpKind.JumpIfTrueOrPop
                    or JitOpKind.JumpIfNullOrUndefined or JitOpKind.JumpIfDefined
                    or JitOpKind.GetPropMethod or JitOpKind.GetPropCall0 or JitOpKind.CallMethod
                    or JitOpKind.EnterBlock or JitOpKind.LeaveBlock)
                    return null;

            // Build a tree of value-producing nodes. Expression statements (Pop) are
            // collected as side-effecting nodes to run, in order, before the result.
            var strict = chunk.IsStrict;
            var stack = new Stack<JitDelegate>();
            var effects = new List<JitDelegate>();
            JitDelegate result = null;

            foreach (var instr in instrs)
            {
                switch (instr.Kind)
                {
                    case JitOpKind.PushConst:      stack.Push(ConstNode(instr.Constant)); break;
                    case JitOpKind.PushIntLiteral: stack.Push(IntLitNode(instr.IntValue)); break;
                    case JitOpKind.PushVar:        stack.Push(VarNode(instr.Name)); break;
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
                        stack.Push(CallNode(callee, argNodes));
                        break;
                    }
                    case JitOpKind.SetVar:        stack.Push(SetVarNode(instr.Name, stack.Pop(), strict)); break;
                    case JitOpKind.SetVarPop:     effects.Add(SetVarNode(instr.Name, stack.Pop(), strict)); break;
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
                        effects.Add(SetPropNode(obj, instr.Name, value, strict));
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
                    case JitOpKind.DeclareVar:    effects.Add(DeclareNode(instr.Name, JitDeclareKind.Var)); break;
                    case JitOpKind.DeclareLocal:  effects.Add(DeclareNode(instr.Name, JitDeclareKind.Local)); break;
                    case JitOpKind.DeclareConst:  effects.Add(DeclareNode(instr.Name, JitDeclareKind.Const)); break;
                    case JitOpKind.Pop:     effects.Add(stack.Pop()); break;
                    case JitOpKind.Return:  result = stack.Pop(); break;
                }
            }

            return FinishNode(effects.ToArray(), result);
        }

        // Each factory captures only its own parameters, so closures built in a loop
        // never share mutable state.

        private static JitDelegate ConstNode(ConstantValue c) => (vm, args, env) => c.Materialize();

        private static JitDelegate IntLitNode(int v) => (vm, args, env) => ScriptVar.FromInt(v);

        private static JitDelegate VarNode(string name) => (vm, args, env) => VirtualMachine.JitGetVar(env, name);

        private static JitDelegate GetPropNode(JitDelegate obj, string name)
        {
            var cell = new PropCacheCell(); // one inline-cache cell per site
            return (vm, args, env) => vm.JitGetPropCached(obj(vm, args, env), name, cell);
        }

        private static JitDelegate SetVarNode(string name, JitDelegate value, bool strict) =>
            (vm, args, env) =>
            {
                var v = value(vm, args, env);
                VirtualMachine.JitSetVar(env, name, v, strict);
                return v;
            };

        private static JitDelegate SetPropNode(JitDelegate obj, string name, JitDelegate value, bool strict) =>
            (vm, args, env) =>
            {
                var o = obj(vm, args, env);     // object evaluated before value (push order)
                var v = value(vm, args, env);
                vm.JitSetProp(o, name, v, strict);
                return v;
            };

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

        private static JitDelegate FinishNode(JitDelegate[] effects, JitDelegate result) =>
            (vm, args, env) =>
            {
                for (var i = 0; i < effects.Length; i++)
                    effects[i](vm, args, env);
                return result != null ? result(vm, args, env) : ScriptVar.CreateUndefined();
            };
    }
}
