# DScript — Implementation Task List

Tasks are ordered to minimise blocked work: pure compiler changes come first
(lowest risk, no VM churn), then incremental VM additions, then the large
architectural features. Performance tasks are interleaved where they can be done
without disrupting ongoing language work.

Status: `[ ]` todo · `[~]` in progress · `[x]` done

See `plan.md` for full design notes on each item.

---

## Phase 1 — Quick compiler wins (no new opcodes)

These are pure parser / code-generation changes. Each can be done and shipped
independently in a day or two.

- [ ] **Short-hand object properties** — `{ x, y }` sugar in `CompileObjectLiteral`
- [ ] **Tiered constant folding (P1)** — fold constant chains in the optimizer (`Chunk.cs`)
- [ ] **Default parameter values** — `function f(x, y = 0)` guard at function entry
- [ ] **`class` syntax** — desugar to constructor + prototype assignments (no new opcodes)
- [ ] **REPL** — `DScript.Repl` console project wrapping a persistent `ScriptEngine`

---

## Phase 2 — New tokens / short-circuit operators

Small lexer additions and new compile paths. No new opcodes except two cheap
jump variants.

- [ ] **`?.` optional chaining** — member, index, and call forms; new `JumpIfNullOrUndefined` opcode
- [ ] **`??` nullish coalescing** — short-circuit with same new jump opcode
- [ ] **Computed object properties** — `{ [expr]: val }`; new `SetPropDynamic` opcode

---

## Phase 3 — Block scoping

Moderate VM change that improves correctness and unlocks `let`/`const` semantics.
Do before destructuring so destructuring can emit `DefineLocal` correctly.

- [ ] **`EnterBlock` / `LeaveBlock` opcodes** — push/pop a scope frame on the env chain
- [ ] **`let` declarations** — compile to `DefineLocal` in the innermost block frame
- [ ] **`const` immutability** — flag the binding; runtime error on reassignment
- [ ] **String interning for name indices (P2)** — good time to add while touching env lookup

---

## Phase 4 — Destructuring and rest/spread

Build on block scoping. Destructuring is foundational; rest/spread builds on top.

- [ ] **Array destructuring in `var`/`let`/`const`** — `const [a, b] = arr`
- [ ] **Object destructuring in `var`/`let`/`const`** — `const { x, y } = obj`
- [ ] **Destructuring with defaults** — `const { x = 0 } = obj`
- [ ] **Destructuring in assignment** — `[a, b] = [b, a]`
- [ ] **Destructuring in function parameters** — `function f({ x, y }) { ... }`
- [ ] **Rest parameters** — `function f(...args)` → `MakeRestArray` opcode at entry
- [ ] **Spread in array literals** — `[...a, ...b]` → `PushSpread` opcode
- [ ] **Spread in object literals** — `{ ...defaults, ...overrides }` → `MergeObject` opcode
- [ ] **Spread at call sites** — `fn(...arr)` → pre-call unpack step

---

## Phase 5 — Performance pass

With the language surface stable, sharpen the runtime before tackling the larger
architectural features.

- [ ] **Call-frame allocation pool (P3)** — extend `BorrowFrameVars` pool to non-recyclable frames
- [ ] **Tail-call elimination (P5)** — `TailCall` opcode; detect tail position in compiler
- [ ] **Inline property cache (P4)** — shape-versioned cache for `GetProp` / `SetProp`

---

## Phase 6 — Source maps

Low-risk infrastructure work before generators change the execution model.

- [ ] **Collect column info in the lexer** — store column alongside line in line-number table
- [ ] **Emit `.dsmap` sidecar** — VLQ-encoded offset → (file, line, col) in `BytecodeSerializer`
- [ ] **Load and expose source maps** — `BytecodeSerializer.LoadWithSourceMap`; surface in `DebugLocation`

---

## Phase 7 — Generators and iterators

The biggest VM change in the plan. Tackle after the runtime is optimised and the
language surface is settled.

- [ ] **`GeneratorObject` type** — holds saved `(ip, stack slice, env)` per generator instance
- [ ] **`Yield` opcode** — suspend current frame, return value to caller
- [ ] **`Resume` opcode** — restore saved state and continue execution
- [ ] **`function*` syntax** — compiler emits wrapper that returns a `GeneratorObject`
- [ ] **Iterator protocol** — any object with `next()` is iterable
- [ ] **`for...of` loop** — compiler desugars to iterator `next()` calls

---

## Phase 8 — `async` / `await`

Depends on generators (same suspension mechanism).

- [ ] **Lightweight `Promise` type** — resolve/reject callbacks, chainable `.then`
- [ ] **Micro-task queue** — inside the VM or host-supplied scheduler
- [ ] **`async function` syntax** — wraps body in generator-style state machine
- [ ] **`await` expression** — yields to the scheduler; resumes on resolution
- [ ] **Top-level `await`** — usable at module scope (depends on module system)

---

## Phase 9 — Module system

- [ ] **`require(path)`** — CommonJS-style; simpler stepping stone, no new syntax
- [ ] **Host resolver API** — `(importPath, currentModule) → Chunk` callback on `ScriptEngine`
- [ ] **Module execution cache** — keyed by resolved path; re-exports without re-running
- [ ] **`export` declaration** — writes to `__exports__` scope object
- [ ] **`import { x } from "..."` syntax** — calls resolver, reads export namespace
- [ ] **`export default`** — sugar for `__exports__.default = expr`
- [ ] **`import * as ns from "..."`** — binds the whole export namespace

---

## Phase 10 — Language Server Protocol

Long-running, can be started in parallel with any phase above.

- [ ] **`DScript.LanguageServer` project skeleton** — stdio LSP transport, JSON-RPC dispatcher
- [ ] **Syntax error diagnostics** — pipe compiler errors into LSP `textDocument/publishDiagnostics`
- [ ] **Document sync** — incremental text updates, re-compile on change
- [ ] **Hover** — surface variable type / value at cursor position
- [ ] **Go-to-definition** — resolve identifier to declaration site
- [ ] **Completion** — variables in scope, object properties, function signatures
- [ ] **Signature help** — parameter hints while typing a function call
- [ ] **VS Code extension** — thin extension that launches the language server
