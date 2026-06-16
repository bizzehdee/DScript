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

namespace DScript.Vm
{
    /// <summary>
    /// The DScript virtual-machine instruction set. Each instruction is a single
    /// <see cref="OpCode"/> byte optionally followed by inline operands. Operand
    /// widths are documented per-opcode below and encoded little-endian by
    /// <see cref="Chunk"/>:
    ///   [i] = 4-byte int operand (constant index, name index, jump target, count
    ///         or operator code, depending on the opcode).
    /// </summary>
    public enum OpCode : byte
    {
        // --- constants / literals -------------------------------------------
        Constant,        // [i constIndex]  push a copy of constants[i]
        PushUndefined,   //                 push undefined
        PushNull,        //                 push null
        PushTrue,        //                 push 1 (true)
        PushFalse,       //                 push 0 (false)

        // --- stack ----------------------------------------------------------
        Pop,             //                 discard top of stack
        Dup,             //                 duplicate top of stack

        // --- variables (lexical scope) --------------------------------------
        GetVar,          // [i nameIndex]   push the value of the named variable
        SetVar,          // [i nameIndex]   assign top to the named variable (value left on stack)
        DeclareVar,      // [i nameIndex]   declare a variable in the current scope
        DeclareConst,    // [i nameIndex]   declare a read-only variable in the current scope

        // --- properties -----------------------------------------------------
        GetProp,         // [i nameIndex]   obj -> obj[name]
        SetProp,         // [i nameIndex]   obj, value -> value   (writes an OWN property)
        GetIndex,        //                 obj, key -> obj[key]
        SetIndex,        //                 obj, key, value -> value
        DeleteProp,      // [i nameIndex]   obj -> bool
        DeleteIndex,     //                 obj, key -> bool

        // --- operators ------------------------------------------------------
        Binary,          // [i op]          a, b -> (a op b)   (op is a ScriptLex.LexTypes)
        Shift,           // [i op]          a, b -> (a op b)   for << >> >>>
        Negate,          //                 a -> -a
        Not,             //                 a -> !a
        BitNot,          //                 a -> ~a
        ToNumber,        //                 a -> +a (numeric coercion)
        Typeof,          //                 a -> typeof a (string)
        In,              //                 key, obj -> bool
        InstanceOf,      //                 a, b -> bool

        // --- control flow ---------------------------------------------------
        Jump,            // [i target]      unconditional jump
        JumpIfFalse,     // [i target]      pop; jump if falsy
        JumpIfTrue,      // [i target]      pop; jump if truthy
        JumpIfFalseOrPop,// [i target]      peek; if falsy jump (keep), else pop  (for &&)
        JumpIfTrueOrPop, // [i target]      peek; if truthy jump (keep), else pop (for ||)
        Return,          //                 return top of stack from the current frame

        // --- functions / objects -------------------------------------------
        MakeClosure,     // [i funcIndex]   push a function value capturing the current env
        Call,            // [i argc]        fn, a1..aN -> result
        CallMethod,      // [i argc]        obj, fn, a1..aN -> result  (this = obj)
        New,             // [i argc]        ctor, a1..aN -> instance

        // --- aggregates -----------------------------------------------------
        NewObject,       //                 push an empty object
        NewArray,        //                 push an empty array
        InitProp,        // [i nameIndex]   obj, value -> obj  (object literal member)
        InitElem,        // [i index]       arr, value -> arr  (array literal element)

        // --- exceptions -----------------------------------------------------
        SetupTry,        // [i catch][i fin] push an exception handler (-1 = absent)
        PopTry,          //                 pop the current exception handler
        Throw,           //                 throw top of stack

        // --- termination ----------------------------------------------------
        Halt             //                 stop execution of the top-level chunk
    }
}
