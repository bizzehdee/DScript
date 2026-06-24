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

            // Build a tree of value-producing nodes. Expression statements (Pop) are
            // collected as side-effecting nodes to run, in order, before the result.
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

        private static JitDelegate NullNode() => (vm, args, env) => ScriptVar.CreateNull();

        private static JitDelegate UndefinedNode() => (vm, args, env) => ScriptVar.CreateUndefined();

        private static JitDelegate NotNode(JitDelegate operand) =>
            (vm, args, env) => ScriptVar.FromInt(operand(vm, args, env).Bool ? 0 : 1);

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
