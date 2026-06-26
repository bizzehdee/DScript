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
        private static readonly MethodInfo IsAnyIntGetter   = Prop(typeof(ScriptVar), "IsAnyInt");
        private static readonly MethodInfo IsDoubleGetter   = Prop(typeof(ScriptVar), "IsDouble");
        private static readonly MethodInfo IsStringGetter   = Prop(typeof(ScriptVar), "IsString");
        private static readonly MethodInfo IsNullGetter      = Prop(typeof(ScriptVar), "IsNull");
        private static readonly MethodInfo IsUndefinedGetter = Prop(typeof(ScriptVar), "IsUndefined");
        private static readonly MethodInfo IntGetter        = Prop(typeof(ScriptVar), "Int");
        private static readonly MethodInfo LongGetter       = Prop(typeof(ScriptVar), "Long");
        private static readonly MethodInfo BoolGetter        = Prop(typeof(ScriptVar), "Bool");
        private static readonly MethodInfo FloatGetter      = Prop(typeof(ScriptVar), "Float");
        private static readonly MethodInfo FromIntMethod    = typeof(ScriptVar).GetMethod("FromInt", new[] { typeof(int) });
        private static readonly MethodInfo FromLongMethod   = typeof(ScriptVar).GetMethod("FromLong", new[] { typeof(long) });
        private static readonly MethodInfo FromDoubleMethod = typeof(ScriptVar).GetMethod("FromDouble", new[] { typeof(double) });
        private static readonly MethodInfo FromStringMethod = typeof(ScriptVar).GetMethod("FromString", new[] { typeof(string) });
        private static readonly MethodInfo CreateUndefinedMethod = typeof(ScriptVar).GetMethod("CreateUndefined", Type.EmptyTypes);
        private static readonly MethodInfo CreateNullMethod = typeof(ScriptVar).GetMethod("CreateNull", Type.EmptyTypes);
        private static readonly MethodInfo StringGetter     = Prop(typeof(ScriptVar), "String");
        private static readonly MethodInfo ConcatMethod     = typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string) });
        private static readonly MethodInfo MathsOpMethod    = typeof(ScriptVar).GetMethod("MathsOp", new[] { typeof(ScriptVar), typeof(ScriptLex.LexTypes) });
        private static readonly MethodInfo JitGetVarMethod  = typeof(VirtualMachine).GetMethod(
            "JitGetVar", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo JitEnterBlockMethod = typeof(VirtualMachine).GetMethod(
            "JitEnterBlock", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo EnvParentGetter   = Prop(typeof(Environment), "Parent");
        private static readonly MethodInfo JitGetPropCachedMethod = typeof(VirtualMachine).GetMethod(
            "JitGetPropCached", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo JitGetPropCall0Method = typeof(VirtualMachine).GetMethod(
            "JitGetPropCall0", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo JitSetVarMethod = typeof(VirtualMachine).GetMethod(
            "JitSetVar", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo JitSetPropMethod = typeof(VirtualMachine).GetMethod(
            "JitSetProp", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo JitGetIndexMethod = typeof(VirtualMachine).GetMethod(
            "JitGetIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo JitSetIndexMethod = typeof(VirtualMachine).GetMethod(
            "JitSetIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo JitNegateMethod = typeof(VirtualMachine).GetMethod(
            "JitNegate", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo JitBitNotMethod = typeof(VirtualMachine).GetMethod(
            "JitBitNot", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo JitTypeofMethod = typeof(VirtualMachine).GetMethod(
            "JitTypeof", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo JitToNumberMethod = typeof(VirtualMachine).GetMethod(
            "JitToNumber", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo JitShiftMethod = typeof(VirtualMachine).GetMethod(
            "JitShift", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo JitDeclareVarMethod = typeof(VirtualMachine).GetMethod(
            "JitDeclareVar", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo JitDeclareLocalMethod = typeof(VirtualMachine).GetMethod(
            "JitDeclareLocal", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo JitDeclareConstMethod = typeof(VirtualMachine).GetMethod(
            "JitDeclareConst", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo JitNewObjectMethod = typeof(VirtualMachine).GetMethod(
            "JitNewObject", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo JitNewArrayMethod = typeof(VirtualMachine).GetMethod(
            "JitNewArray", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo JitInitPropMethod = typeof(VirtualMachine).GetMethod(
            "JitInitProp", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo JitInitElemMethod = typeof(VirtualMachine).GetMethod(
            "JitInitElem", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo MaterializeMethod = typeof(ConstantValue).GetMethod("Materialize", Type.EmptyTypes);
        private static readonly MethodInfo IntBinaryMethod  = typeof(VirtualMachine).GetMethod(
            "IntBinary", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo InvokeCallableMethod = typeof(VirtualMachine).GetMethod(
            "InvokeCallable", new[] { typeof(ScriptVar), typeof(ScriptVar), typeof(ScriptVar[]) });
        private static readonly MethodInfo DeoptimizeMethod = typeof(VirtualMachine).GetMethod(
            "Deoptimize", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly ConstructorInfo DeoptFrameCtor = typeof(DeoptFrame).GetConstructor(
            new[] { typeof(Chunk), typeof(ScriptVar[]), typeof(Environment) });

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

        /// <summary>
        /// When set, variable operations resolve against this local (the "current
        /// environment") instead of the <c>env</c> argument — used so block scopes can
        /// swap the active environment on EnterBlock/LeaveBlock. Null = use the argument.
        /// </summary>
        public LocalBuilder CurrentEnvLocal { get; set; }

        /// <summary>Push the current environment (the <see cref="CurrentEnvLocal"/> if set, else the <c>env</c> argument).</summary>
        public void EmitLoadEnv()
        {
            if (CurrentEnvLocal != null) EmitLoadLocal(CurrentEnvLocal);
            else IL.Emit(OpCodes.Ldarg, ArgEnv);
        }

        /// <summary>Enter a block scope: <c>currentEnv = JitEnterBlock(currentEnv)</c>.</summary>
        public void EmitEnterBlock(LocalBuilder currentEnv)
        {
            EmitLoadLocal(currentEnv);
            IL.EmitCall(OpCodes.Call, JitEnterBlockMethod, null);
            EmitStoreLocal(currentEnv);
        }

        /// <summary>Leave a block scope: <c>currentEnv = currentEnv.Parent</c>.</summary>
        public void EmitLeaveBlock(LocalBuilder currentEnv)
        {
            EmitLoadLocal(currentEnv);
            IL.EmitCall(OpCodes.Callvirt, EnvParentGetter, null);
            EmitStoreLocal(currentEnv);
        }

        /// <summary>Push a fresh empty object (object-literal start).</summary>
        public void EmitNewObject() => IL.EmitCall(OpCodes.Call, JitNewObjectMethod, null);

        /// <summary>Push a fresh empty array (array-literal start).</summary>
        public void EmitNewArray() => IL.EmitCall(OpCodes.Call, JitNewArrayMethod, null);

        /// <summary>
        /// Add a named property to the object-literal under construction. The stack
        /// holds [obj, value]; emits <c>JitInitProp(obj, value, name)</c>, which leaves
        /// the object on the stack for the next initialiser.
        /// </summary>
        public void EmitInitProp(string name)
        {
            EmitLoadData(AddData(name), typeof(string));   // [obj, value, name]
            IL.EmitCall(OpCodes.Call, JitInitPropMethod, null);
        }

        /// <summary>
        /// Store an element into the array-literal under construction. The stack holds
        /// [arr, value]; emits <c>JitInitElem(arr, value, index)</c>, leaving the array.
        /// </summary>
        public void EmitInitElem(int index)
        {
            EmitLdcI4(index);                              // [arr, value, index]
            IL.EmitCall(OpCodes.Call, JitInitElemMethod, null);
        }

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

        /// <summary>
        /// Resolve a variable through a specific baked environment (rather than the
        /// caller's <c>env</c> argument). Used by the inliner to read a callee's free
        /// (e.g. global) variables against the function's captured defining scope —
        /// exactly where the interpreter would resolve them.
        /// </summary>
        public void EmitLoadNamedVarFrom(string name, Environment captured)
        {
            EmitLoadData(AddData(captured), typeof(Environment));
            EmitLoadData(AddData(name), typeof(string));
            IL.EmitCall(OpCodes.Call, JitGetVarMethod, null);
        }

        /// <summary>
        /// Read a property of the object on top of the stack through a per-site inline
        /// cache: <c>vm.JitGetPropCached(obj, name, cell)</c>, where <c>cell</c> is a
        /// fresh <see cref="PropCacheCell"/> baked for this site. <paramref name="objTemp"/>
        /// is a scratch <see cref="ScriptVar"/> local.
        /// </summary>
        public void EmitGetProp(string name, LocalBuilder objTemp)
        {
            EmitStoreLocal(objTemp);          // pop obj
            EmitLoadVm();
            EmitLoadLocal(objTemp);
            EmitLoadData(AddData(name), typeof(string));
            EmitLoadData(AddData(new PropCacheCell()), typeof(PropCacheCell));
            IL.EmitCall(OpCodes.Callvirt, JitGetPropCachedMethod, null);
        }

        /// <summary>
        /// Assign a variable: <c>JitSetVar(env, name, value, strict)</c> where the value
        /// is on top of the stack. When <paramref name="leaveValue"/> is true (expression
        /// form) the value is left on the stack. <paramref name="valTemp"/> is scratch.
        /// </summary>
        public void EmitSetVar(string name, bool strict, bool leaveValue, LocalBuilder valTemp)
        {
            EmitStoreLocal(valTemp);                 // pop value
            EmitLoadEnv();
            EmitLoadData(AddData(name), typeof(string));
            EmitLoadLocal(valTemp);
            IL.Emit(strict ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            IL.EmitCall(OpCodes.Call, JitSetVarMethod, null);
            if (leaveValue) EmitLoadLocal(valTemp);
        }

        /// <summary>
        /// Set a property: <c>vm.JitSetProp(obj, name, value, strict)</c> with the
        /// object below the value on the stack. Leaves the value when
        /// <paramref name="leaveValue"/> is true. <paramref name="valTemp"/>/<paramref name="objTemp"/>
        /// are scratch.
        /// </summary>
        public void EmitSetProp(string name, bool strict, bool leaveValue, LocalBuilder valTemp, LocalBuilder objTemp)
        {
            EmitStoreLocal(valTemp);                 // pop value
            EmitStoreLocal(objTemp);                 // pop object
            EmitLoadVm();
            EmitLoadLocal(objTemp);
            EmitLoadData(AddData(name), typeof(string));
            EmitLoadLocal(valTemp);
            IL.Emit(strict ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            IL.EmitCall(OpCodes.Callvirt, JitSetPropMethod, null);
            if (leaveValue) EmitLoadLocal(valTemp);
        }

        /// <summary>Declare a variable (<c>var</c>/<c>let</c>/<c>const</c>); no stack effect.</summary>
        public void EmitDeclare(string name, JitDeclareKind kind)
        {
            EmitLoadEnv();
            EmitLoadData(AddData(name), typeof(string));
            var m = kind switch
            {
                JitDeclareKind.Var   => JitDeclareVarMethod,
                JitDeclareKind.Local => JitDeclareLocalMethod,
                _                    => JitDeclareConstMethod,
            };
            IL.EmitCall(OpCodes.Call, m, null);
        }

        /// <summary>Emit <c>a.IsInt</c> for the <see cref="ScriptVar"/> in <paramref name="local"/>.</summary>
        public void EmitIsInt(LocalBuilder local)
        {
            EmitLoadLocal(local);
            IL.EmitCall(OpCodes.Callvirt, IsIntGetter, null);
        }

        /// <summary>Emit <c>a.IsAnyInt</c> — true for both int32 and LargeInt (int64) values.</summary>
        public void EmitIsAnyInt(LocalBuilder local)
        {
            EmitLoadLocal(local);
            IL.EmitCall(OpCodes.Callvirt, IsAnyIntGetter, null);
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

        /// <summary>Push <c>local.Long</c> (int64, without truncation for LargeInt values).</summary>
        public void EmitLoadLong(LocalBuilder local)
        {
            EmitLoadLocal(local);
            IL.EmitCall(OpCodes.Callvirt, LongGetter, null);
        }

        /// <summary>Push <c>local.Long</c> and also add <c>FromLong</c> to box it back.</summary>
        public void EmitFromLong() => IL.EmitCall(OpCodes.Call, FromLongMethod, null);

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

        /// <summary>Replace the <see cref="ScriptVar"/> on top of the stack with its
        /// truthiness (<c>a.Bool</c>) as an int 0/1 — used for conditional branches.</summary>
        public void EmitToBool() => IL.EmitCall(OpCodes.Callvirt, BoolGetter, null);

        /// <summary>Replace the <see cref="ScriptVar"/> on top of the stack with <c>a.IsNull</c>.</summary>
        public void EmitIsNull() => IL.EmitCall(OpCodes.Callvirt, IsNullGetter, null);

        /// <summary>Replace the <see cref="ScriptVar"/> on top of the stack with <c>a.IsUndefined</c>.</summary>
        public void EmitIsUndefined() => IL.EmitCall(OpCodes.Callvirt, IsUndefinedGetter, null);

        // ── extra opcodes (delegate to interpreter-identical helpers) ─────────────

        /// <summary>Arithmetic negation of the operand on top of the stack.</summary>
        public void EmitNegate() => IL.EmitCall(OpCodes.Call, JitNegateMethod, null);
        /// <summary>Bitwise NOT of the operand on top of the stack.</summary>
        public void EmitBitNot() => IL.EmitCall(OpCodes.Call, JitBitNotMethod, null);
        /// <summary>typeof of the operand on top of the stack.</summary>
        public void EmitTypeof() => IL.EmitCall(OpCodes.Call, JitTypeofMethod, null);
        /// <summary>Numeric coercion of the operand on top of the stack.</summary>
        public void EmitToNumber() => IL.EmitCall(OpCodes.Call, JitToNumberMethod, null);

        /// <summary>Shift the two operands on the stack ([a, b]) by <paramref name="op"/>.</summary>
        public void EmitShift(ScriptLex.LexTypes op)
        {
            EmitLdcI4((int)op);
            IL.EmitCall(OpCodes.Call, JitShiftMethod, null);
        }

        /// <summary>Indexed read of <c>[obj, key]</c> on the stack: <c>vm.JitGetIndex(obj, key)</c>.</summary>
        public void EmitGetIndex(LocalBuilder keyTmp, LocalBuilder objTmp)
        {
            EmitStoreLocal(keyTmp);   // pop key
            EmitStoreLocal(objTmp);   // pop obj
            EmitLoadVm();
            EmitLoadLocal(objTmp);
            EmitLoadLocal(keyTmp);
            IL.EmitCall(OpCodes.Callvirt, JitGetIndexMethod, null);
        }

        /// <summary>Indexed write of <c>[obj, key, value]</c> on the stack:
        /// <c>vm.JitSetIndex(obj, key, value, strict)</c>; leaves the value if asked.</summary>
        public void EmitSetIndex(bool strict, bool leaveValue, LocalBuilder valTmp, LocalBuilder keyTmp, LocalBuilder objTmp)
        {
            EmitStoreLocal(valTmp);   // pop value
            EmitStoreLocal(keyTmp);   // pop key
            EmitStoreLocal(objTmp);   // pop obj
            EmitLoadVm();
            EmitLoadLocal(objTmp);
            EmitLoadLocal(keyTmp);
            EmitLoadLocal(valTmp);
            IL.Emit(strict ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            IL.EmitCall(OpCodes.Callvirt, JitSetIndexMethod, null);
            if (leaveValue) EmitLoadLocal(valTmp);
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
            EmitLoadLong(a);  // IntBinary now takes (long, long, ...)
            EmitLoadLong(b);
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

        /// <summary>Push <c>argArray[index]</c> (a <see cref="ScriptVar"/>) — used by the
        /// inliner to read an inlined callee's parameter from the caller's arg array.</summary>
        public void EmitLoadArgElement(LocalBuilder argArray, int index)
        {
            EmitLoadLocal(argArray);
            EmitLdcI4(index);
            IL.Emit(OpCodes.Ldelem_Ref);
        }

        /// <summary>
        /// Emit <c>vm.InvokeCallable(callee, thisArg, args)</c>. The vm, callee,
        /// thisArg and args must already be on the stack in that order.
        /// </summary>
        public void EmitInvokeCallable() => IL.EmitCall(OpCodes.Callvirt, InvokeCallableMethod, null);

        /// <summary>
        /// Fused zero-arg method call of the object on top of the stack:
        /// <c>vm.JitGetPropCall0(obj, name, cell)</c>, where <c>cell</c> is a fresh
        /// per-site inline cache. <paramref name="objTemp"/> is scratch.
        /// </summary>
        public void EmitGetPropCall0(string name, LocalBuilder objTemp)
        {
            EmitStoreLocal(objTemp);          // pop obj
            EmitLoadVm();
            EmitLoadLocal(objTemp);
            EmitLoadData(AddData(name), typeof(string));
            EmitLoadData(AddData(new PropCacheCell()), typeof(PropCacheCell));
            IL.EmitCall(OpCodes.Callvirt, JitGetPropCall0Method, null);
        }

        /// <summary>
        /// Method-call dispatch. The stack holds <c>[receiver, callee, arg0..arg{argc-1}]</c>;
        /// emits <c>vm.InvokeCallable(callee, receiver, args)</c> and leaves the result.
        /// </summary>
        public void EmitCallMethod(int argc, LocalBuilder argTmp, LocalBuilder calleeSlot, LocalBuilder receiverSlot, LocalBuilder argArr)
        {
            EmitNewScriptVarArray(argc);
            EmitStoreLocal(argArr);
            for (var j = argc - 1; j >= 0; j--)
            {
                EmitStoreLocal(argTmp);       // pop one argument
                EmitLoadLocal(argArr);
                EmitLdcI4(j);
                EmitLoadLocal(argTmp);
                EmitStoreElemRef();
            }
            EmitStoreLocal(calleeSlot);       // pop callee
            EmitStoreLocal(receiverSlot);     // pop receiver
            EmitLoadVm();
            EmitLoadLocal(calleeSlot);
            EmitLoadLocal(receiverSlot);
            EmitLoadLocal(argArr);
            EmitInvokeCallable();
        }

        // ── speculative unboxed-int tier ────────────────────────────────────────

        /// <summary>
        /// Prologue guard for the speculative int tier: resolve <paramref name="name"/>,
        /// branch to <paramref name="deopt"/> if it is not an integer, otherwise cache
        /// its raw <c>.Int</c> value into <paramref name="intLocal"/>. <paramref name="svTemp"/>
        /// is a scratch <see cref="ScriptVar"/> local. The IL stack is empty when the
        /// branch to <paramref name="deopt"/> is taken, so the deopt block can cleanly
        /// <c>ret</c>.
        /// </summary>
        public void EmitResolveGuardedInt(string name, LocalBuilder intLocal, LocalBuilder svTemp, Label deopt)
        {
            EmitLoadEnv();
            EmitLoadData(AddData(name), typeof(string));
            IL.EmitCall(OpCodes.Call, JitGetVarMethod, null);
            EmitStoreLocal(svTemp);                      // []
            EmitLoadLocal(svTemp);
            IL.EmitCall(OpCodes.Callvirt, IsIntGetter, null);
            IL.Emit(OpCodes.Brfalse, deopt);             // [] at deopt
            EmitLoadLocal(svTemp);
            IL.EmitCall(OpCodes.Callvirt, IntGetter, null);
            EmitStoreLocal(intLocal);                    // []
        }

        /// <summary>
        /// Prologue guard for the speculative double tier: resolve <paramref name="name"/>,
        /// branch to <paramref name="deopt"/> if it is not numeric (int or double),
        /// otherwise cache its <c>.Float</c> value (int operands coerce to double) into
        /// <paramref name="dblLocal"/>. Clean IL stack on the branch to <paramref name="deopt"/>.
        /// </summary>
        public void EmitResolveGuardedDouble(string name, LocalBuilder dblLocal, LocalBuilder svTemp, Label deopt)
        {
            EmitLoadEnv();
            EmitLoadData(AddData(name), typeof(string));
            IL.EmitCall(OpCodes.Call, JitGetVarMethod, null);
            EmitStoreLocal(svTemp);                      // []

            var numeric = IL.DefineLabel();
            EmitLoadLocal(svTemp);
            IL.EmitCall(OpCodes.Callvirt, IsIntGetter, null);
            IL.Emit(OpCodes.Brtrue, numeric);            // int is numeric
            EmitLoadLocal(svTemp);
            IL.EmitCall(OpCodes.Callvirt, IsDoubleGetter, null);
            IL.Emit(OpCodes.Brfalse, deopt);             // [] at deopt
            IL.MarkLabel(numeric);
            EmitLoadLocal(svTemp);
            IL.EmitCall(OpCodes.Callvirt, FloatGetter, null);
            EmitStoreLocal(dblLocal);                    // []
        }

        /// <summary>Push a raw <c>double</c> constant.</summary>
        public void EmitLdcR8(double value) => IL.Emit(OpCodes.Ldc_R8, value);

        /// <summary>Convert the raw int on top of the stack to a raw <c>double</c>.</summary>
        public void EmitConvR8() => IL.Emit(OpCodes.Conv_R8);

        /// <summary>
        /// Emit the deopt block at <paramref name="deopt"/>: bail to
        /// <c>vm.Deoptimize(new DeoptFrame(chunk, args, env))</c> and return its result.
        /// Assumes a clean IL stack at the label.
        /// </summary>
        public void EmitDeoptReturn(Label deopt, int chunkDataIndex)
        {
            IL.MarkLabel(deopt);
            EmitLoadVm();
            EmitLoadData(chunkDataIndex, typeof(Chunk));
            EmitLoadArgs();
            EmitLoadEnv();
            IL.Emit(OpCodes.Newobj, DeoptFrameCtor);     // DeoptFrame(chunk, args, env)
            IL.EmitCall(OpCodes.Callvirt, DeoptimizeMethod, null);
            IL.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// Bake the delegate, binding the data array. When <paramref name="appendRet"/>
        /// is true a trailing <c>ret</c> is emitted (the conservative tier relies on it
        /// to return a fall-through value); the speculative tier emits its own returns
        /// and passes false.
        /// </summary>
        public JitDelegate Finish(bool appendRet = true)
        {
            if (appendRet) IL.Emit(OpCodes.Ret);
            return (JitDelegate)method.CreateDelegate(typeof(JitDelegate), data.ToArray());
        }
    }
}
