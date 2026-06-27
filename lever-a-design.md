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

**A1 outcome (committed 6f64bb9):** built the *flat* metadata version (`Chunk.SlotMap`
name→slot, `SlotCount`, `CapturedSlots`, `SlotEligible`). Sufficient as a summary, but **not**
sufficient to drive A2 emission — see the constraint below.

**A2 correctness constraint (discovered before implementing).** Slot resolution **must happen
at emit time with a block-aware scope stack**, not as a post-pass bytecode promotion. A flat
name→slot map cannot tell a reference that lexically binds to a block-local from one outside
that block: `function f(){ { let x=1; } return x; }` shares one slot for both `x` tokens, so
promoting `return x` to `GetLocal` would read the block's stale slot instead of resolving
outward. Therefore A2 must:
1. Maintain a compile-time **scope stack** that mirrors runtime `EnterBlock`/`LeaveBlock`
   exactly (function-root scope + nested block scopes), with monotonic slot allocation
   (no slot reuse across sibling blocks → slot↔name is 1:1, needed for capture demotion).
2. At each identifier load/store, resolve the name through the current function's scopes; emit
   `GetLocal`/`SetLocal` only when it binds to a local declaration of the current function.
3. A per-*slot* capture-demotion post-pass (a captured slot's *all* occurrences —
   declaration + every reference — revert to name-based together, which is correct).
Conservative A2 gates (fall back to name-based): non-eligible function (`eval`), `main`/expr
chunk, `IsGenerator`/`IsAsync` (suspend/resume slot persistence deferred), `UsesArguments`
(params must stay reachable for the arguments object), captured slots. Params may stay
name-based in the first A2 cut (locals-only) to avoid changing call-site binding.

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

## A2 refinement — de-risked "all-or-none per-name" promotion (2026-06-27)

Investigation while extending the closure JIT settled the A2 emission strategy and surfaced
two constraints that reshape the phasing.

### Strategy: promote in a post-pass, all-or-none per name (no demotion, no divergence)

Earlier notes weighed emit-time slot opcodes + a capture-demotion post-pass. A simpler,
provably-safe alternative: keep the compiler emitting name-based `GetVar`/`SetVar`, then run a
**promotion pass after `AnalyzeSlotsAndCaptures` and before the optimizer** (so it only sees
wide `GetVar`/`SetVar`, never fused/narrow forms). For each chunk, rewrite *every* occurrence
of a name to `GetLocal`/`SetLocal` **only if the name is fully slottable**, else leave all
occurrences name-based. "Fully slottable" gates (any failure → name stays name-based):

- chunk is a function (not `<main>`/`<expr>`), `SlotEligible`, not generator/async;
- chunk has **≤1 `EnterBlock`** (no nested block scopes) → the flat `SlotMap` is 1:1 and
  resolution is unambiguous, sidestepping the block-shadowing problem entirely;
- name is a `let`/`var` local (not a parameter, not `const`) — track slottable-kind names in a
  per-chunk set at declaration time, since `SlotMap` alone can't distinguish `const`/params;
- name's slot ∉ `CapturedSlots`;
- name has **no use before its declaration** in bytecode order (linear scan) — otherwise the
  promoted read would see the (undefined) slot instead of the outer binding DScript currently
  resolves, changing observable behaviour.

Why this is safe: because promotion is all-or-none per name, a slotted local has **zero**
name-based accesses, so slot storage and the (now-dead) env binding can never diverge. No
capture-demotion bytecode rewrite is needed (captured names simply aren't promoted). Declares
are left untouched (the dead env binding is harmless; one `AddChild` per call, not per access).
TDZ is a non-issue: DScript does not implement TDZ (`DeclareLocal` creates a plain undefined
binding), so slots default to undefined with matching semantics.

Runtime: add `ScriptVar[] Slots` to `Environment`; block child envs (`EnterBlock`,
`JitEnterBlock`) share the parent's `Slots` reference so a function-wide slot is reachable at
any block depth. Allocate `Slots` in `InvokeCallable` when the entered chunk uses slots.
`GetLocal`/`SetLocal` index `env.Slots`. Both JITs read via `JitGetLocal`/`JitSetLocal`
helpers (same pattern as `JitGetVar`), so the closure back-end is a two-node change.

### Constraint 1 — A2 and A3 are inseparable (JIT-decline-on-unknown forces a vertical landing)

A back-end that meets an unknown opcode **declines the whole chunk** and the VM interprets it.
So the moment the compiler emits `GetLocal`, any chunk containing it stops being JIT-compiled
unless the back-end handles it. The hot functions are exactly the ones that get slotted, so
"emit slots now, teach the JIT later" would regress them to the interpreter. A2 (emit + run)
and A3 (JIT reads) must land **together**, and coverage (90% gate) means the first commit must
be the full vertical slice: compiler-emit → interpreter → both JIT decoders/back-ends → tests.

### Constraint 2 — the Reflection.Emit back-end is multi-tier (~10 var-handling sites)

`ReflectionEmitJitCompiler` is not a single lowering: it has multiple tiers/strategies, each
with its own `PushVar` handling (`varLocals`, `regs`, `argTemps`, `EmitLoadLocal`,
`EmitLoadNamedVar` — ~10 sites). It already promotes vars to IL locals internally. Adding
slot support there is a substantial, error-prone change that risks the **default** build.

### Resulting recommendation for the first landing

Two viable shapes, an explicit architectural choice for the repo owner:

1. **Clean / both back-ends (larger):** implement slot reads in the Reflection.Emit back-end's
   tiers too, so the default build also benefits (the doc's original intent). Bigger, riskier,
   touches the multi-tier JIT.
2. **AOT/closure-only first cut (smaller, but with smells):** gate promotion behind a flag
   enabled only for the closure/AOT path; the Reflection.Emit back-end **declines** any chunk
   containing `GetLocal`/`SetLocal` (3-line guard) so the default build is byte-for-byte
   unchanged and never miscompiles slots. Cost: a global compile flag (global mutable state),
   build-dependent bytecode, and divergence from the both-back-ends vision.

The closure-JIT control-flow/OSR/block-scope/inlining work (commits on
`closure-jit-control-flow`) already took the AOT/closure Functions workload 4543 → ~2280 ms;
slots are the next lever for the residual name-resolution + boxed-arithmetic cost.
