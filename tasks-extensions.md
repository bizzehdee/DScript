# DScript JIT & VM — Extensions Task List (Tiers 1–3)

Companion to `plan-extensions.md`. Continues the numbering from `tasks.md`
(base JIT = T01–T29, complete). Each task names its direct predecessors under
**Depends on**; tasks depending only on completed base work cite the base task.

---

# Part A — Tier 1: broaden JIT coverage

## Phase 7 — Control flow (branches, loops, OSR)

### T30 — Decoder: jump decoding + offset→index map
**File:** `DScript/Jit/JitDecoder.cs`, `DScript/Jit/JitInstruction.cs`
**Work:** Stop declining jump opcodes. Add `Jump`, `JumpIfFalse`, `JumpIfTrue`
(and OrPop / `JumpIfDefined` / `JumpIfNullOrUndefined`) `JitInstruction`s carrying a
target **instruction index**. Build a bytecode-offset → instruction-index map in one
pass and resolve targets. Still decline `EnterTry`, generators/async, and unsupported
opcodes.
**Depends on:** T16 (decoder)

### T31 — Slot-locals value model in the conservative emitter
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`, `DScript/Jit/DynamicMethodBuilder.cs`
**Work:** When a chunk contains branches, compute per-instruction abstract stack
depth and lower the operand stack to depth-indexed IL locals (`push`→`stloc`,
`pop`→`ldloc`). Straight-line chunks keep the existing flat IL-stack model.
**Depends on:** T30

### T32 — Branch/label emission
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`
**Work:** Define an IL label per jump-target instruction; lower `Jump`→`br`,
`JumpIfFalse`/`JumpIfTrue`→pop condition (truthiness via `ScriptVar.Bool`) + branch,
and the OrPop / defined / null variants to match the opcode semantics exactly.
**Depends on:** T31

### T33 — Tests: control flow correctness
**File:** `DScript.Test/JitControlFlowTests.cs`
**Work:** `if`/`else`, `while`, `for`, ternary, `&&`/`||`, nested loops, accumulating
loop — JIT result matches interpreter (Reflection.Emit back-end; closure back-end
asserted to decline). Edge cases: empty loop body, condition false on entry.
**Depends on:** T32

### T34 — Live-frame transfer mechanism (prerequisite for OSR)
**File:** `DScript/Vm/OsrEntryFrame.cs`, `DScript/Vm/VirtualMachine.cs`, `DScript/Jit/ReflectionEmitJitCompiler.cs`
**Work:** Build the standalone capability to transfer a *running* frame between the
interpreter and compiled code — independent of when it is triggered. Define
`OsrEntryFrame` capturing a live frame's locals, operand stack, and instruction
pointer; a VM routine to snapshot the current interpreter frame into one and to
reconstruct interpreter state from one; and a compiled-code entry point that accepts
a transferred frame (a per-loop-header IL prologue that loads locals/stack from it
before falling through to the body). No trigger yet — exercised directly by tests.
This is sequenced **before** OSR: OSR is just one consumer of it.
**Depends on:** T32

### T35 — Tests: live-frame transfer
**File:** `DScript.Test/JitFrameTransferTests.cs`
**Work:** Round-trip a captured frame (snapshot → reconstruct) and resume both in the
interpreter and in compiled code at a loop header, asserting the result matches a
straight interpreter run. Edge cases: empty stack, frame captured at the first vs a
later loop iteration.
**Depends on:** T34

### T35a — OSR: trigger using the transfer mechanism (after T34/T35)
**File:** `DScript/Vm/VirtualMachine.cs`, `DScript/Jit/ReflectionEmitJitCompiler.cs`
**Work:** In the backward-`Jump` handler, when `BackEdgeCount` crosses the threshold
and the chunk is `Cold` with a compiler registered, compile it, snapshot the live
frame via the T34 mechanism, and enter the compiled code at the current loop header.
OSR is now purely the *policy* layer on top of the (already-built and tested)
transfer mechanism.
**Depends on:** T34, T35, T08 (tier gate)

### T35b — Tests: OSR correctness
**File:** `DScript.Test/JitOsrTests.cs`
**Work:** A long `while` loop tiers up mid-run and returns the correct accumulated
result; nested loops; OSR combined with a deopt inside the loop falls back correctly.
**Depends on:** T35a

---

## Phase 8 — Local variables & assignments

### T36 — `JitSetVar` / declare helpers
**File:** `DScript/Vm/VirtualMachine.cs`
**Work:** Add `internal` helpers mirroring `SetVar`/`DeclareVar`/`DeclareLocal`/
`DeclareConst` (resolve + `ReplaceWith`; strict `ReferenceError`; non-strict global
create + version bump; declare into the correct env).
**Depends on:** T20 (JIT runtime helpers pattern)

### T37 — `JitSetProp` helper
**File:** `DScript/Vm/VirtualMachine.cs`
**Work:** Add `internal ScriptVar JitSetProp(obj, name, value, strict)` mirroring
`SetMember`, consistent with the property inline cache (shape bump invalidates cells).
**Depends on:** T28 (inline cache)

### T38 — Assignment/declare codegen (both back-ends)
**File:** `DScript/Jit/JitDecoder.cs`, `DScript/Jit/JitInstruction.cs`, `DScript/Jit/ReflectionEmitJitCompiler.cs`, `DScript/Jit/ClosureThreadedJitCompiler.cs`
**Work:** Decoder emits `SetVar`/`SetProp`/`Declare*` instructions (wide + narrow);
both back-ends lower them via the T36/T37 helpers. Speculative tiers permit numeric
local assignment where the binding stays int/double; otherwise conservative.
**Depends on:** T36, T37, T31

### T39 — Tests: locals & assignments
**File:** `DScript.Test/JitAssignmentTests.cs`
**Work:** Accumulator loops, property mutation, compound assignment (`+=`, `++`),
`let`/`const`, strict-mode reference errors — match the interpreter on both back-ends
(closure declines control-flow cases).
**Depends on:** T38

### T40 — (Optional) Promote non-captured locals to IL locals
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`
**Work:** Escape/capture analysis; promote locals not captured by a nested closure to
IL locals, bypassing env resolution. Helper path remains the fallback.
**Depends on:** T38

---

## Phase 9 — Monomorphic call inlining

### T41 — Inlining eligibility & budget
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`
**Work:** Decide inlinability from the call-site profile + callee chunk: monomorphic,
non-native, JIT-eligible body, within a size budget, no closure-over-frame / no
`arguments` / no `eval`, bounded inline depth.
**Depends on:** T14 (mono dispatch), T38

### T42 — Body splicing with param/return rewriting
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`
**Work:** Inline the callee body: args → fresh locals, parameter reads → those locals,
`return` → produce-value + jump-to-continuation. Identity guard on the baked callee;
fall back to `InvokeCallable` on miss or non-inlinable.
**Depends on:** T41, T31

### T43 — Tests: inlining correctness + alloc delta
**File:** `DScript.Test/JitInliningTests.cs`
**Work:** Inlined monomorphic call matches interpreter; guard miss falls back;
bounded recursion; benchmark shows reduced allocation on a call-heavy workload
(numbers in the commit message).
**Depends on:** T42

---

# Part B — Tier 2: root-cause VM performance

## Phase 10 — Call-frame pooling & escape analysis

> **Outcome: not viable as specified.** The `vars` object is already pooled
> (`frameVarsPool`, gated by `RecyclableFrame`). Pooling the `Environment` object
> too was attempted and **reverted**: the GetVar inline cache keys on `Environment`
> *identity* + version (`Chunk.InlineCacheEntry`), so reusing env objects makes
> references recur and a stale cache entry can falsely validate (it broke
> `test043.js`). Safe env pooling would require redesigning the env-identity inline
> cache — out of scope. Frame-allocation reduction therefore stops at the existing
> vars pool plus the JIT inlining from Phase 9.

### T44 — Expanded escape analysis
**File:** `DScript/Compiler/*`, `DScript/Vm/Chunk.cs`
**Work:** Extend the `MakesClosure`/`RecyclableFrame` analysis so a frame is poolable
when nothing escapes it (no closure capture, no returned local reference, no
`arguments` leak). Record the result on the chunk.
**Depends on:** nothing (independent of JIT)

### T45 — Frame pool in the VM
**File:** `DScript/Vm/VirtualMachine.cs`
**Work:** Per-VM pool of `Environment` + vars objects; borrow on call entry, clear and
return on exit (no stale child links — honour the repo's cycle-safety rules).
**Depends on:** T44

### T46 — Tests: pooling correctness + alloc delta
**File:** `DScript.Test/FramePoolingTests.cs`
**Work:** Recursion / call-heavy code returns correct results with fewer allocations
(alloc-MB delta recorded); closures still capture correctly; no cross-call leakage.
**Depends on:** T45

## Phase 11 — Value-representation overhaul (deferred / high-risk; spike first)

### T47 — Spike: `Value` type + conversions — **DONE**
**File:** `DScript/Vm/Value.cs`, `DScript.Test/ValueTests.cs`
**Work:** Tagged `readonly struct Value` (int/double/null/undefined inline; ScriptVar
ref otherwise) with From/ToScriptVar conversions + queries; 12 unit tests covering
edge values (NaN, -0, ±Infinity, ref round-trip). Not wired into the VM.

### T48 — Migrate interpreter operand stack + arithmetic to `Value` — **DEFERRED**
**File:** `DScript/Vm/VirtualMachine.cs`
**Why deferred:** this is a whole-interpreter refactor (the `ScriptVar[]` operand
stack is threaded through ~100 opcode handlers). It cannot be landed safely as one
change, and a naive `Value[]`-with-conversion shim would be *slower* (extra
conversions) and *change identity semantics* — `ToScriptVar()` reallocates
primitives, breaking reference-equality invariants the VM relies on (`SharedTrue`/
`SharedFalse`, the inline caches). It must be done incrementally, opcode-by-opcode,
keeping the suite green at each step — a dedicated multi-session effort. The T47
spike is the abandon-safe foundation; the migration is the open work.
**Depends on:** T47

### T49 — Migrate JIT speculative tiers to `Value` — **DEFERRED** (after T48)
**File:** `DScript/Jit/*`
**Work:** Flow `Value` through compiled code (subsumes the unboxed int/double tiers).
Parity tests both back-ends; benchmark.
**Depends on:** T48, T31

---

# Part C — Tier 3: incremental improvements

### T50 — Expanded JIT opcode coverage
**File:** `DScript/Jit/JitDecoder.cs`, `DScript/Jit/*`, `DScript.Test/*`
**Work:** Array index get/set, `typeof`, `BitNot`, shifts, `Negate`, `instanceof`/`in`
(helpers), template literals. Decoder + both back-ends + parity tests per opcode.
**Depends on:** T32, T38

### T51 — Polymorphic (bimorphic) inline caches — **property cache done**
**File:** `DScript/Vm/PropCacheCell.cs`, `DScript/Vm/VirtualMachine.cs`
**Done:** `PropCacheCell` is now a 2-way LRU (two object/shape/link entries); a site
alternating between two objects/shapes hits instead of thrashing. Bimorphic tests +
a benchmark workload (`bimorphic prop read` ~1.21× ReflEmit) added.
**Scoped out at the time:** bimorphic *call* dispatch — without inlining it's
near-identical to general dispatch. **Now done via bimorphic inlining** (see below):
a bimorphic call site bakes both observed callees and inlines each inline-eligible
one behind an identity guard.

### Bimorphic inlining (follow-up to T41/T42 + T51)
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`, `JitDecoder.cs`, `JitInstruction.cs`
**Done:** the decoder bakes both callees of a Bimorphic site (`Callee0`/`Callee1`);
`EmitCall` emits one guarded inline path per inline-eligible baked callee (≤2),
splicing the matching body on a hit and falling through to general dispatch on a
miss or for non-inlinable/megamorphic sites. Unifies the mono and bimorphic paths
(a non-inlinable callee now gets no dead guard). Tests: both-inlined, mixed
eligibility, megamorphic fallback. Benchmark `bimorphic inlined call` ~1.52× ReflEmit
vs 1.11× closure (general dispatch) — confirming inlining engages.
**Depends on:** T14, T28

### T52 — Background compilation
**File:** `DScript/Vm/VirtualMachine.cs`, `DScript/Vm/JitRegistry.cs`
**Work:** Compile hot chunks on a worker thread; interpret until the delegate is
installed. Thread-safe against `JitRegistry` and chunk state; no double-compile.
**Depends on:** T08

### T53 — Tests: background compilation
**File:** `DScript.Test/JitBackgroundTests.cs`
**Work:** Concurrent compile produces correct results, no state corruption, no
double-compile, deterministic fallback while compiling.
**Depends on:** T52

### T54 — JIT diagnostics & observability
**File:** `DScript/Jit/JitDiagnostics.cs`, `DScript/Jit/JitDecoder.cs`
**Work:** Per-chunk report: JIT state, chosen tier, deopt count, and decline reason
(which opcode/condition). `JitDecoder` returns a reason on decline. Optional REPL/CLI
surface.
**Depends on:** T16

### T55 — Tests: diagnostics
**File:** `DScript.Test/JitDiagnosticsTests.cs`
**Work:** Known compiled/declined/deopted chunks report the expected tier and reason.
**Depends on:** T54

### T56 — Interpreter dispatch optimisation — **investigated; no safe positive lever**
**File:** `DScript/Vm/VirtualMachine.cs`
**Outcome:**
- **Computed-goto: N/A in C#.** The interpreter dispatches via `switch (op)` over a
  dense opcode enum, which RyuJIT already lowers to a jump table — the platform
  optimum. A hand-rolled delegate-threaded dispatch would be *slower* (indirect
  calls vs inlined switch arms), so this lever does not apply.
- **Superinstruction fusion is already extensive** — `BinaryConst`, `BinaryIntConst`,
  `GetVarGetVarBinary`, `GetVarGetProp`, `SetVarPop`, `SetPropPop`, `GetPropMethod`,
  `GetPropCall0`. The peephole optimizer already yields 1.3–1.77× on the loop
  workloads, and the JIT adds 1.5–2.4× on top.
- The one remaining candidate is a **compound-assign superinstruction** (fuse
  `GetVar(x) + BinaryIntConst(op,k) + SetVarPop(x)`, same `x`, into one opcode for
  `x = x op k`). It needs a fragile 3-instruction byte-stream peephole (variable-width
  + narrow/wide forms + jump-target safety) and a new handler in the *hottest* loop,
  for a marginal dispatch saving. Deferred: the regression/correctness risk in the
  hot path outweighs the small expected gain. Left as a dedicated, benchmark-gated
  follow-up.
**Depends on:** nothing (independent)

---

## Summary — dependency order at a glance

```
Phase 7 (control flow):  T16 → T30 → T31 → T32 → T33
                         T32 → T34 (live-frame transfer) → T35
                         T34 + T35 + T08 → T35a (OSR) → T35b
Phase 8 (locals):        T20 → T36 ┐
                         T28 → T37 ┤
                         T31 ──────┴→ T38 → T39 → T40(opt)
Phase 9 (inlining):      T14 + T38 → T41 → T42 → T43   (T42 also needs T31)
Phase 10 (frame pool):   T44 → T45 → T46
Phase 11 (Value, spike): T47 → T48 → T49   (T49 also needs T31)
Phase 12 (opcodes):      T32 + T38 → T50
Phase 13 (PICs):         T14 + T28 → T51
Phase 14 (bg compile):   T08 → T52 → T53
Phase 15 (diagnostics):  T16 → T54 → T55
Phase 16 (dispatch):     T56 (independent)
```

Recommended order: **Phase 7 → 8 → 9** (Tier 1, the coverage + bottleneck wins),
then **Phase 10** (cheap, broad), then Tier 3 phases as desired, with **Phase 11**
(Value overhaul) last and only after a successful spike. Phases 10, 15, 16, and the
T47 spike are independent and can be picked up any time.

---

## Status

**Tier 1 complete** (T30–T33, T36–T43): control flow (if/while/for), local
variables & assignments, and monomorphic inlining of pure-parameter leaf callees.
Loops and stateful functions now compile (conservative tier); the closure back-end
declines control flow. Benchmark: an inlined helper loop runs ~2.37× the interpreter
(ReflEmit).

**Tier 3 complete** (T50–T55): expanded opcodes (indexing, unary, shifts),
bimorphic property inline cache, opt-in background compilation, and JIT
diagnostics. **T56** (dispatch) investigated — computed-goto is N/A in C# (the
switch is already a jump table) and fusion is already extensive; the one remaining
lever (a compound-assign superinstruction) is deferred as high-risk/low-reward in
the hot path.

**Tier 2 partial:** Phase 10 (frame pooling) — env pooling not viable (inline-cache
identity conflict); vars pooling already in place. Phase 11 — `Value` spike (T47)
done; the full operand-stack/arithmetic migration (T48/T49) is deferred as a
dedicated incremental refactor (a single-leap migration would break identity
invariants and the green suite).

**Deferred:** OSR (sequenced behind the live-frame-transfer mechanism, T34/T35 →
T35a/T35b — narrow ROI); Phase 11 migration (T48/T49); the T56 compound-assign
superinstruction.

All phases from 10 onward have been addressed (implemented or investigated with a
recorded outcome). 1799 tests green, ~90.5% coverage; everything pushed.

**Benchmark accounting** (the cross-cutting rule): the `JitSection` now has a
workload per shipped JIT phase — `inlined helper loop` (Phase 9, ~2.36× ReflEmit),
`bimorphic prop read` (Phase 13, ~1.20×), and `control-flow loop` (Phase 7, ~1.28×).
Phases 10/11/16 shipped no runtime change (reverted / spike-only / investigation), so
before/after was moot. Interpreter-only regression check: the benchmark's interpreter
`workloads` section (JIT off) totals ~323 ms vs the ~325–340 ms pre-extension
baseline — no regression (the interpreter hot loop was never modified; the JIT is
opt-in/off by default).
