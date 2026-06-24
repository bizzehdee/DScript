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

## Phase 4 — Guards and Deoptimization

**Goal:** when a JIT guard fails at runtime (e.g. a site profiled as Int-only
receives a String), fall back to the interpreter without corrupting state.

### Protocol

Every JIT frame carries a `DeoptFrame` struct:

```csharp
public struct DeoptFrame
{
    public Chunk Chunk;
    public int   InstructionPointer;  // bytecode offset of the failing guard
    public ScriptVar[] Locals;        // snapshot of locals at the guard point
}
```

When a guard fails, the JIT calls `VirtualMachine.Deoptimize(DeoptFrame)`, which:

1. Reconstructs an interpreter stack frame from the `DeoptFrame`.
2. Resumes interpretation from `InstructionPointer`.
3. Increments a `Chunk.DeoptCount` counter (useful for future recompilation decisions).

### Recompilation policy

After `DeoptThreshold` (default: 5) deoptimizations, the chunk's `JitState` is
reset to `Cold` so it can be recompiled with less aggressive specialization on the
next hotness trigger.

**Deliverables:**
- `DScript/Vm/DeoptFrame.cs`
- `VirtualMachine.Deoptimize(DeoptFrame)`
- `Chunk.DeoptCount`, recompilation policy
- Tests: guard failure falls back correctly, result matches pure-interpreter run,
  recompilation after `DeoptThreshold` exceeded

---

## Phase 5 — On-Stack Replacement (OSR)

**Goal:** for a function that is already running in the interpreter when it crosses
the back-edge threshold, replace the live interpreter frame with a JIT frame
mid-execution rather than waiting for the next call.

OSR is optional for correctness but important for long-lived loops (e.g. a
top-level `while(true)` that never returns).

### Entry point

The VM checks `chunk.BackEdgeCount >= JitThresholds.BackEdgeThreshold` inside the
`Jump` opcode handler (after incrementing the counter). If the chunk is still `Cold`
and a JIT compiler is registered:

1. Transition to `Compiling`; compile the chunk (reuse Phase 3 pipeline).
2. Build an `OsrEntryFrame` from the current interpreter locals and stack pointer.
3. Jump into the compiled code at the OSR entry point corresponding to the
   current `ip` (each backward-jump target is an OSR entry).

### Complexity note

OSR requires the JIT to emit a separate entry prologue for every backward-jump
target (loop header). Each prologue loads locals from the `OsrEntryFrame` into the
appropriate IL locals before falling through to the main loop body.

**Deliverables:**
- `DScript/Vm/OsrEntryFrame.cs`
- OSR trigger in `VirtualMachine` `Jump` handler
- OSR entry prologues in `ReflectionEmitJitCompiler`
- Tests: long-running loop exits with correct result when OSR fires mid-loop

---

## Phase 6 — Inline Cache Promotion

**Goal:** bake the offsets cached by the existing `InlineCache` array as IL
constants in JIT output, eliminating the dictionary lookup on every property access.

### Approach

The interpreter already stores the resolved slot index for `GetVar`/`SetVar` in
`Chunk.InlineCache[offset]`. The JIT reads `InlineCache[site]` at compile time:

- If the cache is warm (non-zero slot index), emit `ldc.i4 <slot>` followed by a
  direct array-index load — no name lookup.
- Emit a guard checking that the object's shape version matches the version
  recorded when the cache was filled. On mismatch, call the interpreter's
  `GetVarSlow` helper and update the baked constant via recompilation.

Shape versioning requires a `ShapeVersion` counter on `ScriptVar` (or its
underlying property map) that increments on every property addition or deletion.

**Deliverables:**
- `ScriptVar.ShapeVersion` (or equivalent)
- IC-promotion path in `ReflectionEmitJitCompiler`
- Shape-guard failure triggers re-entry via `Deoptimize` (Phase 4)
- Tests: warm IC path executes without dictionary lookup (verified via benchmark
  delta), shape change triggers correct deopt and re-execution

---

## Cross-cutting concerns

- **Test coverage**: every new class in `DScript/Jit/` and `DScript/Vm/` additions
  must maintain the 90 % line-coverage gate.
- **Benchmarks**: Phases 3, 5, and 6 each require before/after `DScript.Benchmark`
  runs; regressions > 5 % block the commit.
- **Platform**: `System.Reflection.Emit` is available on all three platforms
  (.NET 8+ on Windows, Linux, macOS). No platform-specific APIs.
- **Opcodes**: no new opcodes are needed; the JIT operates above the bytecode layer.
- **Wiki**: add `wiki/JIT.md` documenting thresholds, the `IJitCompiler` interface,
  and the `JitRegistry` API for host embedders.
