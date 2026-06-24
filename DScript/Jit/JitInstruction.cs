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
        /// <summary>Pop an object, read a named property, push it.</summary>
        GetProp,
        /// <summary>Push a fresh null.</summary>
        PushNull,
        /// <summary>Push a fresh undefined.</summary>
        PushUndefined,
        /// <summary>Pop two operands, apply a binary operator, push the result.</summary>
        Binary,
        /// <summary>Logical NOT of the top operand.</summary>
        Not,
        /// <summary>Pop callee + N args, dispatch, push the result.</summary>
        Call,
        /// <summary>Discard the top operand (its side effects still run).</summary>
        Pop,
        /// <summary>Return the top operand.</summary>
        Return,
    }

    /// <summary>One normalised JIT instruction. Only the fields relevant to
    /// <see cref="Kind"/> are populated.</summary>
    internal readonly struct JitInstruction
    {
        public readonly JitOpKind Kind;
        public readonly ConstantValue Constant;     // PushConst
        public readonly int IntValue;               // PushIntLiteral, Call (argc)
        public readonly string Name;                // PushVar
        public readonly ScriptLex.LexTypes Op;      // Binary
        public readonly ScriptVar MonoCallee;       // Call (the baked monomorphic target, or null)

        private JitInstruction(JitOpKind kind, ConstantValue constant, int intValue,
                               string name, ScriptLex.LexTypes op, ScriptVar monoCallee)
        {
            Kind = kind;
            Constant = constant;
            IntValue = intValue;
            Name = name;
            Op = op;
            MonoCallee = monoCallee;
        }

        public static JitInstruction PushConst(ConstantValue c) =>
            new(JitOpKind.PushConst, c, 0, null, default, null);
        public static JitInstruction PushIntLiteral(int v) =>
            new(JitOpKind.PushIntLiteral, null, v, null, default, null);
        public static JitInstruction PushVar(string name) =>
            new(JitOpKind.PushVar, null, 0, name, default, null);
        public static JitInstruction GetProp(string name) =>
            new(JitOpKind.GetProp, null, 0, name, default, null);
        public static JitInstruction PushNull() =>
            new(JitOpKind.PushNull, null, 0, null, default, null);
        public static JitInstruction PushUndefined() =>
            new(JitOpKind.PushUndefined, null, 0, null, default, null);
        public static JitInstruction Binary(ScriptLex.LexTypes op) =>
            new(JitOpKind.Binary, null, 0, null, op, null);
        public static JitInstruction Not() =>
            new(JitOpKind.Not, null, 0, null, default, null);
        public static JitInstruction Call(int argc, ScriptVar monoCallee) =>
            new(JitOpKind.Call, null, argc, null, default, monoCallee);
        public static JitInstruction Pop() =>
            new(JitOpKind.Pop, null, 0, null, default, null);
        public static JitInstruction Return() =>
            new(JitOpKind.Return, null, 0, null, default, null);
    }
}
