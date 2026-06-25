# DScript JIT — Further Features & Performance Task List

Companion to `plan.md`. Task numbers continue from the completed JIT work (T01–T56
plus bimorphic inlining and the abandoned T48 are in git history) and start at T57 to
stay unambiguous. Each task names its direct predecessors under **Depends on**.

Existing pieces referenced: `JitDecoder` (decode), `VirtualMachine.Jit*` helpers,
`ReflectionEmitJitCompiler` (`EmitCall`, `VerifyStackConsistency`, `StackEffect`,
conservative emit loop, inliner), `ClosureThreadedJitCompiler`, `DynamicMethodBuilder`.

---

# Group A — Coverage

## Phase 1 — Short-circuit & ternary

### T57 — Decode conditional-pop jumps
**File:** `DScript/Jit/JitInstruction.cs`, `DScript/Jit/JitDecoder.cs`
**Work:** Add instructions for `JumpIfFalseOrPop`, `JumpIfTrueOrPop`,
`JumpIfNullOrUndefined`, `JumpIfDefined` (each carrying a resolved target index),
decoded alongside the existing jumps. No longer hit the decline `default`.
**Depends on:** — (extends T30)

### T58 — Per-edge stack verification
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`
**Work:** Extend `VerifyStackConsistency`/`StackEffect` so a jump's stack delta can
differ between its branch and fall-through edges (OrPop keeps on branch / pops on
fall-through; `JumpIfNullOrUndefined` pushes on both). Decline if inconsistent.
**Depends on:** T57

### T59 — Emit conditional-pop jumps
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`, `DScript/Jit/DynamicMethodBuilder.cs`
**Work:** Lower each to IL: peek/duplicate the condition, test truthiness /
null-or-undefined, conditional branch, and conditional pop matching the opcode. Closure
back-end continues to decline control flow.
**Depends on:** T58

### T60 — Tests: short-circuit & ternary
**File:** `DScript.Test/JitShortCircuitTests.cs`
**Work:** `a && b`, `a || b`, `a ?? b`, `o?.x`, `c ? a : b`, nested/chained, mixed with
loops — JIT result matches interpreter (Reflection.Emit; closure declines).
**Depends on:** T59

## Phase 2 — Method calls

### T61 — Decode method-call opcodes
**File:** `DScript/Jit/JitInstruction.cs`, `DScript/Jit/JitDecoder.cs`
**Work:** Decode `GetPropMethod(N)` / `GetPropCall0(N)` (leave `[receiver, fn]`) and
`CallMethod` (non-tail). Capture call-site profile callee(s) as for `Call`.
**Depends on:** — (extends T16/T30)

### T62 — Emit method dispatch (with receiver)
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`, `DScript/Jit/ClosureThreadedJitCompiler.cs`, `DScript/Vm/VirtualMachine.cs`
**Work:** Emit the receiver-preserving lookup + `vm.InvokeCallable(callee, receiver,
args)` on both back-ends (helper if needed). Stack effects added to `StackEffect`.
**Depends on:** T61

### T63 — Tests: method calls
**File:** `DScript.Test/JitMethodCallTests.cs`
**Work:** own + inherited method, zero-arg fast path, chained `a.b().c()`, getter-backed
— matching the interpreter on both back-ends.
**Depends on:** T62

## Phase 3 — Object & array literals

### T64 — Literal runtime helpers + decode
**File:** `DScript/Vm/VirtualMachine.cs`, `DScript/Jit/JitInstruction.cs`, `DScript/Jit/JitDecoder.cs`
**Work:** Helpers mirroring `NewObject`/`InitProp`/`NewArray`/`InitElem`; decode them
(decline computed keys / spread for now).
**Depends on:** — (extends T16)

### T65 — Emit literals (both back-ends)
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`, `DScript/Jit/ClosureThreadedJitCompiler.cs`, `DScript/Jit/DynamicMethodBuilder.cs`
**Work:** Lower the literal instructions on both back-ends (straight-line); add
`StackEffect` entries.
**Depends on:** T64

### T66 — Tests: literals
**File:** `DScript.Test/JitLiteralTests.cs`
**Work:** object literal, array literal, nested, returning a built object — both
back-ends match the interpreter.
**Depends on:** T65

## Phase 4 — `let`/`const` block scopes

### T67 — Block-scope env helpers + current-env tracking
**File:** `DScript/Vm/VirtualMachine.cs`, `DScript/Jit/DynamicMethodBuilder.cs`
**Work:** `JitEnterBlock(env)`→child block-scope env, `JitLeaveBlock`→parent; the
emitter keeps the current env in an IL local that variable ops resolve against.
**Depends on:** — (extends T36)

### T68 — Decode/emit `EnterBlock`/`LeaveBlock`
**File:** `DScript/Jit/JitDecoder.cs`, `DScript/Jit/ReflectionEmitJitCompiler.cs`, `DScript/Jit/ClosureThreadedJitCompiler.cs`
**Work:** Decode the block opcodes; emit env push/restore around the block body.
**Depends on:** T67

### T69 — Tests: block scopes
**File:** `DScript.Test/JitBlockScopeTests.cs`
**Work:** `for (let i …)`, nested blocks, shadowing, `const` — match the interpreter.
**Depends on:** T68

## Phase 5 — Tail calls

### T70 — Compile non-self tail calls safely
**File:** `DScript/Jit/JitDecoder.cs`, `DScript/Jit/ReflectionEmitJitCompiler.cs`
**Work:** Decode `TailCall`; emit as `InvokeCallable` + `return`. **Decline when the
tail callee may be the current function** (self-recursion relies on the interpreter
trampoline). Document the limitation.
**Depends on:** — (extends T14)

### T71 — Tests: tail calls
**File:** `DScript.Test/JitTailCallTests.cs`
**Work:** non-recursive `return f(x)` compiles and matches; self-tail-recursion
declines and still runs correctly (no overflow regression).
**Depends on:** T70

---

# Group B — Performance

## Phase 6 — Speculative unboxed loop tier

### T72 — Loop-tier eligibility
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`
**Work:** Detect a pure, call-free function whose binary sites + loop variables are
profiled int-only (or double-only), with only supported control flow + assignments.
**Depends on:** — (extends the speculative tiers + T31/T38)

### T73 — Unboxed control flow + register locals
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`, `DScript/Jit/DynamicMethodBuilder.cs`
**Work:** Flow raw `int`/`double` through depth-indexed slot-locals across branches;
promote loop counter/accumulator to IL `int`/`double` locals (no boxing per iteration).
**Depends on:** T72

### T74 — Deopt with a clean operand model
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`, `DScript/Vm/VirtualMachine.cs`
**Work:** On a mid-loop type surprise, bail to the interpreter (re-run the pure chunk),
ensuring the deopt site has a clean stack. Reuse `Deoptimize`/`DeoptFrame`.
**Depends on:** T73

### T75 — Tests + benchmark: loop tier
**File:** `DScript.Test/JitLoopTierTests.cs`, `DScript.Benchmark/Program.cs`
**Work:** int and double accumulator loops, nested loops, type-surprise deopt — match
the interpreter; benchmark shows the unboxed loop beating the conservative tier
(numbers in the commit message).
**Depends on:** T74

## Phase 7 — Inlining beyond pure-parameter leaves

### T76 — Inline control-flow leaf callees
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`
**Work:** Relax `TryGetInlineBody`/`EmitInlinedBody` to splice callees containing
branches (fresh label set; slot model). Still leaf (no calls), still pure-param.
**Depends on:** — (extends T41/T42, needs T31)

### T77 — Inline global-reading callees
**File:** `DScript/Jit/ReflectionEmitJitCompiler.cs`
**Work:** Allow inlined bodies to read globals (resolved through the caller-reachable
global scope). Still decline callees that capture their defining environment.
**Depends on:** T76

### T78 — Tests + benchmark: extended inlining
**File:** `DScript.Test/JitInliningTests.cs`, `DScript.Benchmark/Program.cs`
**Work:** branchy helper inlined; global-reading helper inlined; closure-capturing
helper falls back — match the interpreter; benchmark records the delta.
**Depends on:** T77

---

## Summary — dependency order at a glance

```
Phase 1 (short-circuit): T57 → T58 → T59 → T60
Phase 2 (method calls):  T61 → T62 → T63
Phase 3 (literals):      T64 → T65 → T66
Phase 4 (block scopes):  T67 → T68 → T69
Phase 5 (tail calls):    T70 → T71
Phase 6 (loop tier):     T72 → T73 → T74 → T75
Phase 7 (inlining+):     T76 → T77 → T78
```

Recommended order: **Phase 1** (short-circuit/ternary — biggest coverage gap, moderate
risk) first; then **Phase 6** (unboxed loop tier — biggest perf win, hardest) as a
focused effort; **Phases 2–5** are independent coverage adds pickable in any order;
**Phase 7** builds on the inliner. Group A phases are mostly independent of each other.
