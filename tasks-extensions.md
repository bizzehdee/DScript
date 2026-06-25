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

### T47 — Spike: `Value` type + conversions
**File:** `DScript/Vm/Value.cs`
**Work:** Introduce a NaN-boxed/tagged `readonly struct Value` with conversions
to/from `ScriptVar`. No call sites migrated yet; pure addition + unit tests for the
conversions and edge values (NaN, -0, int/double boundary).
**Depends on:** nothing (research spike)

### T48 — Migrate interpreter operand stack + arithmetic to `Value`
**File:** `DScript/Vm/VirtualMachine.cs`
**Work:** Operand stack becomes `Value[]`; arithmetic opcodes operate on `Value`;
convert at object/property boundaries. Full suite + benchmark before/after.
**Depends on:** T47

### T49 — Migrate JIT speculative tiers to `Value`
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

### T51 — Polymorphic (bimorphic) inline caches
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`, `DScript/Vm/PropCacheCell.cs`
**Work:** Two baked entries for call sites (T14) and property sites (T28) before
megamorphic fallback, driven by the morphism profiles. Tests + benchmark on
2-callee / 2-shape workloads.
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

### T56 — Interpreter dispatch optimisation
**File:** `DScript/Vm/VirtualMachine.cs`
**Work:** Reduce dispatch cost for never-JIT chunks (computed-goto-style dispatch
where supported; a few loop-shaped superinstructions). Benchmark-gated; must not
regress. Append any new opcodes to the end of the enum.
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

**OSR is sequenced behind the live-frame-transfer mechanism**: build and test
T34 (live-frame transfer) + T35 first, then T35a/T35b (OSR) as a thin policy layer
on top. All four are deferred — narrow ROI (only a long loop within a sub-threshold
number of calls) — and picked up only if a workload demands it.

**Not yet started:** Tier 2 (T44–T49) and Tier 3 (T50–T56).
