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

## Phase 4 — Guards and Deoptimization

### T18 — `DeoptFrame` struct
**File:** `DScript/Vm/DeoptFrame.cs`  
**Work:** Define struct with `Chunk Chunk`, `int InstructionPointer`,
`ScriptVar[] Locals`.  
**Depends on:** T02

### T19 — `Chunk.DeoptCount` and recompilation policy
**File:** `DScript/Vm/Chunk.cs`  
**Work:** Add `int DeoptCount { get; set; }`. Define `JitThresholds.DeoptThreshold
= 5`. When `DeoptCount >= DeoptThreshold`, reset `JitState` to `Cold` and clear
`CompiledDelegate` so the chunk recompiles on the next invocation.  
**Depends on:** T18, T03

### T20 — `VirtualMachine.Deoptimize(DeoptFrame)`
**File:** `DScript/Vm/VirtualMachine.cs`  
**Work:** Static/instance method that reconstructs an interpreter stack frame from
a `DeoptFrame`, increments `DeoptCount`, applies recompilation policy from T19, and
resumes interpretation from `InstructionPointer`.  
**Depends on:** T19, T08

### T21 — Emit guard failure calls in JIT output
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`  
**Work:** Replace the `EmitCallDeopt()` stub from T10 with a real call to
`VirtualMachine.Deoptimize`, passing a `DeoptFrame` constructed from the current
IL locals and the bytecode offset of the failing guard.  
**Depends on:** T20, T16

### T22 — Tests: deoptimization correctness
**File:** `DScript.Test/JitDeoptTests.cs`  
**Work:** Tests for: guard failure produces correct result via interpreter fallback;
`DeoptCount` increments; after `DeoptThreshold` deoptimizations the chunk resets to
`Cold`; recompiled chunk runs again.  
**Depends on:** T21

---

## Phase 5 — On-Stack Replacement (OSR)

### T23 — `OsrEntryFrame` struct
**File:** `DScript/Vm/OsrEntryFrame.cs`  
**Work:** Define struct with `Chunk Chunk`, `int TargetIp` (loop-header bytecode
offset), `ScriptVar[] Locals`, `ScriptVar[] Stack`.  
**Depends on:** T18

### T24 — OSR trigger in `Jump` handler
**File:** `DScript/Vm/VirtualMachine.cs`  
**Work:** After incrementing `BackEdgeCount` on a backward jump, check
`BackEdgeCount >= JitThresholds.BackEdgeThreshold && JitState == Cold &&
JitRegistry.Current != null`. If so: compile the chunk (T16 pipeline), build an
`OsrEntryFrame` from current interpreter state, call the OSR entry point.  
**Depends on:** T23, T16, T08

### T25 — OSR entry prologues in the JIT compiler
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`  
**Work:** For every backward-jump target (loop header), emit a separate IL entry
label. Each prologue loads locals from `OsrEntryFrame.Locals` into the
corresponding IL local slots, then falls through to the main compiled body.  
**Depends on:** T24

### T26 — Tests: OSR correctness
**File:** `DScript.Test/JitOsrTests.cs`  
**Work:** Tests for: loop running past `BackEdgeThreshold` exits with correct
accumulated result; nested loops each fire OSR correctly; OSR + deopt combination
(guard fails inside OSR-entered loop) falls back and produces correct result.  
**Depends on:** T25

---

## Phase 6 — Inline Cache Promotion

### T27 — `ScriptVar.ShapeVersion` counter
**File:** `DScript/ScriptVar.cs`  
**Work:** Add `int ShapeVersion { get; private set; }`. Increment it in
`AddChild`, `RemoveLink`, and any other method that structurally mutates the
property map. This is the shape guard key used by the JIT.  
**Depends on:** nothing (can be done in parallel with earlier phases)

### T28 — IC-promotion path in the JIT compiler
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`  
**Work:** For `GetVar`/`SetVar` sites where `InlineCache[offset]` is warm, emit:
(1) `ldc.i4 <slot>` — the baked slot index; (2) a shape-version guard comparing
`obj.ShapeVersion` to the version recorded at compile time; (3) on guard pass,
direct array-element access; (4) on guard fail, call `Deoptimize` (T21).  
**Depends on:** T27, T21

### T29 — Tests: IC promotion correctness and performance
**File:** `DScript.Test/JitIcPromotionTests.cs`  
**Work:** Tests for: warm IC path produces correct property value; adding a new
property (shape change) triggers deopt and still returns correct value; benchmark
assertion that IC-promoted run is faster than un-promoted baseline (verified via
`DScript.Benchmark` before/after numbers recorded in commit message).  
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
T02 → T18 → T19 → T20 → T21 → T22
                   T16 ────↗
T23 → T24 → T25 → T26
      T16 ──↗
T27 → T28 → T29
      T21 ──↗
```
