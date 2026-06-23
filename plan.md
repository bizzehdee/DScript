# Implementation Plan

---

## `Object.getOwnPropertyDescriptors` (ES2017)

**Status: already implemented** — `ObjectFunctionProvider.cs:247` in `DScript.Extras`.

The only outstanding work is a documentation fix: `ES-COMPATIBILITY.md` has a duplicate row for this method — a correct ✅ entry in the ES5 section and a stale ❌ entry in the ES2017 section (plus a stale "not implemented" note in the limitations list). Both the ❌ row and the limitations note should be removed.

---

## ES2018 — Async generators (`async function*`)

**Status:** ❌ — generators and async work independently; combined is not implemented.

**What's needed:**

- **Compiler** (`Compiler.Factor.cs`, `Compiler.Statement.cs`): Parse `async function*` — recognise both keywords together and set both `chunk.IsAsync = true` and `chunk.IsGenerator = true` on the compiled chunk.
- **VM** (`VirtualMachine.cs`): Add an async generator driver. Instead of returning a plain `{value, done}` object from `.next()`, each `.next()` call must return a Promise that resolves to `{value, done}`. The async-drive loop (currently in `CreateAsyncPromise`) and the generator-step loop (currently in `CreateGeneratorIterator`) need to be combined into a single `CreateAsyncGeneratorIterator` path.
- **`Symbol.asyncIterator`**: The async generator object needs a `[Symbol.asyncIterator]` method returning `this`.
- **`.return()` and `.throw()`**: Must also wrap results in Promises per the spec.

**Effort:** Medium. All the machinery exists; it needs a new execution driver bridging the two existing paths.

---

## ES2018 — `for await...of`

**Status:** ❌ — no async variant of the `for...of` loop.

**What's needed:**

- **Compiler** (`Compiler.Statement.cs`): Detect `for await (... of ...)` — the `await` keyword between `for` and `(`. Emit a new `ForAwaitOfStep` opcode (appended to end of `OpCode` enum). Require the enclosing function to be async, otherwise throw a `SyntaxError`.
- **New opcode `ForAwaitOfStep`**: Like `ForOfStep` but after calling `.next()` it yields the resulting Promise back through the async drive loop, then on resume checks `done`. Must be appended to the end of the `OpCode` enum.
- **`GetAsyncIterator`** (or extend `GetIterator`): Checks for `[Symbol.asyncIterator]` first, falls back to `[Symbol.iterator]` wrapping sync iterables in an async adapter.
- **VM** (`VirtualMachine.cs`): `ForAwaitOfStep` handler calls `.next()`, wraps the result as a Promise, and yields it up the async stack — same mechanism `await` already uses — then on resume checks `done`.

**Effort:** Medium. Async-yield-and-resume machinery already exists; this connects `ForOfStep`'s loop structure to it.

---

## ES2018 — Unicode property escapes in RegExp (`\p{...}`)

**Status:** ❌ — `u`/`v` flags not handled; `\p{...}` patterns passed to .NET as-is which may throw or silently mismatch.

**What's needed:**

- **Lexer** (`ScriptLex.cs`): Accept the `u` (unicode) and `v` (unicode sets) flags and pass them through to the `ScriptVar` constructor.
- **`ScriptVar` regex constructor** (`ScriptVar.cs:261–288`): When `u` or `v` flag is present, drop `RegexOptions.ECMAScript` (incompatible with Unicode categories) and add `RegexOptions.CultureInvariant`. Translate JS `\p{...}` property names to .NET equivalents:
  - JS `\p{Letter}` → .NET `\p{L}`
  - JS `\p{Script=Greek}` → .NET `\p{IsGreek}` (.NET 7+ supports this natively, and DScript targets net8.0+)
  - JS `\P{...}` → .NET `\P{...}` (same translation, negated)
- A translation dictionary covering the common Unicode general categories and script names covers ~90% of real-world usage. Full coverage is a long tail.
- Note: the `v` flag's set notation (`[[\p{Letter}&&\p{ASCII}]]`) is a separate problem — .NET has no equivalent and would require a regex rewriter. That is tracked separately in the ES2024 row.

**Effort:** Small-to-medium for common properties; full Unicode property coverage is a long tail.

---

## ES2022 — RegExp `d` (indices) flag

**Status:** ❌ — flag not handled; result objects have no `.indices` property.

**What it does:** When `d` is present, `.exec()` and `.match()` results gain an `.indices` array of `[start, end]` offset pairs — one per capture group (index 0 = whole match) — and an `.indices.groups` object for named captures. The data already exists in .NET: `Match.Groups[i].Index` and `Match.Groups[i].Length`.

**What's needed:**

- **Lexer** (`ScriptLex.cs`): Accept `d` in the flags string and pass it through.
- **`ScriptVar` regex constructor** (`ScriptVar.cs:261–288`): Store a `hasIndices` flag when `d` is present. `RegexOptions.ECMAScript` has no equivalent flag — it is purely an output-shaping option, so no change to the compiled `Regex` is needed.
- **`exec()`/`match()` result builder** (wherever the match result `ScriptVar` is assembled after calling `.NET Regex.Match`): When `hasIndices` is true, build the `.indices` array alongside the existing captures — for each group `i`, append a two-element array `[group.Index, group.Index + group.Length]`. For named groups, also populate `.indices.groups.<name>` with the same pair.

**Effort:** Small. No regex compilation changes; purely output-side result object construction.

---

## ES2019 — Optional `catch` binding (`catch { }`)

**Status:** ⚠️ — `catch` without a binding variable compiles but the error value is inaccessible.

**Current state:** `CompileTry()` (`Compiler.Statement.cs:782–851`) requires an identifier after `catch (` — it unconditionally calls `lexer.Match(Id)`. The `EnterTry` opcode stores a `catchVarIdx` (a Names index, or -1 if absent), and `DispatchException` (`VirtualMachine.cs:1407–1413`) already skips binding when `catchVarIdx == -1`. So the VM already supports it; only the compiler needs fixing.

**What's needed:**

- **Compiler** (`Compiler.Statement.cs`): After matching `catch`, peek at the next token. If it is `(` followed by an identifier, parse as today. If it is `(` followed by `)` (empty binding), or if the `(...)` is omitted entirely, set `catchVarIdx = -1` and skip the name-parsing. Pass `-1` to `EnterTry`'s catch-var slot.
- No VM changes needed.

**Effort:** Tiny — a one-line compiler change with a guard.

---

## ES2019 — `Symbol.prototype.description`

**Status:** ⚠️ — description is stored in `scriptData` but not exposed as a `.description` property; only accessible as a side-effect of `.String` conversion.

**Current state:** `ScriptVar.CreateSymbol` (`ScriptVar.cs:104–111`) stores the description string in `scriptData`. `GetString` (`ScriptVar.cs:440–444`) formats it as `"Symbol(desc)"` but does not expose the raw string. `SymbolRegistrar.cs` adds a dummy `description` property to the Symbol constructor, not to Symbol instances.

**What's needed:**

- **`SymbolRegistrar.cs`**: Register a getter on the Symbol prototype that, when called on a symbol instance, returns `sym.scriptData as string ?? undefined`. The getter needs to type-check that `this` is a Symbol (throw `TypeError` if not).
- No changes to `ScriptVar` — the data is already there.

**Effort:** Small — a new getter registration in `SymbolRegistrar`.

---

## ES2019 — `Function.prototype.toString`

**Status:** ⚠️ — compiled functions return their source; native (C#-backed) functions return an empty string. No `Function.prototype.toString` method is registered at all — the behaviour falls through to `ScriptVar.GetString`.

**Current state:** `ScriptVar.GetString` (`ScriptVar.cs:447`) returns `VmFunction.Source` for compiled functions. For native functions no source exists. `FunctionFunctionProvider.cs` implements `call`/`apply`/`bind` but not `toString`. The name metadata is available via `fn.FindChild("name")`.

**What's needed:**

- **`FunctionFunctionProvider.cs`** (or a dedicated `Function.prototype` registrar): Register `Function.prototype.toString` as a native method that:
  - For compiled VM functions: returns `VmFunction.Source` (already stored on the chunk).
  - For native functions: returns the synthetic string `"function <name>() { [native code] }"` using the name from `fn.FindChild("name")`. This matches the ES spec convention and is what V8/SpiderMonkey produce.

**Effort:** Small — one new method registration; the data is already available for both paths.

---

## ES2023 — Hashbang (`#!`)

**Status:** ❌ — lexer does not recognise a `#!` prologue; a script starting with `#!/usr/bin/env node` will fail to parse.

**What it is:** A `#!` on the very first line of a script allows the file to be executed directly on Unix (`#!/usr/bin/env node`). The JS engine must silently discard everything from `#!` to the end of that line before lexing begins. No AST node, no runtime semantics — purely a skip.

**What's needed:**

- **Lexer** (`ScriptLex.cs` — `GetNextToken()`): At the top of the whitespace/comment-skipping loop, add a single guard: if we are at the very start of the source (`dataPos == dataStart + 2`, i.e. the two-character lookahead has just been primed) and `CurrentChar == '#'` and `NextChar == '!'`, advance past every character until `\n` or EOF, then `continue` the loop. No other changes needed.

**Effort:** Trivial — two or three lines in the lexer.

---

# Strict Mode Implementation Plan

## What it is

`"use strict"` is an ES5 directive prologue. When present at the top of a script or function body it opts that scope into stricter runtime and compile-time rules. Nested functions inside a strict scope are automatically strict.

---

## Phase 1 — Directive detection + `Chunk.IsStrict`

**Files:** `DScript/Vm/Chunk.cs`, `DScript/Compiler/Compiler.cs`, `DScript/Compiler/Compiler.Statement.cs`

- Add `bool IsStrict { get; set; }` to `Chunk`.
- At the start of `CompileProgram` and each function/arrow body, peek at the first statement. If it is a string-literal expression statement whose value is `"use strict"`, set `chunk.IsStrict = true` and consume the statement without emitting any bytecode.
- When compiling a nested function, propagate `IsStrict` downward: if the enclosing chunk is strict, the child chunk is automatically strict regardless of its own prologue.

---

## Phase 2 — Compile-time errors

**Files:** `DScript/Compiler/Compiler.Statement.cs`, `DScript/Compiler/Compiler.Factor.cs`

Throw a `SyntaxError`-equivalent at compile time when strict mode is active and any of the following are found:

| Case | Where to check |
|---|---|
| Duplicate parameter names (`function f(a, a)`) | Parameter list parsing |
| `delete <identifier>` (deleting a plain variable) | `CompileUnary` / `CompileFactor` |
| `eval` or `arguments` used as a binding name | `CompileDeclare`, assignment targets |
| Octal literals (`0777`) | Lexer / `CompileFactor` |

---

## Phase 3 — Undeclared variable assignment throws `ReferenceError`

**Files:** `DScript/Vm/VirtualMachine.cs` (or wherever `SetVar` is handled)

Currently `SetVar` on an unknown name walks the scope chain to the global object and creates the variable there. In strict mode the walk must throw `ReferenceError` when the name is not found in any scope. The current call frame exposes `chunk.IsStrict`; gate the throw on that flag.

---

## Phase 4 — `this` is `undefined` in plain function calls

**Files:** `DScript/Vm/VirtualMachine.cs` — `Call` opcode handler

When a function is called as a plain call (not a method call via `CallMethod`) and the callee chunk is strict, pass `undefined` as `this` instead of the global root object.

---

## Phase 5 — Property write `TypeError` for non-writable properties

**Files:** `DScript/Vm/VirtualMachine.cs` — `SetProp` opcode handler

`ScriptVar` already carries a `Writable` flag on property descriptors. Currently `SetProp` silently ignores a write to a non-writable property. In strict mode it must throw `TypeError`. Gate the throw on `chunk.IsStrict` in the current frame.

---

## Phase 6 — `arguments.callee` / `arguments.caller` throw `TypeError`

**Files:** Wherever `arguments` is constructed for a call frame

In strict mode, accessing `arguments.callee` or `arguments.caller` must throw `TypeError`. Add accessor properties to the `arguments` object that throw when the enclosing function is strict.

---

## Phase 7 — Block-scoped function declarations (large)

**Files:** `DScript/Compiler/Compiler.Statement.cs`, VM scope handling

In strict mode, function declarations inside blocks (`if`, `for`, etc.) must be block-scoped rather than hoisted to the enclosing function. This requires the compiler to emit block-level `DeclareLocal` for such declarations when strict and handle their initialisation at the declaration site. Deferring this to a separate milestone is reasonable.

---

## Effort summary

| Phase | Effort | Covers |
|---|---|---|
| 1 — Detection + `Chunk.IsStrict` | Small | Foundation for all later phases |
| 2 — Compile-time errors | Small | Duplicate params, `delete x`, octal, `eval`/`arguments` as names |
| 3 — Undeclared assignment `ReferenceError` | Medium | Most common strict-mode use case |
| 4 — `this = undefined` in plain calls | Small | Correct `this` binding |
| 5 — Non-writable property `TypeError` | Medium | Requires `SetProp` to check descriptor |
| 6 — `arguments.callee`/`.caller` | Small | |
| 7 — Block-scoped function declarations | Large | Defer to later milestone |

Phases 1–4 cover the cases most real-world code depends on and can ship together as a first pass. Phases 5–7 can follow independently.
