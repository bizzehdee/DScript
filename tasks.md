# DScript JIT — Task List

Tasks are ordered so that every dependency is listed before the task that needs it.
Each task names its direct predecessors under **Depends on**.

---

## Phase 1 — Hotness Thresholds

### T01 — `JitThresholds` constants
**File:** `DScript/Vm/JitThresholds.cs`  
**Work:** Create static class with `InvocationThreshold = 1000` and
`BackEdgeThreshold = 10000`. Add XML doc.  
**Depends on:** nothing

### T02 — `Chunk.JitState` enum and property
**File:** `DScript/Vm/Chunk.cs`  
**Work:** Add `JitState` enum (`Cold`, `Compiling`, `Compiled`, `Failed`) and a
`JitState JitState { get; set; }` property initialized to `Cold`.  
**Depends on:** T01

### T03 — `Chunk.IsHot()` predicate
**File:** `DScript/Vm/Chunk.cs`  
**Work:** Add `bool IsHot()` returning `true` when
`InvocationCount >= JitThresholds.InvocationThreshold ||
BackEdgeCount >= JitThresholds.BackEdgeThreshold`, and `JitState == Cold`.  
**Depends on:** T02

### T04 — Tests: hotness thresholds
**File:** `DScript.Test/JitThresholdTests.cs`  
**Work:** Tests for `IsHot()` — below threshold returns false, at threshold returns
true, already `Compiled` returns false, `Failed` returns false.  
**Depends on:** T03

---

## Phase 2 — JIT Interface

### T05 — `JitDelegate` and `IJitCompiler`
**File:** `DScript/Vm/JitDelegate.cs`  
**Work:** Define
`delegate ScriptVar JitDelegate(VirtualMachine vm, ScriptVar[] args, Environment env)`
and `interface IJitCompiler { JitDelegate? Compile(Chunk chunk); }`. The `vm`
parameter is the runtime handle the compiled code uses for call dispatch, deopt,
OSR, and inline-cache misses; the `env` parameter is the full lexical environment
so compiled code resolves variables exactly as the interpreter does.  
**Depends on:** nothing (can be done in parallel with Phase 1)

### T06 — `JitRegistry` singleton
**File:** `DScript/Vm/JitDelegate.cs` (or `JitRegistry.cs`)  
**Work:** Thread-safe singleton holding `IJitCompiler? Current`. Methods:
`Register(IJitCompiler)`, `Clear()`.  
**Depends on:** T05

### T07 — `Chunk.CompiledDelegate` property
**File:** `DScript/Vm/Chunk.cs`  
**Work:** Add `JitDelegate? CompiledDelegate { get; set; }`.  
**Depends on:** T05, T02

### T08 — VM tier-selection logic
**File:** `DScript/Vm/VirtualMachine.cs` — top of `Execute()`  
**Work:** Before the interpreter loop: if `CompiledDelegate != null` invoke and
return. Else if `IsHot()` and `JitRegistry.Current != null`, set `JitState =
Compiling`, call `Compile`, store result (or set `Failed` on exception), set
`JitState = Compiled`, then invoke.  
**Depends on:** T03, T06, T07

### T09 — Tests: tier-selection routing
**File:** `DScript.Test/JitTierTests.cs`  
**Work:** Tests for: no compiler registered → interpreter runs normally; stub
compiler registered → delegate invoked after threshold; compile exception →
`JitState == Failed`; subsequent calls with `Failed` state → fall back to
interpreter.  
**Depends on:** T08

---

## Phase 3 — Type-Specialized Code Generation

### T10 — `DynamicMethodBuilder` helper
**File:** `DScript/Jit/DynamicMethodBuilder.cs`  
**Work:** Wrapper around `ILGenerator` with helpers for loading/storing locals,
emitting guarded integer arithmetic, emitting a `MathsOp` fallback call, and
finalizing to a `JitDelegate`.  
**Depends on:** T05

### T11 — Specialized Int×Int binary-op emission
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`  
**Work:** For `BinaryOpProfile` sites where both flags are `Int` only, emit inline
IL integer arithmetic (`add`, `sub`, `mul`, `div`) with an overflow/type guard that
calls `EmitCallDeopt()` on mismatch.  
**Depends on:** T10

### T12 — Specialized Double binary-op emission
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`  
**Work:** For sites flagged `Double` (either side), emit `conv.r8` + floating-point
IL ops. Fall through to `MathsOp` for `Other`.  
**Depends on:** T10

### T13 — Specialized String binary-op emission
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`  
**Work:** For sites where `LeftTypes` includes `String`, emit a direct
`String.Concat` call. Guard on left operand being string; deopt otherwise.  
**Depends on:** T10

### T14 — Monomorphic call dispatch
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`  
**Work:** For `Monomorphic` call-site profiles, bake the target chunk/delegate as
a constant and emit a direct call, bypassing dispatch overhead.  
**Depends on:** T10, T11

### T15 — Megamorphic call dispatch fallback
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`  
**Work:** For `Bimorphic`/`Megamorphic` sites, emit a call to the interpreter's
existing `DispatchCall` helper.  
**Depends on:** T14

### T16 — Wire `ReflectionEmitJitCompiler` to `IJitCompiler`
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`  
**Work:** Implement `IJitCompiler.Compile(Chunk)`: walk bytecode, dispatch to
T11–T15 emitters per opcode, fall back to interpreter-call stubs for unhandled
opcodes, return finished `JitDelegate`.  
**Depends on:** T11, T12, T13, T15

### T17 — Tests: code generation correctness
**File:** `DScript.Test/JitCodeGenTests.cs`  
**Work:** Register the real `ReflectionEmitJitCompiler`; drive a chunk past the
invocation threshold; assert results match a pure-interpreter run for: Int+Int,
Double, String concat, monomorphic call, megamorphic call.  
**Depends on:** T16, T09

---

## Phase 4 — Speculative unboxed-int specialization + deoptimization (re-planned)

### T18 — `DeoptFrame` struct
**File:** `DScript/Vm/DeoptFrame.cs`  
**Work:** Define a readonly struct with `Chunk Chunk`, `ScriptVar[] Args`,
`Environment Env` — the data needed to re-run a bailed pure chunk on the
interpreter.  
**Depends on:** T02

### T19 — `Chunk.DeoptCount`, `DeoptThreshold`, recompilation policy
**File:** `DScript/Vm/Chunk.cs`, `DScript/Vm/JitThresholds.cs`  
**Work:** Add `int DeoptCount { get; set; }` and
`JitThresholds.DeoptThreshold = 5`. Add a `bool PreferConservativeTier` flag set
when deopts exceed the threshold; when tripped, clear `CompiledDelegate` and reset
`JitState` to `Cold` so the next hotness trigger recompiles — to the conservative
boxed tier.  
**Depends on:** T18, T03

### T20 — `VirtualMachine.Deoptimize(DeoptFrame)` + interpreter-only entry
**File:** `DScript/Vm/VirtualMachine.cs`  
**Work:** Add an interpreter-only execution path (Execute that bypasses the JIT
tier-up gate so it cannot re-enter compiled code). `Deoptimize` increments
`DeoptCount`, applies the T19 recompilation policy, runs the chunk interpreter-only
on the supplied `Env`, and returns the result.  
**Depends on:** T19, T08

### T21 — Speculative unboxed-int tier in the emitter
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`, `DScript/Jit/DynamicMethodBuilder.cs`  
**Work:** Add a speculative tier tried before the conservative one. Eligibility:
pure (no `Call`/`TailCall`/`CallMethod`), branch-free, every binary site profiled
`Int`-only, int constants only, supported opcodes only, and `!PreferConservativeTier`.
Emit raw-`int` value flow: `ldc.i4` constants, `GetVar`→guard `IsInt` else deopt→
push `.Int`, binary→raw int IL (`/`,`%` guard divisor `!=0` else deopt), `Return`→
`FromInt`. On any guard miss emit
`return vm.Deoptimize(new DeoptFrame(chunk, args, env))`. Non-eligible chunks fall
through to the conservative tier.  
**Depends on:** T20, T16

### T22 — Tests: speculative int tier + deoptimization correctness
**File:** `DScript.Test/JitDeoptTests.cs`  
**Work:** Speculative result matches interpreter for all-int pure functions; a
type surprise deopts and still returns the correct value; `DeoptCount` increments;
after `DeoptThreshold` the chunk recompiles to the conservative tier (still
correct); division-by-zero deopts and matches the interpreter's double result.  
**Depends on:** T21

---

## Phase 5 — Speculative unboxed-double tier (re-planned)

### T23 — Double value-flow primitives
**File:** `DScript/Jit/DynamicMethodBuilder.cs`  
**Work:** Add primitives for raw-`double` flow: guard-numeric-else-deopt, load
`.Float`, `conv.r8` of a raw int, raw `double` arithmetic, `FromDouble` at boundary.  
**Depends on:** T21

### T24 — Unboxed-double tier in the emitter
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`  
**Work:** For pure, branch-free functions whose numeric sites are profiled `Double`
(or mixed int/double), flow raw `double` through IL, guard operands numeric (else
deopt), `FromDouble` only at return. Tried after the int tier, before conservative.  
**Depends on:** T23

### T25 — Tests: speculative double tier
**File:** `DScript.Test/JitDoubleTierTests.cs`  
**Work:** Double and mixed int/double pure functions match the interpreter; a
non-numeric surprise deopts and returns the correct value.  
**Depends on:** T24

> OSR / loop compilation deferred: worse cost/benefit than allocation elimination.

---

## Phase 6 — Inline Cache Promotion

### T26 — `ScriptVar.ShapeVersion` counter
**File:** `DScript/ScriptVar.cs`  
**Work:** Add `int ShapeVersion { get; private set; }`; increment on structural
property-map mutation (`AddChild`, `RemoveLink`, …). The shape-guard key.  
**Depends on:** nothing (parallelisable)

### T27 — `GetProp` support in the conservative tier
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`  
**Work:** Support `GetProp`/`GetPropN` via a runtime property-read helper, so
property-reading functions compile at all (prerequisite for IC promotion).  
**Depends on:** T16

### T28 — IC-promotion path in the JIT compiler
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`  
**Work:** For warm `GetProp` sites, bake the resolved slot, guard
`obj.ShapeVersion` against the compile-time version, direct-load on hit, `Deoptimize`
on miss.  
**Depends on:** T26, T27, T21

### T29 — Tests: IC promotion correctness
**File:** `DScript.Test/JitIcPromotionTests.cs`  
**Work:** Warm IC path returns the correct property value; a shape change deopts
and still returns the correct value.  
**Depends on:** T28

---

## Summary — dependency order at a glance

```
T01 → T02 → T03 → T04
T05 → T06
      T05 + T02 → T07
      T03 + T06 + T07 → T08 → T09
T05 → T10 → T11 → T14 → T15 → T16 → T17
            T12 ──────────────↗
            T13 ──────────────↗
T02 → T18 → T19 → T20 → T21 → T22         (T16 → T21)
                  T08 ──↗
T21 → T23 → T24 → T25                      (Phase 5: unboxed double)
T26 → T28 → T29                            (Phase 6: inline caches)
T16 → T27 ──↗
T21 ───────↗
```

Status: T01–T17 complete. Phases 4-6 re-planned for maximum performance
(unbox values, eliminate per-op allocation + MathsOp dispatch; deopt on type
surprise). OSR/loop compilation deferred.
