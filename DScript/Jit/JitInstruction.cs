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

using DScript.Vm;

namespace DScript.Jit
{
    /// <summary>
    /// The primitive operations a JIT back-end must implement. The shared
    /// <see cref="JitDecoder"/> normalises a chunk's bytecode (including fused
    /// super-instructions such as <c>BinaryConst</c> and <c>GetVarGetVarBinary</c>)
    /// down to this small set, so every back-end consumes the same uniform stream.
    /// </summary>
    internal enum JitOpKind
    {
        /// <summary>Push a materialised constant.</summary>
        PushConst,
        /// <summary>Push an integer literal value.</summary>
        PushIntLiteral,
        /// <summary>Resolve a variable by name and push it.</summary>
        PushVar,
        /// <summary>Push a positional local slot (Lever A). IntValue is the slot index.</summary>
        GetLocal,
        /// <summary>Pop a value, store it in a positional local slot, push it (Lever A). IntValue is the slot index.</summary>
        SetLocal,
        /// <summary>Pop an object, read a named property, push it.</summary>
        GetProp,
        /// <summary>Assign a variable (expression form): pop value, set, push value.</summary>
        SetVar,
        /// <summary>Assign a variable (statement form): pop value, set.</summary>
        SetVarPop,
        /// <summary>Set a property (expression): pop value + object, set, push value.</summary>
        SetProp,
        /// <summary>Set a property (statement): pop value + object, set.</summary>
        SetPropPop,
        /// <summary>Declare a function-scoped variable (no stack effect).</summary>
        DeclareVar,
        /// <summary>Declare a block-scoped variable (no stack effect).</summary>
        DeclareLocal,
        /// <summary>Declare a block-scoped const (no stack effect).</summary>
        DeclareConst,
        /// <summary>Enter a block scope (push a child environment).</summary>
        EnterBlock,
        /// <summary>Leave a block scope (restore the parent environment).</summary>
        LeaveBlock,
        /// <summary>Push a fresh null.</summary>
        PushNull,
        /// <summary>Push a fresh undefined.</summary>
        PushUndefined,
        /// <summary>Pop two operands, apply a binary operator, push the result.</summary>
        Binary,
        /// <summary>Unconditional branch to a target instruction index.</summary>
        Jump,
        /// <summary>Pop a condition; branch if falsy.</summary>
        JumpIfFalse,
        /// <summary>Pop a condition; branch if truthy.</summary>
        JumpIfTrue,
        /// <summary>Peek a condition; branch (keeping it) if falsy, else pop it. (&amp;&amp;)</summary>
        JumpIfFalseOrPop,
        /// <summary>Peek a condition; branch (keeping it) if truthy, else pop it. (||)</summary>
        JumpIfTrueOrPop,
        /// <summary>Pop a value; if null/undefined push undefined and branch, else push the value. (??)</summary>
        JumpIfNullOrUndefined,
        /// <summary>Pop a value; branch if it is not undefined. (optional chaining)</summary>
        JumpIfDefined,
        /// <summary>Logical NOT of the top operand.</summary>
        Not,
        /// <summary>Pop object + key, read the indexed element, push it.</summary>
        GetIndex,
        /// <summary>Pop object + key + value, set the indexed element, push value.</summary>
        SetIndex,
        /// <summary>Arithmetic negation of the top operand.</summary>
        Negate,
        /// <summary>Bitwise NOT of the top operand.</summary>
        BitNot,
        /// <summary>typeof of the top operand.</summary>
        Typeof,
        /// <summary>Numeric coercion of the top operand.</summary>
        ToNumber,
        /// <summary>Pop two operands, apply a shift operator, push the result.</summary>
        Shift,
        /// <summary>Pop callee + N args, dispatch, push the result.</summary>
        Call,
        /// <summary>Create a closure over the current environment and push it.</summary>
        MakeClosure,
        /// <summary>Peek the receiver (keep it), push its named method.</summary>
        GetPropMethod,
        /// <summary>Pop the receiver, call its named method with no args, push the result.</summary>
        GetPropCall0,
        /// <summary>Pop receiver + callee + N args, dispatch with the receiver as `this`, push the result.</summary>
        CallMethod,
        /// <summary>Push a fresh empty object (object-literal start).</summary>
        NewObject,
        /// <summary>Push a fresh empty array (array-literal start).</summary>
        NewArray,
        /// <summary>Pop a value, add it as a named property of the peeked object, keep the object.</summary>
        InitProp,
        /// <summary>Pop a value, store it at the given index of the peeked array, keep the array.</summary>
        InitElem,
        /// <summary>Discard the top operand (its side effects still run).</summary>
        Pop,
        /// <summary>Return the top operand.</summary>
        Return,
        /// <summary>Pop ctor + N args, call the constructor, push the new instance.</summary>
        New,
        /// <summary>Pop source; merge its enumerable own properties into the peeked target; keep target.</summary>
        MergeObject,
        /// <summary>Pop value; add/replace the named property on the peeked object; keep the object.</summary>
        InitPropOverwrite,
        /// <summary>Pop value; append it to the end of the peeked array; keep the array.</summary>
        AppendElem,
    }

    /// <summary>Which kind of variable declaration to emit.</summary>
    internal enum JitDeclareKind { Var, Local, Const }

    /// <summary>One normalised JIT instruction. Only the fields relevant to
    /// <see cref="Kind"/> are populated.</summary>
    internal readonly struct JitInstruction
    {
        public readonly JitOpKind Kind;
        public readonly ConstantValue Constant;     // PushConst
        public readonly int IntValue;               // PushIntLiteral, Call (argc), Jump* (target instruction index)
        public readonly string Name;                // PushVar
        public readonly ScriptLex.LexTypes Op;      // Binary
        public readonly ScriptVar MonoCallee;        // Call: first baked callee (monomorphic/bimorphic), or null
        public readonly ScriptVar MonoCallee1;       // Call: second baked callee (bimorphic only), or null
        public readonly Chunk Closure;               // MakeClosure: the nested function's chunk

        private JitInstruction(JitOpKind kind, ConstantValue constant, int intValue,
                               string name, ScriptLex.LexTypes op, ScriptVar monoCallee,
                               ScriptVar monoCallee1 = null, Chunk closure = null)
        {
            Kind = kind;
            Constant = constant;
            IntValue = intValue;
            Name = name;
            Op = op;
            MonoCallee = monoCallee;
            MonoCallee1 = monoCallee1;
            Closure = closure;
        }

        public static JitInstruction PushConst(ConstantValue c) =>
            new(JitOpKind.PushConst, c, 0, null, default, null);
        public static JitInstruction PushIntLiteral(int v) =>
            new(JitOpKind.PushIntLiteral, null, v, null, default, null);
        public static JitInstruction PushVar(string name) =>
            new(JitOpKind.PushVar, null, 0, name, default, null);
        public static JitInstruction GetLocal(int slot) =>
            new(JitOpKind.GetLocal, null, slot, null, default, null);
        public static JitInstruction SetLocal(int slot) =>
            new(JitOpKind.SetLocal, null, slot, null, default, null);
        public static JitInstruction GetProp(string name) =>
            new(JitOpKind.GetProp, null, 0, name, default, null);
        public static JitInstruction SetVar(string name) =>
            new(JitOpKind.SetVar, null, 0, name, default, null);
        public static JitInstruction SetVarPop(string name) =>
            new(JitOpKind.SetVarPop, null, 0, name, default, null);
        public static JitInstruction SetProp(string name) =>
            new(JitOpKind.SetProp, null, 0, name, default, null);
        public static JitInstruction SetPropPop(string name) =>
            new(JitOpKind.SetPropPop, null, 0, name, default, null);
        public static JitInstruction DeclareVar(string name) =>
            new(JitOpKind.DeclareVar, null, 0, name, default, null);
        public static JitInstruction DeclareLocal(string name) =>
            new(JitOpKind.DeclareLocal, null, 0, name, default, null);
        public static JitInstruction DeclareConst(string name) =>
            new(JitOpKind.DeclareConst, null, 0, name, default, null);
        public static JitInstruction EnterBlock() =>
            new(JitOpKind.EnterBlock, null, 0, null, default, null);
        public static JitInstruction LeaveBlock() =>
            new(JitOpKind.LeaveBlock, null, 0, null, default, null);
        public static JitInstruction PushNull() =>
            new(JitOpKind.PushNull, null, 0, null, default, null);
        public static JitInstruction PushUndefined() =>
            new(JitOpKind.PushUndefined, null, 0, null, default, null);
        public static JitInstruction Binary(ScriptLex.LexTypes op) =>
            new(JitOpKind.Binary, null, 0, null, op, null);
        public static JitInstruction Jump(int targetIndex) =>
            new(JitOpKind.Jump, null, targetIndex, null, default, null);
        public static JitInstruction JumpIfFalse(int targetIndex) =>
            new(JitOpKind.JumpIfFalse, null, targetIndex, null, default, null);
        public static JitInstruction JumpIfTrue(int targetIndex) =>
            new(JitOpKind.JumpIfTrue, null, targetIndex, null, default, null);
        public static JitInstruction JumpIfFalseOrPop(int targetIndex) =>
            new(JitOpKind.JumpIfFalseOrPop, null, targetIndex, null, default, null);
        public static JitInstruction JumpIfTrueOrPop(int targetIndex) =>
            new(JitOpKind.JumpIfTrueOrPop, null, targetIndex, null, default, null);
        public static JitInstruction JumpIfNullOrUndefined(int targetIndex) =>
            new(JitOpKind.JumpIfNullOrUndefined, null, targetIndex, null, default, null);
        public static JitInstruction JumpIfDefined(int targetIndex) =>
            new(JitOpKind.JumpIfDefined, null, targetIndex, null, default, null);
        public static JitInstruction Not() =>
            new(JitOpKind.Not, null, 0, null, default, null);
        public static JitInstruction GetIndex() =>
            new(JitOpKind.GetIndex, null, 0, null, default, null);
        public static JitInstruction SetIndex() =>
            new(JitOpKind.SetIndex, null, 0, null, default, null);
        public static JitInstruction Negate() =>
            new(JitOpKind.Negate, null, 0, null, default, null);
        public static JitInstruction BitNot() =>
            new(JitOpKind.BitNot, null, 0, null, default, null);
        public static JitInstruction Typeof() =>
            new(JitOpKind.Typeof, null, 0, null, default, null);
        public static JitInstruction ToNumber() =>
            new(JitOpKind.ToNumber, null, 0, null, default, null);
        public static JitInstruction Shift(ScriptLex.LexTypes op) =>
            new(JitOpKind.Shift, null, 0, null, op, null);
        public static JitInstruction Call(int argc, ScriptVar callee0, ScriptVar callee1) =>
            new(JitOpKind.Call, null, argc, null, default, callee0, callee1);
        public static JitInstruction MakeClosure(Chunk closure) =>
            new(JitOpKind.MakeClosure, null, 0, null, default, null, null, closure);
        public static JitInstruction GetPropMethod(string name) =>
            new(JitOpKind.GetPropMethod, null, 0, name, default, null);
        public static JitInstruction GetPropCall0(string name) =>
            new(JitOpKind.GetPropCall0, null, 0, name, default, null);
        public static JitInstruction CallMethod(int argc) =>
            new(JitOpKind.CallMethod, null, argc, null, default, null);
        public static JitInstruction NewObject() =>
            new(JitOpKind.NewObject, null, 0, null, default, null);
        public static JitInstruction NewArray() =>
            new(JitOpKind.NewArray, null, 0, null, default, null);
        public static JitInstruction InitProp(string name) =>
            new(JitOpKind.InitProp, null, 0, name, default, null);
        public static JitInstruction InitElem(int index) =>
            new(JitOpKind.InitElem, null, index, null, default, null);
        public static JitInstruction Pop() =>
            new(JitOpKind.Pop, null, 0, null, default, null);
        public static JitInstruction Return() =>
            new(JitOpKind.Return, null, 0, null, default, null);
        public static JitInstruction New(int argc) =>
            new(JitOpKind.New, null, argc, null, default, null);
        public static JitInstruction MergeObject() =>
            new(JitOpKind.MergeObject, null, 0, null, default, null);
        public static JitInstruction InitPropOverwrite(string name) =>
            new(JitOpKind.InitPropOverwrite, null, 0, name, default, null);
        public static JitInstruction AppendElem() =>
            new(JitOpKind.AppendElem, null, 0, null, default, null);
    }
}
