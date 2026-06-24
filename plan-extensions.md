# DScript JIT & VM — Extensions Plan (Tiers 1–3)

## Background

The base JIT is complete (`plan.md` / `tasks.md`, T01–T29): an opt-in, two-backend
JIT (`ReflectionEmitJitCompiler` + `ClosureThreadedJitCompiler`) sharing a
`JitDecoder` front-end, with speculative unboxed int/double tiers, a conservative
boxed tier, deoptimization, and a per-site property inline cache. It compiles
**straight-line** functions only; anything with control flow, assignments, or calls
beyond plain dispatch is declined and interpreted. Benchmarks show ~1.4–1.6× on
arithmetic; the profiler flags **call-frame allocation** as the VM's dominant cost.

This plan extends that base in three tiers:

- **Tier 1 (Phases 7–9)** — broaden what the JIT compiles: control flow, local
  state, and call inlining. Turns "straight-line leaf functions" into "most ordinary
  functions" and attacks the call-frame allocation bottleneck.
- **Tier 2 (Phases 10–11)** — root-cause VM performance that helps the interpreter
  *and* the JIT: call-frame pooling and a value-representation overhaul.
- **Tier 3 (Phases 12–16)** — incremental coverage, caching, and observability.

Each phase keeps the invariant established in the base work: **every JIT change is
validated JIT-on vs JIT-off for identical results, across both backends where
applicable**, and the 90 % coverage gate holds.

---

# Part A — Tier 1: broaden JIT coverage

## Phase 7 — Control flow (branches, loops, OSR)

**Goal:** compile chunks containing jumps — `if`/`else`, `while`/`for`, ternary,
`&&`/`||`, `switch` — which today are declined outright. This is the single biggest
coverage limiter: most hot code has a loop or a branch.

### The operand-stack-at-branch problem

IL requires a statically consistent evaluation-stack depth at every branch target.
The current emitter keeps operands on the IL eval stack, which only works for
straight-line code. The clean, fully-general fix is to **model the operand stack as
depth-indexed IL locals**:

- Compute the abstract stack depth at every instruction (straightforward: each
  opcode has a fixed stack effect; well-formed bytecode is balanced).
- Allocate one IL local per stack slot (`slot0..slotMax`, typed `ScriptVar`, or the
  unboxed type in a speculative tier).
- `push` → `stloc slot[sp++]`; `pop` → `ldloc slot[--sp]`.
- Nothing lives on the IL eval stack across a branch, so jumps become trivial IL
  `br`/`brtrue`/`brfalse` to labels — ternary, short-circuit operators, and loop
  joins all work without special cases.

This is a rewrite of the conservative emitter's value model (IL-stack → slot-locals).
It is slightly slower per op than raw IL-stack code but is the standard, robust
approach and unblocks all control flow. The speculative tiers keep their existing
(faster) flat model for the straight-line case and only adopt slot-locals when a
chunk has branches.

### Decoder & instruction set

`JitDecoder` stops declining jumps. It emits new `JitInstruction`s — `Jump`,
`JumpIfFalse`, `JumpIfTrue`, and the OrPop / defined / null variants — each carrying
a **resolved target index into the instruction list** (the decoder builds a
bytecode-offset → instruction-index map in one pass). Both back-ends consume them;
the Reflection.Emit back-end maps each to an IL label.

### Closure back-end

Arbitrary jumps don't fit the closure backend's expression-tree model. It either
(a) keeps declining jump-containing chunks (control flow stays Reflection.Emit-only),
or (b) gains a *structured* subset (compile `if`/`while` whose shape is recognisable
into nested closures). Start with (a); (b) is optional.

### OSR (revived from the old Phase 5)

Once loops compile, long-running loops that never return can tier up mid-execution.
Add the `Jump`-handler trigger: when `BackEdgeCount` crosses the threshold and the
chunk is `Cold` with a compiler registered, compile it and transfer the live
interpreter frame into the compiled code at the loop header (an `OsrEntryFrame`
carrying the current locals/stack, with a per-loop-header IL entry prologue).

**Deliverables:**
- `JitDecoder` jump decoding + offset→index resolution
- Slot-locals value model in `ReflectionEmitJitCompiler` (used when branches present)
- Branch/label emission; conditional-jump lowering
- OSR trigger + entry prologues (`OsrEntryFrame`)
- Tests: `if`/loop/ternary/short-circuit functions match the interpreter; nested
  loops; an accumulating loop; OSR fires on a long loop and produces the right result

---

## Phase 8 — Local variables & assignments

**Goal:** compile functions that mutate state — `let`/`var` locals, `SetVar`,
`SetProp`, compound assignment — so loops with accumulators (the common hot shape)
can compile. Today any assignment opcode declines.

### Approach

- **`SetVar`** via a runtime helper `VirtualMachine.JitSetVar(env, name, value)`
  mirroring the `SetVar` opcode (resolve + `ReplaceWith`; strict-mode `ReferenceError`;
  non-strict global create + version bump). Correct and complete; does not yet promote
  locals to registers.
- **`DeclareVar`/`DeclareLocal`/`DeclareConst`** via helpers that mirror the opcode
  env mutation.
- **`SetProp`** via `VirtualMachine.JitSetProp(obj, name, value, strict)` mirroring
  `SetMember` (and invalidating/refreshing the relevant inline-cache cell on the
  shape change).
- Decoder emits `SetVar`/`SetProp`/`Declare*` instructions; both back-ends lower them.

### Optimisation (later, optional)

Promote **non-captured** locals to IL locals (register allocation) instead of going
through the env, eliminating the `FindChild`/`ReplaceWith` per access. Requires
escape/capture analysis (a local is promotable iff no nested closure captures the
frame). Gated behind that analysis; the helper path remains the correct fallback.

**Deliverables:**
- `JitSetVar` / `JitSetProp` / declare helpers + decoder/back-end lowering
- Speculative tiers updated to allow assignments to numeric locals where safe
- Tests: accumulator loops, property mutation, compound assignment, strict-mode
  reference errors — all matching the interpreter on both back-ends

---

## Phase 9 — Monomorphic call inlining

**Goal:** inline small, stable monomorphic callees into the caller, eliminating the
`InvokeCallable` call-frame allocation (new `Environment` + vars object + arg binding)
— the documented VM bottleneck. Biggest win for call-heavy code (e.g. `fib`).

### Constraints (callee is inlinable iff)

- the call site is monomorphic and the callee is a non-native VM function;
- the callee body is itself JIT-eligible and within a size budget;
- the callee does not create a closure over its frame, use `arguments`, `eval`, or
  recurse unboundedly at this site (bounded inline depth);
- parameters can be rewritten to caller-side IL locals (no `this`/receiver coupling
  beyond what we can supply).

### Mechanism

At a monomorphic `Call`, emit a guard `runtimeCallee == bakedCallee`; on hit, inline:
evaluate args into fresh locals, splice the callee's compiled body with its parameter
reads rewritten to those locals and its `return` rewritten to "produce a value, jump
to the continuation". On guard miss or any non-inlinable condition, fall back to the
existing `InvokeCallable` dispatch. Inlining reuses the Phase 7 slot-locals model and
the Phase 8 local handling.

This is the most intricate phase; depth and size budgets keep code growth bounded.

**Deliverables:**
- Inlining decision (profile + eligibility + budget) in the compiler
- Body-splicing with parameter/return rewriting; identity guard + `InvokeCallable`
  fallback
- Tests: inlined monomorphic call matches interpreter; guard miss falls back; bounded
  recursion; a deep call chain shows reduced allocation (benchmark delta recorded)

---

# Part B — Tier 2: root-cause VM performance

## Phase 10 — Call-frame pooling & escape analysis

**Goal:** cut call-frame allocation for the (still large) set of functions that are
*not* JIT-inlined — helping the interpreter directly. Extends the existing
`RecyclableFrame` / `MakesClosure` machinery.

### Approach

- Strengthen escape analysis: a call frame's `Environment` + vars object may be
  pooled and reused when nothing escapes it (no closure capture, no returned
  reference to locals, no `arguments` leak).
- Maintain a per-VM pool of frame objects; borrow on call entry, return on exit.
- Clear borrowed frames safely (no stale child links) to avoid the cycle/leak hazards
  called out in the repo's safety rules.

**Deliverables:**
- Expanded escape analysis + frame pool
- Tests: recursion and call-heavy code produce correct results with fewer allocations
  (benchmark alloc-MB delta recorded); closures still capture correctly

---

## Phase 11 — Value-representation overhaul (deferred / high-risk)

**Goal:** eliminate the heap `ScriptVar` allocation per intermediate value — the root
cause the speculative tiers work around per-feature. The deepest lever and the
biggest refactor; sequenced last and broken into independently-shippable steps.

### Direction

Introduce a compact value representation for the hot path — either a NaN-boxed
`readonly struct Value` (doubles/ints/bools/null/undefined inline; objects/strings as
a boxed reference) or a tagged union — used by the VM operand stack and arithmetic,
with `ScriptVar` retained for object identity/property storage. Migrate incrementally:

1. Introduce `Value` + conversions to/from `ScriptVar`; no behaviour change.
2. Migrate the interpreter operand stack and arithmetic opcodes to `Value`.
3. Migrate the JIT speculative tiers to flow `Value` (subsumes the unboxed tiers).
4. Migrate property/array storage boundaries.

Each step is benchmarked and fully tested before the next. **High risk** (`ScriptVar`
is a `sealed class` threaded through the whole codebase, and the repo has explicit
object-graph/cycle safety rules) — treat as a research spike first.

**Deliverables (per step):** the `Value` type + conversions; migrated subsystem;
parity tests; benchmark before/after. Ship steps independently; abandon-safe.

---

# Part C — Tier 3: incremental improvements

## Phase 12 — Expanded JIT opcode coverage
Add the remaining safe opcodes now that control flow exists: array index get/set,
`typeof`, `BitNot`, shifts, `Negate` (handled correctly per `ScriptVar` numeric
rules), `instanceof`/`in` via helpers, template literals. Each: decoder + both
back-ends + parity tests.

## Phase 13 — Polymorphic inline caches
Upgrade the monomorphic call guard (T14) and the property inline cache (T28) to
**bimorphic** (two baked entries before falling back to megamorphic dispatch), using
the existing call-site morphism profiles. Tests + benchmark on a 2-callee / 2-shape
workload.

## Phase 14 — Background compilation
Compile hot chunks on a worker thread so the compile pause doesn't stall execution:
mark `Compiling`, hand off, install `CompiledDelegate` when ready (the VM keeps
interpreting until then). Must be thread-safe against `JitRegistry` and chunk state.
Tests: concurrent compile doesn't corrupt state; result correct; no double-compile.

## Phase 15 — JIT diagnostics & observability
A diagnostics API exposing, per chunk: JIT state, chosen tier, deopt count, and
*why a chunk was declined* (which opcode/condition). Invaluable for tuning thresholds
and finding missed compilations. Optional CLI/REPL surface. Tests on known
compile/decline/deopt cases.

## Phase 16 — Interpreter dispatch optimisation
For chunks that never JIT (declined, or below threshold), reduce interpreter dispatch
cost: computed-goto-style dispatch where the runtime supports it, and a few more
loop-shaped superinstructions. Benchmark-gated (must not regress); helps the
interpreted majority. Lowest priority.

---

## Cross-cutting concerns

- **Correctness**: every compiled path validated JIT-on vs JIT-off for identical
  results; the `JitCodeGenTestsBase` matrix runs against both back-ends. Control flow
  is Reflection.Emit-first; the closure back-end declines what it can't model.
- **Coverage**: maintain the 90 % line-coverage gate; `[ExcludeFromCodeCoverage]`
  (with justification) only for genuinely unreachable infrastructure.
- **Benchmarks**: Phases 7, 9, 10, 11, 13, 16 each record before/after
  `DScript.Benchmark` numbers (the `JitSection` for JIT phases; alloc-MB for VM
  phases). A >5 % interpreter regression blocks the commit.
- **Opcodes**: any new VM opcode is appended to the end of the enum (never inserted).
- **Platform**: Windows/Linux/macOS; no platform-specific APIs; background
  compilation must be safe on all three.
- **Safety**: no new `ScriptVar` graph cycles; pooled frames cleared before reuse.
- **Docs**: update `wiki/JIT.md` as coverage and back-end behaviour change.
