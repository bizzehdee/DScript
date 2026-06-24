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
using System.Reflection.Emit;
using DScript.Vm;
using OpCode = DScript.Vm.OpCode;

namespace DScript.Jit
{
    /// <summary>
    /// A first-tier JIT back-end built on <see cref="System.Reflection.Emit"/>.
    ///
    /// It compiles straight-line, branch-free leaf functions whose bodies are made
    /// up of constant loads, variable reads (resolved through the full lexical scope
    /// chain), and arithmetic — exactly the shape the binary-op type profiles target.
    /// Any chunk containing control flow, calls,
    /// assignments, or other unsupported opcodes is declined (Compile returns
    /// <c>null</c>) and the VM keeps interpreting it. Because the compiled code
    /// mirrors the interpreter's operand semantics (including its int fast path and
    /// <c>MathsOp</c> fallback) the JIT output is value-identical to interpretation.
    /// </summary>
    public sealed class ReflectionEmitJitCompiler : IJitCompiler
    {
        public JitDelegate Compile(Chunk chunk)
        {
            try
            {
                return CompileCore(chunk);
            }
            catch (NotSupportedException)
            {
                // An unsupported construct was hit mid-walk: decline this chunk.
                return null;
            }
        }

        private static JitDelegate CompileCore(Chunk chunk)
        {
            var b = new DynamicMethodBuilder(chunk.Name ?? "anon");

            // Two scratch slots for binary operands and one for IntBinary's out value.
            // Each binary op fully consumes them before the next, so they are reused.
            var aSlot = b.DeclareLocal(typeof(ScriptVar));
            var bSlot = b.DeclareLocal(typeof(ScriptVar));
            var rSlot = b.DeclareLocal(typeof(ScriptVar));
            var argArr = b.DeclareLocal(typeof(ScriptVar[]));

            var code = chunk.CodeBytes;
            var ip = 0;
            var emittedTerminalReturn = false;

            while (ip < code.Length)
            {
                if (emittedTerminalReturn)
                    // Straight-line code should have a single trailing return; anything
                    // after it implies control flow we do not model.
                    throw new NotSupportedException();

                var op = (OpCode)code[ip];
                ip++;

                switch (op)
                {
                    case OpCode.Constant:
                        b.EmitMaterializeConstant(chunk.Constants[chunk.ReadInt(ip)]);
                        ip += 4;
                        break;
                    case OpCode.ConstantN:
                        b.EmitMaterializeConstant(chunk.Constants[code[ip]]);
                        ip += 1;
                        break;

                    case OpCode.GetVar:
                        b.EmitLoadNamedVar(chunk.Names[chunk.ReadInt(ip)]);
                        ip += 4;
                        break;
                    case OpCode.GetVarN:
                        b.EmitLoadNamedVar(chunk.Names[code[ip]]);
                        ip += 1;
                        break;

                    case OpCode.PushUndefined: b.EmitPushUndefined(); break;
                    case OpCode.PushNull:      b.EmitPushNull(); break;
                    case OpCode.PushTrue:      b.EmitPushIntConst(1); break;
                    case OpCode.PushFalse:     b.EmitPushIntConst(0); break;

                    case OpCode.Pop:
                        b.IL.Emit(OpCodes.Pop);
                        break;

                    case OpCode.Binary:
                        EmitBinary(b, (ScriptLex.LexTypes)chunk.ReadInt(ip), aSlot, bSlot, rSlot);
                        ip += 4;
                        break;
                    case OpCode.BinaryConst:
                    {
                        var operatorCode = (ScriptLex.LexTypes)chunk.ReadInt(ip);
                        var constant = chunk.Constants[chunk.ReadInt(ip + 4)];
                        b.EmitMaterializeConstant(constant);     // push right operand
                        EmitBinary(b, operatorCode, aSlot, bSlot, rSlot);
                        ip += 8;
                        break;
                    }
                    case OpCode.BinaryIntConst:
                    {
                        var operatorCode = (ScriptLex.LexTypes)chunk.ReadInt(ip);
                        var intValue = chunk.ReadInt(ip + 4);
                        b.EmitPushIntConst(intValue);            // push right operand
                        EmitBinary(b, operatorCode, aSlot, bSlot, rSlot);
                        ip += 8;
                        break;
                    }
                    case OpCode.GetVarGetVarBinary:
                    {
                        var operatorCode = (ScriptLex.LexTypes)chunk.ReadInt(ip);
                        b.EmitLoadNamedVar(chunk.Names[chunk.ReadInt(ip + 4)]);
                        b.EmitLoadNamedVar(chunk.Names[chunk.ReadInt(ip + 8)]);
                        EmitBinary(b, operatorCode, aSlot, bSlot, rSlot);
                        ip += 12;
                        break;
                    }
                    case OpCode.GetVarGetVarBinaryN:
                    {
                        var operatorCode = (ScriptLex.LexTypes)code[ip];
                        b.EmitLoadNamedVar(chunk.Names[code[ip + 1]]);
                        b.EmitLoadNamedVar(chunk.Names[code[ip + 2]]);
                        EmitBinary(b, operatorCode, aSlot, bSlot, rSlot);
                        ip += 3;
                        break;
                    }

                    case OpCode.Call:
                        EmitCall(b, chunk, ip /* site = operand-start */,
                                 chunk.ReadInt(ip), aSlot, bSlot, argArr);
                        ip += 4;
                        break;

                    case OpCode.Return:
                        b.IL.Emit(OpCodes.Ret);  // returns the ScriptVar on top of stack
                        emittedTerminalReturn = true;
                        break;
                    case OpCode.Halt:
                        b.EmitPushUndefined();
                        b.IL.Emit(OpCodes.Ret);
                        emittedTerminalReturn = true;
                        break;

                    default:
                        // Jumps, calls, assignments, object/array ops, try, etc.
                        throw new NotSupportedException();
                }
            }

            if (!emittedTerminalReturn)
                b.EmitPushUndefined(); // fall-through: function returns undefined

            return b.Finish();
        }

        // Emit a plain (non-tail) call. The callee and its argc arguments are on the
        // IL stack as [callee, arg0, ..., arg{argc-1}] (top = last arg), matching the
        // interpreter's Call layout. Tail and method calls are declined elsewhere.
        //
        // Monomorphic sites (T14): bake the single observed callee, guard the runtime
        // callee against it, and dispatch through vm.InvokeCallable. A guard miss
        // falls through to the same general dispatch on the runtime callee.
        // Bimorphic/megamorphic/unobserved sites (T15): dispatch on the runtime
        // callee directly, with no baked guard.
        private static void EmitCall(DynamicMethodBuilder b, Chunk chunk, int site, int argc,
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

            var profile = chunk.CallProfiles[site];
            if (profile.State != Chunk.CallSiteMorphism.Monomorphic || profile.Callee0 == null)
            {
                // Megamorphic fallback (T15): bimorphic, megamorphic, and unobserved
                // sites dispatch on the runtime callee with no baked guard.
                EmitGeneralCall(b, calleeSlot, argArr);
                return;
            }

            var bakedIndex = b.AddData(profile.Callee0);
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
