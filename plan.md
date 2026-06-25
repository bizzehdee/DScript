# DScript JIT — Further Features & Performance Plan

## Background

The JIT is feature-rich already: opt-in, two back-ends (`ReflectionEmitJitCompiler`
emitting IL, `ClosureThreadedJitCompiler` with no reflection / AOT-safe) sharing a
`JitDecoder` front-end. The Reflection.Emit back-end has speculative unboxed int/double
tiers and a conservative boxed tier, with deoptimization, opt-in background
compilation, per-site bimorphic property inline caches, and monomorphic + bimorphic
inlining of small pure-parameter leaf callees. It compiles straight-line and
control-flow (`if`/`while`/`for`) functions, local variables/assignments, property
reads/writes, indexing, unary/shift ops, and plain calls.

Two earlier ideas were investigated and **closed with negative results** (do not
re-attempt blindly): call-frame *environment* pooling (conflicts with the
env-identity inline cache) and migrating the operand stack to the tagged `Value`
struct (correct, but a measured ~55 % interpreter regression — the struct is 3–4× a
reference on the hottest data structure; only NaN-boxing into 8 bytes would be
size-neutral, which C# can't do for managed refs).

This plan adds the remaining worthwhile work, in two groups.

- **Group A — Coverage**: constructs the decoder currently *declines*, so functions
  containing them run fully interpreted. Each is an incremental add following the
  established pattern (decoder → interpreter-identical helper → both back-ends →
  parity tests). Control-flow-bearing additions are Reflection.Emit-only (the closure
  back-end declines all control flow).
- **Group B — Performance**: make already-compilable code faster.

Each phase keeps the invariant: every compiled path is validated JIT-on vs JIT-off
for identical results, the 90 % coverage gate holds, and performance phases record
before/after `DScript.Benchmark` numbers (>5 % interpreter regression blocks a commit).

---

# Group A — Coverage

## Phase 1 — Short-circuit & ternary (`&&`, `||`, `??`, `?.`, `?:`)

**Goal:** compile the most common currently-declined construct. These lower to the
conditional-pop jump opcodes (`JumpIfFalseOrPop`, `JumpIfTrueOrPop`,
`JumpIfNullOrUndefined`, `JumpIfDefined`).

**Why it's blocked today:** `VerifyStackConsistency` (and the decoder) assume every
jump pops uniformly. These opcodes have a **branch-vs-fall-through stack-effect
difference** — e.g. `JumpIfFalseOrPop` keeps the operand on the taken edge (`&&`/`||`
result) but pops it on the fall-through; `JumpIfNullOrUndefined` pushes on both edges.

**Design:** decode them into instructions carrying their target; extend the stack
verifier's worklist to apply a **per-edge** stack delta (branch edge vs fall-through
edge) instead of one net effect; emit the matching IL (duplicate/peek the condition,
test truthiness, conditional branch, conditional pop). The flat IL model already
tolerates consistent non-zero merge depths, so no slot-locals rewrite is needed —
only correct per-edge depth bookkeeping.

**Deliverables:** decoder support + per-edge verifier + emission (Reflection.Emit;
closure declines); tests for `a && b`, `a || b`, `a ?? b`, `o?.x`, `c ? a : b`,
nested/chained, matching the interpreter.

## Phase 2 — Method calls (`obj.m(...)`)

**Goal:** compile method-call sites — the dominant call shape in OO code.

**Design:** support `GetPropMethod`/`GetPropCall0` (which leave `[receiver, fn]` on the
stack) and `CallMethod`/`TailCallMethod`'s non-tail form, dispatching via
`vm.InvokeCallable(callee, receiver, args)` (receiver = `this`). Reuse the existing
call machinery; the new piece is preserving the receiver through the call. Extend the
inliner later if a method callee is a pure-parameter leaf.

**Deliverables:** decoder (`GetPropMethod(N)`, `GetPropCall0(N)`, `CallMethod`),
method-dispatch emission with receiver binding (both back-ends), tests (own + inherited
method, zero-arg, chained `a.b().c()`).

## Phase 3 — Object & array literals (`{…}`, `[…]`)

**Goal:** compile functions that build small objects/arrays.

**Design:** runtime helpers mirroring the opcodes — `NewObject`→`CreateObject`,
`InitProp`→`AddChild` on the peeked object, `NewArray`→`CreateArray`,
`InitElem`→`SetArrayIndex`. Straight-line, so both back-ends.

**Deliverables:** helpers + decoder + emission (both back-ends) + tests (object
literal, array literal, nested, computed/spread declined).

## Phase 4 — `let`/`const` block scopes

**Goal:** compile functions using block-scoped declarations (today only `var` works;
`let`/`const` introduce `EnterBlock`/`LeaveBlock` and decline).

**Design:** track the current environment in an IL local; `EnterBlock` calls a helper
that pushes a child block-scope env (and stores it in the local), `LeaveBlock`
restores the parent. `JitGetVar`/`JitSetVar`/declares operate on the current-env
local rather than the delegate's `env` parameter.

**Deliverables:** block-env helpers + current-env tracking + decoder/emission + tests
(`for (let i …)`, nested blocks, shadowing).

## Phase 5 — Tail calls (`return f(x)`)

**Goal:** compile functions ending in a tail call (currently declined).

**Design / risk:** `TailCall` uses an interpreter trampoline for unbounded tail
recursion that compiled code can't reproduce. Safe options: (a) compile a tail call as
a normal `InvokeCallable` + `return` — correct for bounded depth, but a deeply
self-tail-recursive function would overflow; so **decline when the tail callee may be
the current function** (self-recursion), compiling only non-self tail calls; or
(b) signal the existing trampoline from compiled code. Start with (a).

**Deliverables:** tail-call detection + safe emission + tests (non-recursive tail call
matches; self-tail-recursion still declines/interprets correctly).

> **Deferred coverage (larger, lower priority):** `for..of` (iterator protocol),
> `try`/`catch` (exception mapping), `new`/constructors. Tackle after Group A's
> higher-value items if demand appears.

---

# Group B — Performance

## Phase 6 — Speculative unboxed *loop* tier (the headline perf win)

**Goal:** hot numeric **loops** flow raw `int`/`double` through IL with no per-iteration
boxing. Today the speculative unboxed tiers only handle straight-line code (they reject
jumps and assignments), so a hot `for(…){ s += i; }` compiles *conservatively (boxed)* —
missing the unboxing exactly where it matters most.

**Design (hard):**
- **Eligibility:** a pure (call-free) function whose loop variables are profiled
  int-only (or double-only), with supported control flow and assignments only.
- **Unboxed value flow through control flow:** values flow as raw `int`/`double` in
  depth-indexed IL slot-locals (so branches are valid); the loop's accumulator/counter
  are promoted to IL `int`/`double` locals (register allocation), read/written without
  boxing.
- **Guards + deopt:** guard each variable's type once on entry; a mid-loop type
  surprise deopts — but deopt must occur with a *clean* operand model, so the function
  re-runs interpreted (safe because pure). Division/overflow handled as in the
  straight-line int tier.
- Box only at the function boundary (return / escape).

**Deliverables:** loop-tier eligibility, unboxed control-flow + register-local
emission, deopt path, parity tests (int and double loops, nested, accumulator), and a
benchmark showing the hot-loop speedup over the conservative tier.

## Phase 7 — Inlining beyond pure-parameter leaves

**Goal:** inline more of the call graph.

**Design:** relax `TryGetInlineBody` to allow (a) callees with **control flow** (splice
with a fresh label set using the slot model) and (b) callees that read **globals**
(resolved through the caller-reachable global scope at runtime). Still decline callees
that capture their defining environment (the inlined body has no access to it).

**Deliverables:** relaxed eligibility (control flow, then globals), guard + fallback
unchanged, tests (branchy helper inlined; global-reading helper inlined; closure-
capturing helper still falls back), benchmark.

---

## Research / unscheduled (high-risk or large; not tasked)

- **NaN-boxed `Value`** — the only size-neutral fix for the abandoned operand-stack
  migration: pack values into 8 bytes with an object handle/side-table for references.
  Big, C#-awkward (can't store a managed ref in a `double`), and the side-table
  indirection may regress object-heavy code. A dedicated research project.
- **Contiguous register frame** — replace per-call `Environment`/vars objects with a
  register stack to attack the call-frame allocation bottleneck. Large VM redesign.
- **Megamorphic (3+) inline caches**, **OSR** (deferred — narrow ROI), **deopt
  re-profiling**, a **compound-assign interpreter superinstruction** — marginal.

---

## Cross-cutting concerns

- **Correctness:** every compiled path validated JIT-on vs JIT-off for identical
  results, across both back-ends where applicable (`JitCodeGenTestsBase` matrix).
  Control-flow features are Reflection.Emit-only; the closure back-end declines them.
- **Coverage:** maintain the 90 % line-coverage gate.
- **Benchmarks:** Group B phases (6, 7) record before/after `DScript.Benchmark`
  numbers; a >5 % interpreter regression blocks the commit.
- **Opcodes:** no new VM opcodes are expected; if any are added, append to the enum end.
- **Platform:** Windows/Linux/macOS; no platform-specific APIs.
