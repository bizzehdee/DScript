# Lever A — positional local-slot frames (design & phase plan)

The only path to the Objects/Closures/Classes variable-resolution cost (Objects spends ~46%
of self-time in name-based `JitGetVar`/`JitSetVar`). Four opportunistic shortcuts failed
(env-pooling, naive var-cache, interpreter+JIT block-env reuse ×2) because they all kept
resolving variables by name against environments that loops recreate per iteration. Slots
remove that dependency entirely: a local becomes `frame.Slots[i]`, read by index — no env
walk, immune to per-iteration block-env churn.

## Target model

- **Per function, the compiler assigns each parameter and local a slot index** (0..N-1) in a
  flat per-frame `ScriptVar[] Slots`. Access is `GetLocal i` / `SetLocal i` — O(1), no name.
- **Captured locals** (read by a nested closure) are **boxed into a `Cell`** (a 1-field heap
  box) so the closure and the frame share the live value after the frame returns. Only
  captured slots pay this; `RecyclableFrame` functions (no closures) have zero cells — the
  hot benchmarks are all-plain-slot.
- **Globals / `with` / `eval`-introduced / dynamically-resolved names** keep the existing
  name-based `GetVar`/`SetVar` path (fallback). Slots are an additive fast path.
- `this`, `arguments` get reserved slots; `arguments` only materialised when used.

## Why this is safe where shortcuts weren't

Slot access never consults an `Environment`, so block-scope env recreation, OSR env hand-off,
and const re-init (the things that broke the shortcuts) become irrelevant for slotted locals.
The block-env machinery remains only for the name-based fallback and for capture cells.

## Invariants

- A name resolves to a slot **iff** the compiler proved it is a lexical local of the current
  function (declared by param/`var`/`let`/`const` in this function, not shadowed by `with`/`eval`).
- Slot indices are stable for a chunk; baked into bytecode and both JIT back-ends.
- `Cell` identity is per-binding-instance: per-iteration `let` captured in a loop needs a
  fresh cell per iteration (the one case where per-iteration binding is observable — gated by
  capture, so rare and explicit).

## Phases (each: full suite green on net8.0+net10.0, bench best-of-N, own commit, revertible)

**A1 — Compile-time scope & slot analysis (metadata only, zero behaviour change).**
Add a scope tracker to the compiler: on entering a function, push a scope; record each
param/`var`/`let`/`const` with its slot index and block depth; detect captures (a name
resolved from an enclosing function scope by a nested function). Store on `Chunk`: slot
count, per-name→slot map, captured-slot set, `this`/`arguments` slots. Emit **nothing new**
yet — bytecode and runtime unchanged. Validate: suite green; add unit tests asserting the
slot map for representative functions. *Self-contained, low risk.*

**A2 — Interpreter slot frames.**
Allocate `Slots = ScriptVar[slotCount]` on the call frame. Compiler emits `GetLocal`/
`SetLocal`/`DeclareLocalSlot` (appended to opcode enum) for slotted names; `GetVar`/`SetVar`
remain for non-slotted. Interpreter handlers index `Slots`. Captured slots hold a `Cell`;
`GetLocal`/`SetLocal` on a captured slot read/write `cell.Value`. `MakeClosure` captures the
needed cells (not the whole env). `arguments`/`this` from reserved slots. Keep the env-based
path working for globals/eval. Risk: High — the binding model. Heavy tests: closures,
loops-capturing-`let`, recursion, generators/async, `arguments`, TDZ, `eval`.

**A3 — JIT both back-ends read slots.**
`ReflectionEmitJitCompiler` + `ClosureThreadedJitCompiler`: emit slot loads/stores
(`vm`/`args`/frame `Slots`) instead of `JitGetVar`/`JitSetVar` for slotted names; pass args
through the currently-unused `JitDelegate.args`. Captured slots go through cells. Risk: High.

**A4 — OSR slot frames.**
OSR resume must reconstruct/share the slot frame at the resume point. Risk: High.

**A5 — Cleanup / measure.**
Remove now-dead name-resolution fast paths where fully superseded; final bench; update
`performance-plan.md` and wiki (Bytecode/opcodes) per CLAUDE.md.

## Files touched (per the code map)

- Compiler: `Compiler.cs` (scope stack), `Compiler.Statement.cs` (decls/for/blocks),
  `Compiler.Factor.cs` (identifier load/store), `Compiler.Class.cs`.
- Opcodes: `OpCode.cs` (append `GetLocal`/`SetLocal`/`DeclareLocalSlot`/cell ops).
- Chunk: `Chunk.cs` (slot map, captured set, slot count).
- Runtime: `Environment.cs` (or a new `Frame`/`Cell`), `VmFunction.cs` (captured cells),
  `VirtualMachine.cs` (Invoke* frame alloc, opcode handlers, MakeClosure, arguments).
- JIT: `DynamicMethodBuilder.cs`, `ReflectionEmitJitCompiler.cs`,
  `ClosureThreadedJitCompiler.cs`, `JitDecoder.cs`.
- Serialization: `BytecodeSerializer.cs` (new opcodes + slot metadata).
- Tests: new `SlotFrameTests.cs`; extend closure/arguments/generator suites.

## Known hazards (call out explicitly when hit)

- `eval` can introduce bindings at runtime → any function containing direct `eval` must
  **disable slotting** (fall back to name-based) for safety.
- `with` (if supported) similarly disables slotting in its scope.
- Debugger variable view reads named bindings → must map slots back to names (use the slot
  map) or the debugger loses locals.
- `arguments` aliasing of params (non-strict) — DScript already doesn't implement it; keep
  parity.
- Generators/async suspend the frame → slot frame must live on the generator state, not the
  C# stack.
