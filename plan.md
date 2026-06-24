# DScript JIT — Implementation Plan

## Background

The profiling infrastructure is complete: every `Chunk` carries invocation counters,
back-edge counters, per-call-site morphism profiles, and per-binary-op type profiles.
The six phases below build on that data to produce a functioning first JIT tier.

---

## Phase 1 — Hotness Thresholds

**Goal:** decide when a `Chunk` is worth compiling.

A static `JitThresholds` class holds tunable constants:

| Constant | Default | Meaning |
|---|---|---|
| `InvocationThreshold` | 1 000 | Invocations before function is hot |
| `BackEdgeThreshold` | 10 000 | Back-edges before loop body is hot |

A `Chunk.IsHot()` method returns `true` when either threshold is exceeded.
A `Chunk.JitState` enum (`Cold → Compiling → Compiled → Failed`) prevents
re-entrant compilation and records permanent failures.

No VM changes are required yet — the predicate is purely read-side.

**Deliverables:**
- `DScript/Vm/JitThresholds.cs`
- `Chunk.IsHot()`, `Chunk.JitState`
- Unit tests in `DScript.Test/JitThresholdTests.cs`

---

## Phase 2 — JIT Interface

**Goal:** define the contract between the VM and any JIT back-end without coupling them.

```csharp
public delegate ScriptVar JitDelegate(VirtualMachine vm, ScriptVar[] args, Environment env);

public interface IJitCompiler
{
    JitDelegate? Compile(Chunk chunk);
}
```

The `vm` parameter is the runtime handle the compiled code uses for anything it
does not emit inline: call dispatch (Phase 3c), deoptimization (Phase 4), OSR
re-entry (Phase 5), and inline-cache misses (Phase 6). The `env` parameter is the
full lexical environment, so compiled code resolves variables — parameters,
locals, outer captures, and globals — exactly as the interpreter does (via
`VirtualMachine.JitGetVar`). Without these a standalone delegate could only ever
handle pure, branch-free, call-free arithmetic over its own parameters.

`Chunk` gains a nullable `CompiledDelegate` property (type `JitDelegate?`) set by the
compiler after a successful compilation.

A `JitRegistry` singleton holds the active `IJitCompiler` (null by default — JIT
is opt-in). The host registers a compiler at startup:

```csharp
JitRegistry.Register(new ReflectionEmitJitCompiler());
```

The VM is extended in `Execute()`: before entering the interpreter loop, if
`chunk.CompiledDelegate != null`, invoke it and return its result. If `chunk.IsHot()`
and a compiler is registered and the chunk is still `Cold`, transition to `Compiling`,
call `IJitCompiler.Compile`, store the result, transition to `Compiled`.

**Deliverables:**
- `DScript/Vm/JitDelegate.cs` (delegate + interface + registry)
- `Chunk.CompiledDelegate`, `Chunk.JitState` transitions
- VM tier-selection logic in `VirtualMachine.Execute()`
- Tests: tier selection routing, null-compiler no-op, failure → `Failed` state

---

## Phase 3 — Type-Specialized Code Generation

**Goal:** emit a `DynamicMethod` that executes a hot `Chunk` faster than the
interpreter by exploiting the type profiles gathered in Phase 1 / the profiling
infrastructure.

### 3a — IL Emission Infrastructure

A `DynamicMethodBuilder` helper wraps `System.Reflection.Emit.ILGenerator` and
provides convenience methods:

- `EmitLoadArg(int)`, `EmitLoadLocal(int)`, `EmitStoreLocal(int)`
- `EmitIntOp(OpCode arithmeticOp)` — emits guarded integer arithmetic
- `EmitCallDeopt()` — emits a call to the interpreter fallback (see Phase 4)

### 3b — Specialized Binary Ops

For each binary-op profile site, the code generator inspects `BinaryOpProfile`:

- **Int × Int only** → emit `ldc.i4` / `add` / `sub` / `mul` / `div` IL with an
  overflow guard that deoptimizes on overflow or type mismatch.
- **Double × Double** → emit `conv.r8` + `add.r8` etc.
- **String left** → emit a direct `String.Concat` call.
- **Mixed / Other** → emit a direct `MathsOp` call (no specialization).

### 3c — Call Dispatch

For each call-site profile:

- **Monomorphic** → bake the target `Chunk`/delegate as a constant; emit a direct
  call, no dispatch overhead.
- **Bimorphic** → emit an identity check (`beq` on the callee pointer) followed by
  two inline call paths.
- **Megamorphic** → emit a call to the existing `DispatchCall` interpreter helper.

### 3d — Connecting to the Interface

`ReflectionEmitJitCompiler` implements `IJitCompiler.Compile(Chunk)`:

1. Walk the chunk's bytecode.
2. For each instruction, emit specialized IL where profiles justify it, or fall back
   to an interpreter-call stub.
3. Return the finished `JitDelegate`.

**Deliverables:**
- `DScript/Jit/DynamicMethodBuilder.cs`
- `DScript/Jit/ReflectionEmitJitCompiler.cs`
- Tests: Int+Int path, Double path, String concat path, mono call, mega call,
  correctness (results match interpreter)

---

## Phase 4 — Speculative unboxed specialization + deoptimization (re-planned)

**Why re-planned:** the Phase 3 emitter is *conservative* — every guard falls back
to `MathsOp`/general dispatch inline, so the compiled code is correct for any input
and never needs to bail out. That is safe but still pays two costs on the hot path
that the interpreter also pays: a heap `ScriptVar` allocation for *every*
intermediate result (`FromInt`/`FromDouble`), and a virtual `MathsOp` dispatch
whenever a value isn't statically int. Profiling notes call out allocation as the
dominant remaining bottleneck, so the largest win available is to **stop allocating
intermediates**.

**Goal:** for hot, pure (call-free), straight-line functions whose binary sites are
profiled as a single numeric type, compile a **speculative** body where values flow
as **raw `int` (or `double`)** through IL locals/the IL stack — no per-op
`ScriptVar`, no `MathsOp`. A value is boxed back into a `ScriptVar` only at the
function's return. Type assumptions are checked by cheap guards; a guard miss
**deoptimizes**.

### Deoptimization protocol (function-level)

Because the speculative tier only compiles **pure** functions (no calls ⇒ no
observable side effects before any guard), a guard miss can simply abandon the
compiled run and **re-execute the whole chunk in the interpreter**, returning that
result. No mid-stream stack reconstruction is needed.

```csharp
public readonly struct DeoptFrame
{
    public Chunk Chunk;          // the chunk that bailed
    public ScriptVar[] Args;     // original args of this invocation
    public Environment Env;      // original call environment (params already bound)
}
```

The compiled delegate, on a guard miss, does
`return vm.Deoptimize(new DeoptFrame(chunk, args, env));` which:

1. increments `chunk.DeoptCount`,
2. runs the chunk on the **interpreter only** (bypassing the JIT tier-up gate so it
   cannot re-enter the compiled code), and
3. returns the interpreter's result.

### Recompilation policy

After `JitThresholds.DeoptThreshold` (default 5) deopts, the chunk's
`CompiledDelegate` is cleared and `JitState` reset to `Cold`, so the next hotness
trigger recompiles it — but the recompile now prefers the **conservative** boxed
tier (which never deopts), since repeated deopts prove the speculation was wrong.

### Emitter

`ReflectionEmitJitCompiler` gains an unboxed integer tier:
- **Eligibility:** chunk is pure (no `Call`/`TailCall`/`CallMethod`), branch-free,
  every binary site profiled `Int`-only, only int constants, supported opcodes only.
- **Value flow:** `Constant`(int) → `ldc.i4`; `GetVar` → resolve, **guard `IsInt`
  (else deopt)**, push raw `.Int`; binary → raw int IL (`add`/`sub`/`mul`/`and`/`or`/
  `^`; `/`,`%` guard divisor `!= 0` else deopt to match `IntBinary`'s double result;
  comparisons → raw 0/1); `Return` → `FromInt`, `ret`.
- Non-eligible chunks fall through to the conservative boxed tier from Phase 3.

**Deliverables:**
- `DScript/Vm/DeoptFrame.cs`
- `VirtualMachine.Deoptimize(DeoptFrame)` + an interpreter-only execution entry
- `Chunk.DeoptCount`, `JitThresholds.DeoptThreshold`, recompilation policy
- Unboxed-int speculative tier in the emitter
- Tests: speculative result matches interpreter; a type surprise deopts and still
  returns the correct value; `DeoptCount` increments; after `DeoptThreshold` the
  chunk recompiles to the conservative tier; division-by-zero deopts correctly.

---

## Phase 5 — Unboxed double tier (re-planned)

**Why re-planned:** the original Phase 5 was On-Stack Replacement, which requires
compiling loops, which in turn requires modelling control flow and a reified operand
stack — a large subsystem whose payoff (replacing an already-running interpreter
loop) is narrower than removing allocations. The bigger, more general performance
win is to extend the unboxed tier to **doubles**, covering floating-point-heavy pure
functions with the same "no intermediate allocation" benefit.

**Goal:** add a speculative **unboxed `double`** tier: for pure, branch-free
functions whose numeric sites are profiled `Double` (or mixed int/double), flow raw
`double` through IL (`conv.r8` int operands as needed), guard operands numeric (else
deopt), and `FromDouble` only at return.

**Deliverables:**
- Unboxed-double tier in the emitter (shares the deopt machinery from Phase 4)
- Tests: double and mixed int/double pure functions match the interpreter; a
  non-numeric surprise deopts and returns the correct value.

> OSR and loop compilation remain possible future work; they are deferred because
> their cost/benefit is worse than the allocation-elimination tiers above.

---

## Phase 6 — Property reads + inline cache (re-planned for DScript's object model)

**Why re-planned:** the original Phase 6 assumed slot-based objects ("bake the
resolved slot index") and shape-guard-failure → deopt. Neither fits DScript:
objects are **linked lists of named `ScriptVarLink` children** (no numeric slots),
and `ScriptVar.ShapeVersion` **already exists** and already increments on
`AddChild`/`RemoveLink` — the interpreter's `GetProp` already uses a shape-keyed
property cache. A property-cache miss is also *normal* (re-resolve), not a type
surprise, so it must not deoptimize.

**Goal:** let the conservative tier compile property reads at all (`GetProp`), and
give each site a **monomorphic inline cache** keyed by object identity + shape that
skips the `FindChild` + prototype walk on a repeat hit.

### T26 — `ScriptVar.ShapeVersion`
Already present (`ShapeVersion` getter; `BumpShapeVersion`; incremented in
`AddChild`/`RemoveLink`). No code change — Phase 6 reuses it.

### T27 — `GetProp` codegen (conservative tier)
Add a runtime helper `VirtualMachine.JitGetProp(ScriptVar obj, string name)` that
reproduces the interpreter's miss-path semantics exactly: proxy `[[Get]]`, own
`FindChild`, `engine.FindInParentClasses` (prototype walk), getter invocation, and
the built-in virtual properties (`length`/`size`). The decoder gains a `GetProp`
instruction (normalising `GetProp`/`GetPropN`); the emitter lowers it to a call to
`JitGetProp`. Property-reading functions now compile.

### T28 — Per-site monomorphic inline cache
Bake a small mutable cache cell per `GetProp` site. Guard
`ReferenceEquals(obj, cell.Object) && obj.ShapeVersion == cell.Version`; on a hit,
reuse `cell.Link` (skipping the lookup) — invoking its getter if present; on a miss,
call `JitGetProp` and refresh the cell. A miss simply re-resolves inline (no deopt).

**Deliverables:**
- `VirtualMachine.JitGetProp` + `GetProp` in the decoder/emitter
- Per-site IC cell + shape/identity guard in the emitter
- Tests: property reads (own, inherited, getter, built-in `length`) match the
  interpreter; a shape change (added property) still returns the correct value via
  inline re-resolve.

---

## Cross-cutting concerns

- **Test coverage**: every new class in `DScript/Jit/` and `DScript/Vm/` additions
  must maintain the 90 % line-coverage gate.
- **Benchmarks**: Phases 4-6 each require before/after `DScript.Benchmark` runs with
  the JIT registered; the speculative tiers must beat the interpreter on the
  arithmetic workloads and must not regress the interpreter-only path.
- **Platform**: `System.Reflection.Emit` is available on all three platforms
  (.NET 8+ on Windows, Linux, macOS). No platform-specific APIs.
- **Opcodes**: no new opcodes are needed; the JIT operates above the bytecode layer.
- **Wiki**: add `wiki/JIT.md` documenting thresholds, the `IJitCompiler` interface,
  and the `JitRegistry` API for host embedders.
