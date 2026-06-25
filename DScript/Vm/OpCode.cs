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
        Dup2,            //                 duplicate the top two values (a,b -> a,b,a,b)
        EnumKeys,        //                 obj -> array of own member-name strings (for...in)

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
        JumpIfDefined,   // [i target]      pop; jump if NOT undefined (i.e. value is defined)
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
        Throw,           //                 throw top of stack

        // --- termination ----------------------------------------------------
        Halt,            //                 stop execution of the top-level chunk

        // --- fused forms (appended to preserve existing opcode byte values) --
        BinaryConst,     // [i op][i constIndex]  a -> (a op constants[i])
                         //                 fuses a Constant push with the Binary
                         //                 that consumes it, when the right operand
                         //                 is a single literal (saves an opcode
                         //                 dispatch + a push/pop per use)

        BinaryIntConst,  // [i op][i intValue]    a -> (a op intValue)
                         //                 like BinaryConst but stores the integer
                         //                 value inline rather than as a constant-pool
                         //                 index, eliminating the pool lookup on the
                         //                 hot path (e.g. i < n; i + 1 in tight loops)

        // --- optional chaining / nullish coalescing -------------------------
        JumpIfNullOrUndefined,
                         // [i target]      pop TOS; if null or undefined: push undefined
                         //                 and jump to target; else push the value back.
                         //                 Used for `?.` member/index/call chains.

        SetPropDynamic,  //                 obj, key, value → obj
                         //                 pops value and key, peeks obj, sets obj[key]=value
                         //                 (computed object-literal property init).

        // --- structured exception handling (inline bytecode) ----------------
        EnterTry,        // [i catchPC][i finallyPC][i catchVarIdx]
                         //                 push a handler frame; catchPC/finallyPC are
                         //                 absolute bytecode offsets (-1 = absent);
                         //                 catchVarIdx is a Names index (-1 = no binding)
        LeaveTry,        // [i destPC]      normal exit from try body; pop handler frame,
                         //                 jump to destPC (finally or after)
        LeaveCatch,      // [i destPC]      normal exit from catch body; pop the
                         //                 catch-protecting frame, jump to destPC
        LeaveFinally,    //                 end of finally; rethrow pending exception,
                         //                 propagate pending return, or fall through
        SaveReturn,      //                 pop top of stack, save as pending return value
                         //                 (used when `return` appears inside try-with-finally)

        // --- block scoping --------------------------------------------------
        EnterBlock,      //                 push a new child scope frame (for `let`/`const` blocks)
        LeaveBlock,      //                 pop the innermost scope frame (restore parent)
        DeclareLocal,    // [i nameIndex]   declare a block-local variable in the innermost
                         //                 scope (used for `let`; does NOT hoist past blocks)

        // --- Phase 4: spread / rest / destructuring --------------------------------
        PushSpread,      //  arr, spreadArr → arr  (appends all elements of spreadArr to arr)
        MergeObject,     //  obj, sourceObj → obj  (copies all own properties of sourceObj to obj)
        CallSpread,      //  fn, argsArr → result
        CallMethodSpread,//  obj, fn, argsArr → result  (this = obj)
        NewSpread,       //  ctor, argsArr → instance

        // --- Phase 5: tail-call elimination ------------------------------------
        TailCall,        // [i argc]        fn, a1..aN -> result  (tail-position direct call)
        TailCallMethod,  // [i argc]        obj, fn, a1..aN -> result  (tail-position method call)

        // --- Phase 7: generators / iterators -----------------------------------
        Yield,           //                 pop value, suspend generator, push resume value
        GetIterator,     //                 iterable → iterator (wraps arrays; passes through objects with .next)

        // --- Phase 5 optimisation: fused for..of step -------------------------
        ForOfStep,       // <exitOffset>    pops iter, calls .next() natively; if done jumps to exitOffset, else pushes value

        // --- Phase 9: import.meta -------------------------------------------
        PushImportMeta,  //                 push the import.meta object for the current module

        // --- Phase 9: computed method call receiver fix ---------------------
        // Pops key, peeks obj (does not pop it), pushes GetMember(obj, key).
        // Stack: [obj, key] → [obj, fn].  Preserves receiver for CallMethod.
        GetIndexMethod,

        // --- Phase 10: dynamic import() -------------------------------------
        // Pops specifier string, loads module via ModuleLoader, pushes Promise<exports>.
        DynamicImport,

        // --- Property accessors (ES5 get/set syntax) ------------------------
        DefineGetter,    // [i nameIndex]  pops fn, peeks obj, defines getter on obj[name]
        DefineSetter,    // [i nameIndex]  pops fn, peeks obj, defines setter on obj[name]

        // --- Tagged template literals (ES2015) -------------------------------
        // Stack: tag, cooked[0..n-1], raw[0..n-1], expr[0..m-1]
        // Builds the strings array (with .raw), calls tag(strings, expr0..exprM), pushes result.
        TaggedTemplate,  // [i numStrings] [i numExprs]

        // --- Narrow (2-byte) opcode forms ------------------------------------
        // For instructions whose operand is a name or constant index < 256,
        // the narrow form stores the index as a single byte, saving 3 bytes per
        // instruction over the wide (5-byte) form.  Emitted by the post-
        // compilation NarrowEncodePass after all other peephole passes have
        // run; the wide forms are used during compilation so existing peephole
        // passes can inspect them without change.
        // APPENDED AT THE END per CLAUDE.md rules (preserves existing byte values).
        GetVarN,        // [b nameIndex]   narrow form of GetVar
        SetVarN,        // [b nameIndex]   narrow form of SetVar
        ConstantN,      // [b constIndex]  narrow form of Constant
        GetPropN,       // [b nameIndex]   narrow form of GetProp
        SetPropN,       // [b nameIndex]   narrow form of SetProp
        DeclareVarN,    // [b nameIndex]   narrow form of DeclareVar
        DeclareConstN,  // [b nameIndex]   narrow form of DeclareConst
        DeclareLocalN,  // [b nameIndex]   narrow form of DeclareLocal
        InitPropN,      // [b nameIndex]   narrow form of InitProp

        // --- Async iteration (ES2018) ----------------------------------------
        GetAsyncIterator, //               iterable → async iterator (checks Symbol.asyncIterator first, then Symbol.iterator)
        ForAwaitOfStep,   // <exitOffset>  pops iter, calls .next() → Promise; yields Promise; on resume if done→exit else push value

        // --- Superinstructions: fused opcode pairs ----------------------------------
        // Emitted by FuseSuperInstructions() after all earlier peephole passes.
        // Wide forms are fused before NarrowEncodePass runs; narrow forms are the
        // result of NarrowEncodePass processing the wide fused forms.
        // APPENDED AT THE END per CLAUDE.md rules (preserves existing byte values).
        SetVarPop,       // [i nameIndex]             fused SetVar + Pop  (assignment statement, result discarded)
        SetPropPop,      // [i nameIndex]             fused SetProp + Pop (property-set statement, result discarded)
        GetVarGetProp,   // [i varIndex][i propIndex] fused GetVar + GetProp (one-level property read a.b)
        SetVarPopN,      // [b nameIndex]             narrow form of SetVarPop      (2 bytes)
        SetPropPopN,     // [b nameIndex]             narrow form of SetPropPop     (2 bytes)
        GetVarGetPropN,  // [b varIndex][b propIndex] narrow form of GetVarGetProp  (3 bytes)

        // --- Phase 11: method call receiver optimisation -----------------------
        // Named property method-call setup: replaces the Dup+GetProp pair that
        // the compiler used to emit before every obj.method(args) call.
        // GetPropMethod peeks the receiver (keeps it on stack) and pushes the
        // method, ready for a subsequent CallMethod.
        // GetPropCall0 additionally performs the zero-argument call inline,
        // eliminating the separate CallMethod 0 opcode entirely.
        GetPropMethod,   // [i nameIndex]   peek obj (keep), push obj.name  → [obj,fn]
        GetPropCall0,    // [i nameIndex]   pop obj, call obj.name() → result
        GetPropMethodN,  // [b nameIndex]   narrow form of GetPropMethod    (2 bytes)
        GetPropCall0N,   // [b nameIndex]   narrow form of GetPropCall0     (2 bytes)

        // --- Phase 12: O(n) array append ----------------------------------------
        // Replaces the Dup+GetProp("length")+value+SetPropDynamic pattern emitted
        // for post-spread static elements in array literals.  AppendArrayElement
        // reads and maintains the cached array length so each append is O(1).
        AppendElem,      //                 arr, value → arr  (appends value at arr.length)

        // --- Superinstruction: GetVar + GetVar + Binary -------------------------
        // Fuses the two GetVar dispatches and the Binary dispatch into a single
        // handler. The binary operator and both variable indices are inline so the
        // handler reads all three operands, resolves both variables via the scope
        // cache, and executes the operation without any intermediate Push/Pop.
        // Layout: [i op][i var1Index][i var2Index]  (13 bytes wide, 4 bytes narrow)
        GetVarGetVarBinary,  // [i op][i var1][i var2]   (a op b) where a,b are named vars
        GetVarGetVarBinaryN, // [b op][b var1][b var2]   narrow form (4 bytes)

        // --- Object-literal member after a spread -------------------------------
        // Like InitProp but overwrites an existing same-named property instead of
        // appending a duplicate link. Emitted only for explicit/shorthand/method
        // keys that follow a spread (`{ ...o, d: 9 }`), where the key may already
        // have been merged in — so the later literal key must win (JS semantics).
        // The common no-spread path keeps using plain InitProp (append, no lookup).
        InitPropOverwrite, // [i nameIndex]   obj, value -> obj
    }
}
