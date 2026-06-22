# DScript — Optimisation Task List

Tasks are ordered by effort-to-impact ratio: quick wins first, then medium
refactors, then the large architectural change. Each phase is independent and
can be committed separately.

Status: `[ ]` todo · `[~]` in progress · `[x]` done

See `plan.md` for full design notes on each item.

---

## Phase 1 — Trivial peephole fix (< 1 hour)

- [ ] **`GetProp` cache hash** — replace `nameIndex & 0xFF` with
  `(uint)nameIndex * 2654435761u >> 24` in the `GetProp` handler
  (`VirtualMachine.cs`). Reduces false cache evictions for property-heavy code.

---

## Phase 2 — Small compiler change (half a day)

- [ ] **Compound-assignment `BinaryIntConst` peephole** — after the `Binary` emit
  in the compound-assignment branches of `CompileIdentifierChain` and
  `CompileMemberChain` (`Compiler.Factor.cs`), call `TryFuseConstantBinary` +
  `TryUpgradeBinaryConstToInt` so that `i++`, `i--`, `x += 1`, `x -= 1`, etc.
  use the existing inline-int fast path.

---

## Phase 3 — VM helper refactor (1 day)

- [ ] **Spread single-pass helper** — add `ExtractArrayElements(ScriptVar arr)`
  to `VirtualMachine.cs` that walks `FirstChild`/`Next` once into a `ScriptVar[]`.
  Replace the per-element `FindChild(IndexName(i))` loop in `PushSpread`,
  `MergeObject`, `CallSpread`, and `CallMethodSpread`. Eliminates O(n²) list
  traversals for spread operations.

---

## Phase 4 — Compiler + VM medium change (1–2 days)

- [ ] **Array literal spread: single-pass parse** — remove the lexer-clone pre-scan
  in `CompileArrayLiteral` (`Compiler.Factor.cs`); use a `bool seenSpread` flag
  instead. Emit `Dup; GetProp "length"` only once on first spread encounter, not
  per non-spread element.
- [ ] **Array literal spread: compile-time insertion index** — track static element
  count before a spread as a compile-time `int`; push as a `Constant` instead of
  reading `arr.length` at runtime for the common static-prefix case.

---

## Phase 5 — New opcode: `ForOfStep` (1–2 days)

- [ ] **Add `ForOfStep` opcode** to `OpCode.cs`; add 1-byte entry to
  `InstructionSize`; add to `EliminateDeadCode` non-jump set.
- [ ] **Implement `ForOfStep` in VM** (`VirtualMachine.cs`) — pops iterator, calls
  `.next()` natively, pushes value and done-flag without going through full `Call`
  dispatch.
- [ ] **Emit `ForOfStep` in compiler** — replace the
  `GetVar/GetProp next/Call/GetProp done/GetProp value` sequence in `CompileForOf`
  (`Compiler.Statement.cs`) with a single `ForOfStep` + `JumpIfTrue`.
- [ ] **Tests** — add `ForOfTests` cases that verify correctness of `ForOfStep` for
  arrays, generators, and objects with a hand-written `next()` method.

---

## Phase 6 — Large architectural change (3–5 days)

- [ ] **Stackless generator: compiler analysis** — add a `IsSimpleGenerator(chunk)`
  check that returns true when the function body contains no `await`, no closures
  over mutable upvalues that cross a `yield`, and no try/catch spanning a `yield`.
- [ ] **Stackless generator: segment splitting** — in `CompileFunctionRest` when
  `isGenerator && IsSimpleGenerator`, split the body at each `yield` into numbered
  inner `Chunk` segments stored in `chunk.GeneratorSegments`.
- [ ] **Stackless generator: `GeneratorState` type** — new class in
  `DScript/Vm/GeneratorState.cs` holding `(int segmentIndex, ScriptVar[] locals,
  ScriptVar lastYield)`. The `.next()` method runs the next segment synchronously
  and returns `{value, done}`.
- [ ] **VM: route simple generators to `GeneratorState`** — in `InvokeCallable`,
  when `chunk.IsGenerator && chunk.GeneratorSegments != null`, create a
  `GeneratorState` instead of a `GeneratorObject` (thread-based path kept as
  fallback).
- [ ] **Tests** — verify correctness parity between stackless and thread-based paths;
  benchmark thread overhead reduction on a `for...of range(10000)` loop.
