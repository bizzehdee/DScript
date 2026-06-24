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
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using DScript.Vm;
using Environment = DScript.Vm.Environment;

namespace DScript.Jit
{
    /// <summary>
    /// Thin wrapper over <see cref="ILGenerator"/> for emitting a JIT-compiled
    /// chunk body. It hides the boilerplate of the chosen calling convention and
    /// the reflection handles for the <see cref="ScriptVar"/> operations the
    /// emitted code calls.
    ///
    /// The underlying <see cref="DynamicMethod"/> has signature
    /// <c>ScriptVar M(object[] data, VirtualMachine vm, ScriptVar[] args, Environment env)</c>.
    /// The leading <c>data</c> array carries compile-time-baked references (constant
    /// pool entries, name strings); it is bound away with
    /// <see cref="DynamicMethod.CreateDelegate(Type, object)"/> so the finished
    /// delegate matches <see cref="JitDelegate"/>'s <c>(vm, args, scope)</c> shape.
    /// </summary>
    internal sealed class DynamicMethodBuilder
    {
        // Argument slots of the underlying DynamicMethod.
        private const int ArgData = 0;
        private const int ArgVm   = 1;
        private const int ArgArgs = 2;
        private const int ArgEnv  = 3;

        // Reflection handles for the operations emitted code calls.
        private static readonly MethodInfo IsIntGetter      = Prop(typeof(ScriptVar), "IsInt");
        private static readonly MethodInfo IsDoubleGetter   = Prop(typeof(ScriptVar), "IsDouble");
        private static readonly MethodInfo IsStringGetter   = Prop(typeof(ScriptVar), "IsString");
        private static readonly MethodInfo IntGetter        = Prop(typeof(ScriptVar), "Int");
        private static readonly MethodInfo BoolGetter        = Prop(typeof(ScriptVar), "Bool");
        private static readonly MethodInfo FloatGetter      = Prop(typeof(ScriptVar), "Float");
        private static readonly MethodInfo FromIntMethod    = typeof(ScriptVar).GetMethod("FromInt", new[] { typeof(int) });
        private static readonly MethodInfo FromDoubleMethod = typeof(ScriptVar).GetMethod("FromDouble", new[] { typeof(double) });
        private static readonly MethodInfo FromStringMethod = typeof(ScriptVar).GetMethod("FromString", new[] { typeof(string) });
        private static readonly MethodInfo CreateUndefinedMethod = typeof(ScriptVar).GetMethod("CreateUndefined", Type.EmptyTypes);
        private static readonly MethodInfo CreateNullMethod = typeof(ScriptVar).GetMethod("CreateNull", Type.EmptyTypes);
        private static readonly MethodInfo StringGetter     = Prop(typeof(ScriptVar), "String");
        private static readonly MethodInfo ConcatMethod     = typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string) });
        private static readonly MethodInfo MathsOpMethod    = typeof(ScriptVar).GetMethod("MathsOp", new[] { typeof(ScriptVar), typeof(ScriptLex.LexTypes) });
        private static readonly MethodInfo JitGetVarMethod  = typeof(VirtualMachine).GetMethod(
            "JitGetVar", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo MaterializeMethod = typeof(ConstantValue).GetMethod("Materialize", Type.EmptyTypes);
        private static readonly MethodInfo IntBinaryMethod  = typeof(VirtualMachine).GetMethod(
            "IntBinary", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo InvokeCallableMethod = typeof(VirtualMachine).GetMethod(
            "InvokeCallable", new[] { typeof(ScriptVar), typeof(ScriptVar), typeof(ScriptVar[]) });

        private static MethodInfo Prop(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type t,
            string name) => t.GetProperty(name).GetGetMethod();

        private readonly DynamicMethod method;
        private readonly List<object> data = new();

        public ILGenerator IL { get; }

        public DynamicMethodBuilder(string name)
        {
            method = new DynamicMethod(
                "jit_" + name,
                typeof(ScriptVar),
                new[] { typeof(object[]), typeof(VirtualMachine), typeof(ScriptVar[]), typeof(Environment) },
                typeof(DynamicMethodBuilder).Module,
                skipVisibility: true);
            IL = method.GetILGenerator();
        }

        /// <summary>Declare a CLR local of type <typeparamref name="T"/> and return it.</summary>
        public LocalBuilder DeclareLocal(Type t) => IL.DeclareLocal(t);

        /// <summary>Bake a value into the data array, returning its index.</summary>
        public int AddData(object value)
        {
            data.Add(value);
            return data.Count - 1;
        }

        // ── value loads ───────────────────────────────────────────────────────

        /// <summary>Push baked <c>data[index]</c> cast to <typeparamref name="T"/>.</summary>
        public void EmitLoadData(int index, Type asType)
        {
            IL.Emit(OpCodes.Ldarg_0);       // data
            EmitLdcI4(index);
            IL.Emit(OpCodes.Ldelem_Ref);
            IL.Emit(OpCodes.Castclass, asType);
        }

        /// <summary>Push the <c>env</c> argument (an <see cref="Environment"/>).</summary>
        public void EmitLoadEnv() => IL.Emit(OpCodes.Ldarg, ArgEnv);

        /// <summary>Push the <c>vm</c> argument (a <see cref="VirtualMachine"/>).</summary>
        public void EmitLoadVm() => IL.Emit(OpCodes.Ldarg, ArgVm);

        /// <summary>Push the <c>args</c> argument (a <see cref="ScriptVar"/>[]).</summary>
        public void EmitLoadArgs() => IL.Emit(OpCodes.Ldarg, ArgArgs);

        public void EmitLoadLocal(LocalBuilder local) => IL.Emit(OpCodes.Ldloc, local);
        public void EmitStoreLocal(LocalBuilder local) => IL.Emit(OpCodes.Stloc, local);

        // ── ScriptVar operation primitives ─────────────────────────────────────

        /// <summary>Materialize a baked <see cref="ConstantValue"/> to a fresh <see cref="ScriptVar"/>.</summary>
        public void EmitMaterializeConstant(ConstantValue constant)
        {
            EmitLoadData(AddData(constant), typeof(ConstantValue));
            IL.EmitCall(OpCodes.Call, MaterializeMethod, null);
        }

        /// <summary>
        /// Resolve a variable by name through the lexical environment chain and push
        /// its value, exactly as the interpreter's GetVar opcode does (including the
        /// globalThis and undefined fall-throughs).
        /// </summary>
        public void EmitLoadNamedVar(string name)
        {
            EmitLoadEnv();
            EmitLoadData(AddData(name), typeof(string));
            IL.EmitCall(OpCodes.Call, JitGetVarMethod, null);
        }

        /// <summary>Emit <c>a.IsInt</c> for the <see cref="ScriptVar"/> in <paramref name="local"/>.</summary>
        public void EmitIsInt(LocalBuilder local)
        {
            EmitLoadLocal(local);
            IL.EmitCall(OpCodes.Callvirt, IsIntGetter, null);
        }

        public void EmitIsDouble(LocalBuilder local)
        {
            EmitLoadLocal(local);
            IL.EmitCall(OpCodes.Callvirt, IsDoubleGetter, null);
        }

        public void EmitIsString(LocalBuilder local)
        {
            EmitLoadLocal(local);
            IL.EmitCall(OpCodes.Callvirt, IsStringGetter, null);
        }

        /// <summary>Push <c>local.Int</c>.</summary>
        public void EmitLoadInt(LocalBuilder local)
        {
            EmitLoadLocal(local);
            IL.EmitCall(OpCodes.Callvirt, IntGetter, null);
        }

        /// <summary>Push <c>local.Float</c>.</summary>
        public void EmitLoadFloat(LocalBuilder local)
        {
            EmitLoadLocal(local);
            IL.EmitCall(OpCodes.Callvirt, FloatGetter, null);
        }

        /// <summary>Box an int already on the stack into a <see cref="ScriptVar"/> via <c>FromInt</c>.</summary>
        public void EmitFromInt() => IL.EmitCall(OpCodes.Call, FromIntMethod, null);

        /// <summary>Box a double already on the stack into a <see cref="ScriptVar"/> via <c>FromDouble</c>.</summary>
        public void EmitFromDouble() => IL.EmitCall(OpCodes.Call, FromDoubleMethod, null);

        /// <summary>Wrap a string already on the stack into a <see cref="ScriptVar"/> via <c>FromString</c>.</summary>
        public void EmitFromString() => IL.EmitCall(OpCodes.Call, FromStringMethod, null);

        /// <summary>Push <c>local.String</c> (the underlying CLR string).</summary>
        public void EmitLoadString(LocalBuilder local)
        {
            EmitLoadLocal(local);
            IL.EmitCall(OpCodes.Callvirt, StringGetter, null);
        }

        /// <summary>Concatenate two CLR strings already on the stack via <c>string.Concat</c>.</summary>
        public void EmitStringConcat() => IL.EmitCall(OpCodes.Call, ConcatMethod, null);

        /// <summary>
        /// Logical NOT of the <see cref="ScriptVar"/> on top of the stack: replaces it
        /// with <c>FromInt(a.Bool ? 0 : 1)</c>, matching the interpreter's Not opcode.
        /// </summary>
        public void EmitLogicalNot()
        {
            IL.EmitCall(OpCodes.Callvirt, BoolGetter, null); // a.Bool -> int 0/1
            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Ceq);                            // 1 when falsy, else 0
            EmitFromInt();
        }

        /// <summary>Push a fresh undefined <see cref="ScriptVar"/>.</summary>
        public void EmitPushUndefined() => IL.EmitCall(OpCodes.Call, CreateUndefinedMethod, null);

        /// <summary>Push a fresh null <see cref="ScriptVar"/>.</summary>
        public void EmitPushNull() => IL.EmitCall(OpCodes.Call, CreateNullMethod, null);

        /// <summary>Push the integer constant <paramref name="value"/> as a <see cref="ScriptVar"/>.</summary>
        public void EmitPushIntConst(int value)
        {
            EmitLdcI4(value);
            EmitFromInt();
        }

        /// <summary>
        /// Emit the generic fallback <c>a.MathsOp(b, op)</c> for the two operands held
        /// in <paramref name="a"/> and <paramref name="b"/>. Leaves the result on the stack.
        /// </summary>
        public void EmitMathsOpFallback(LocalBuilder a, LocalBuilder b, ScriptLex.LexTypes op)
        {
            EmitLoadLocal(a);
            EmitLoadLocal(b);
            EmitLdcI4((int)op);
            IL.EmitCall(OpCodes.Callvirt, MathsOpMethod, null);
        }

        /// <summary>
        /// Emit a call to <see cref="VirtualMachine.IntBinary"/> for the two int operands
        /// in <paramref name="a"/>/<paramref name="b"/>, leaving the resulting
        /// <see cref="ScriptVar"/> on the stack. The boolean "handled" result is consumed
        /// via <paramref name="onUnhandled"/>: a label to branch to when IntBinary returns
        /// false (the operator was not an integer operator).
        /// </summary>
        public void EmitIntBinaryCall(LocalBuilder a, LocalBuilder b, ScriptLex.LexTypes op,
                                      LocalBuilder resultSlot, Label onUnhandled)
        {
            EmitLoadInt(a);
            EmitLoadInt(b);
            EmitLdcI4((int)op);
            IL.Emit(OpCodes.Ldloca, resultSlot);          // out ScriptVar
            IL.EmitCall(OpCodes.Call, IntBinaryMethod, null);
            IL.Emit(OpCodes.Brfalse, onUnhandled);        // not an int operator
            EmitLoadLocal(resultSlot);
        }

        /// <summary>Emit the most compact <c>ldc.i4</c> form for <paramref name="value"/>.</summary>
        public void EmitLdcI4(int value) => IL.Emit(OpCodes.Ldc_I4, value);

        // ── call dispatch ───────────────────────────────────────────────────────

        /// <summary>Allocate a new <c>ScriptVar[</c><paramref name="length"/><c>]</c> on the stack.</summary>
        public void EmitNewScriptVarArray(int length)
        {
            EmitLdcI4(length);
            IL.Emit(OpCodes.Newarr, typeof(ScriptVar));
        }

        /// <summary>Store the value on top of the stack into <c>array[index]</c> (array and value supplied by caller).</summary>
        public void EmitStoreElemRef() => IL.Emit(OpCodes.Stelem_Ref);

        /// <summary>
        /// Emit <c>vm.InvokeCallable(callee, thisArg, args)</c>. The vm, callee,
        /// thisArg and args must already be on the stack in that order.
        /// </summary>
        public void EmitInvokeCallable() => IL.EmitCall(OpCodes.Callvirt, InvokeCallableMethod, null);

        /// <summary>Emit the trailing <c>ret</c> and bake the delegate, binding the data array.</summary>
        public JitDelegate Finish()
        {
            IL.Emit(OpCodes.Ret);
            return (JitDelegate)method.CreateDelegate(typeof(JitDelegate), data.ToArray());
        }
    }
}
