# Task List

Tasks ordered by dependency: tasks with no dependencies come first, followed by tasks that depend on earlier ones. Within each dependency level, ordered easiest → hardest.

---

## No dependencies

---

### T01 — Fix stale `ES-COMPATIBILITY.md` entry for `Object.getOwnPropertyDescriptors`

**Effort:** Trivial  
**File:** `ES-COMPATIBILITY.md`  
**Dependencies:** None

Remove the duplicate ❌ row in the ES2017 section and the stale "not implemented" bullet in the limitations list. The method is already implemented at `ObjectFunctionProvider.cs:247`.

---

### T02 — Hashbang (`#!`) prologue (ES2023)

**Effort:** Trivial  
**File:** `DScript/ScriptLex.cs` — `GetNextToken()`  
**Dependencies:** None

At the top of the whitespace/comment-skipping loop, add a guard: if at the very start of the source (`dataPos == dataStart + 2`) and `CurrentChar == '#'` and `NextChar == '!'`, skip forward to end of line then `continue`. Two or three lines.

---

### T03 — Optional `catch` binding / `catch { }` (ES2019)

**Effort:** Trivial  
**File:** `DScript/Compiler/Compiler.Statement.cs` — `CompileTry()` (~line 782)  
**Dependencies:** None

The VM already handles `catchVarIdx == -1` correctly (`VirtualMachine.cs:1407`). Only the compiler needs changing: after matching `catch`, if the next tokens are `()` (empty parens) or no parens at all, set `catchVarIdx = -1` and skip the identifier match.

---

### T04 — `Symbol.prototype.description` (ES2019)

**Effort:** Small  
**File:** `DScript/SymbolRegistrar.cs`  
**Dependencies:** None

The description string is already stored in `scriptData` (`ScriptVar.cs:109`). Register a getter on the Symbol prototype that returns `sym.scriptData as string` (or `undefined` if null), with a `TypeError` guard if `this` is not a Symbol.

---

### T05 — `Function.prototype.toString` (ES2019)

**Effort:** Small  
**File:** `DScript.Extras/FunctionProviders/FunctionFunctionProvider.cs`  
**Dependencies:** None

Register `Function.prototype.toString` as a native method. For compiled VM functions return `VmFunction.Source` (already on the chunk). For native functions return `"function <name>() { [native code] }"` using the name from `fn.FindChild("name")`.

---

### T06 — RegExp `d` (indices) flag (ES2022)

**Effort:** Small  
**Files:** `DScript/ScriptLex.cs`, `DScript/ScriptVar.cs` (~line 261), RegExp `exec()`/`match()` result builder  
**Dependencies:** None

Three steps:
1. Lexer: accept `d` in the flags string.
2. `ScriptVar` regex constructor: store a `hasIndices` bool when `d` is present — no `RegexOptions` change needed.
3. Result builder: when `hasIndices`, populate `.indices[i] = [group.Index, group.Index + group.Length]` and `.indices.groups.<name>` for named captures from the .NET `Match.Groups` collection.

---

### T07 — Strict mode phase 1: directive detection + `Chunk.IsStrict`

**Effort:** Small  
**Files:** `DScript/Vm/Chunk.cs`, `DScript/Compiler/Compiler.cs`, `DScript/Compiler/Compiler.Statement.cs`  
**Dependencies:** None  
**Blocks:** T08, T09, T10, T11, T12, T13

Add `bool IsStrict { get; set; }` to `Chunk`. At the start of `CompileProgram` and each function/arrow body, consume a `"use strict"` string-literal first statement without emitting bytecode and set the flag. Propagate `IsStrict` downward into nested function chunks automatically.

---

### T08 — Unicode property escapes in RegExp / `u` flag (ES2018)

**Effort:** Small–Medium  
**Files:** `DScript/ScriptLex.cs`, `DScript/ScriptVar.cs` (~line 261)  
**Dependencies:** None

Accept `u` and `v` flags in the lexer. In the `ScriptVar` regex constructor when `u`/`v` is present, drop `RegexOptions.ECMAScript`, add `RegexOptions.CultureInvariant`, and translate JS `\p{...}` property names to .NET equivalents via a translation dictionary (e.g. `Letter` → `L`, `Script=Greek` → `IsGreek`). Common categories cover ~90% of real-world use; full coverage is a long tail.

---

### T09 — `for await...of` (ES2018)

**Effort:** Medium  
**Files:** `DScript/Compiler/Compiler.Statement.cs`, `DScript/Vm/OpCode.cs`, `DScript/Vm/VirtualMachine.cs`  
**Dependencies:** None  
**Note:** Closely related to T10 — implement together.

1. Compiler: detect `for await (... of ...)`, enforce async context, emit new `ForAwaitOfStep` opcode.
2. New opcode `ForAwaitOfStep` appended to end of `OpCode` enum.
3. Extend `GetIterator` to check `[Symbol.asyncIterator]` first.
4. VM: `ForAwaitOfStep` handler calls `.next()`, yields the resulting Promise through the existing async drive loop, then on resume checks `done`.

---

### T10 — Async generators / `async function*` (ES2018)

**Effort:** Medium  
**Files:** `DScript/Compiler/Compiler.Factor.cs`, `DScript/Compiler/Compiler.Statement.cs`, `DScript/Vm/VirtualMachine.cs`  
**Dependencies:** None  
**Note:** Closely related to T09 — implement together.

1. Compiler: parse `async function*`, set both `chunk.IsAsync` and `chunk.IsGenerator`.
2. VM: add `CreateAsyncGeneratorIterator` — each `.next()` returns a Promise resolving to `{value, done}`, combining the existing `CreateAsyncPromise` and `CreateGeneratorIterator` paths.
3. Add `[Symbol.asyncIterator]` returning `this` to the async generator object.
4. `.return()` and `.throw()` must also wrap results in Promises.

---

## Depends on T07

---

### T11 — Strict mode phase 2: compile-time errors

**Effort:** Small  
**Files:** `DScript/Compiler/Compiler.Statement.cs`, `DScript/Compiler/Compiler.Factor.cs`  
**Dependencies:** T07  
**Blocks:** T13

When `chunk.IsStrict`, throw a `SyntaxError`-equivalent at compile time for:
- Duplicate parameter names
- `delete <identifier>` (plain variable deletion)
- `eval` or `arguments` used as a binding name
- Octal literals (`0777`)

---

### T12 — Strict mode phase 4: `this` is `undefined` in plain calls

**Effort:** Small  
**File:** `DScript/Vm/VirtualMachine.cs` — `Call` opcode handler  
**Dependencies:** T07

When a function is called as a plain call (not via `CallMethod`) and the callee chunk is strict, pass `undefined` as `this` instead of the global root.

---

### T13 — Strict mode phase 6: `arguments.callee` / `arguments.caller` throw `TypeError`

**Effort:** Small  
**File:** wherever `arguments` is constructed for a call frame  
**Dependencies:** T07

Add accessor properties to the `arguments` object that throw `TypeError` when the enclosing function chunk is strict.

---

### T14 — Strict mode phase 3: undeclared assignment throws `ReferenceError`

**Effort:** Medium  
**File:** `DScript/Vm/VirtualMachine.cs` — `SetVar` opcode handler  
**Dependencies:** T07

`SetVar` currently walks the scope chain and creates the variable on the global object if not found. In strict mode it must throw `ReferenceError` instead. Gate on `chunk.IsStrict` in the current frame.

---

### T15 — Strict mode phase 5: non-writable property write throws `TypeError`

**Effort:** Medium  
**File:** `DScript/Vm/VirtualMachine.cs` — `SetProp` opcode handler  
**Dependencies:** T07

`ScriptVar` already carries a `Writable` flag. `SetProp` currently silently ignores writes to non-writable properties. In strict mode, gate a `TypeError` throw on `chunk.IsStrict`.

---

## Depends on T07 + T11

---

### T16 — Strict mode phase 7: block-scoped function declarations

**Effort:** Large  
**Files:** `DScript/Compiler/Compiler.Statement.cs`, VM scope handling  
**Dependencies:** T07, T11

In strict mode, function declarations inside blocks must be block-scoped rather than hoisted. Requires the compiler to emit `DeclareLocal` for such declarations at the declaration site. Defer to a later milestone.
