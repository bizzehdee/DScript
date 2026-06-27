# Objects & Classes rewrite plan

Scoping document for closing the remaining performance gap on the **Objects** and
**Classes** workloads. Companion to `performance-plan.md` (which holds the per-task
history and bench baselines). This file is the *plan*; update task status there.

## Goal

| Workload | Current vs v8 | Target |
|---|---|---|
| Classes | ~22× | ≤ 10× |
| Objects | ~16× | materially lower |

Both are **allocation-bound, not lookup-bound** — established this session:
- Shape-tracking object literals gave **no** Objects gain and regressed Spread 20–60% (reverted).
- Inherited-method inline caching shown to be ~6% of Classes time (proto-method 909 ms vs
  field-reads-only 857 ms) — not the bottleneck.
- Per-property `ScriptVarLink`s and frame `vars` are pooled; the **un-pooled** costs are
  (1) the instance/literal `ScriptVar` itself and (2) one `Environment` per call.

A second structural finding from the code audit: today's shape "slots" are **not O(1)**.
`PropCacheCell.WalkShapeRoot` (`PropCacheCell.cs:104`) reaches a property by walking the
`ScriptVarLink` linked list `Next` *slotIndex* times. So even an inline-cache *hit* is an
O(slot) pointer chase, and every property is still a heap `ScriptVarLink`. A real flat slot
array fixes both the allocation and the lookup cost at once.

## ⚠️ Phase 0a result — the "allocation-bound" premise is FALSIFIED

Phase 0a (instance `ScriptVar` pooling) was implemented, full suite green on both TFMs,
and benchmarked best-of-5: **no measurable gain** — Objects ~612 vs ~608, Classes ~458 vs
~463 (within noise). Pooling fired correctly (Classes instances *are* disposed per
iteration), so this is a real null result, not a wiring miss. **Reverted.**

Conclusion: .NET gen0 GC handles these short-lived `ScriptVar`s cheaply; **the bottleneck
is CPU, not allocation.** A CPU trace (`dotnet-trace`) confirms Classes is JIT-compiled yet
still ~22×, with time spread across object-model bookkeeping — `AddChild`, `Shape.Transition`
(two `Dictionary.FindValue` per property), `FindChild`, `FindInParentClasses`,
`JitGetPropCached` — no single hot spot. (The sampling collapsed to a coarse bucket; clean
per-method attribution still needs symbol resolution work.)

**Revised hypothesis:** the win, if any, comes from cheapening per-property CPU work — replace
the per-property `Dictionary`/linked-list operations with flat array indexing (Lever B, for
CPU reasons not GC). But three rejected experiments this session (literal shapes, method
cache, instance pooling) suggest the engine is near its architectural floor for OOP without a
deeper object-representation change. **Do not commit the full multi-week programme on the
original premise** — validate Lever B1 as a bounded probe first.

## ✅ Clean CPU profiles — the priorities were BACKWARDS

With `dotnet-trace` symbol resolution fixed (attribute past the synthetic
`CPU_TIME`/`UNMANAGED_CODE_TIME` leaf markers), here is real per-method self-time.

**Objects** (`{a,b,c,d}` literal + 4 reads):
| % self | method | meaning |
|--:|---|---|
| 31.1 | `JitGetVar` | **variable read by name** — `env.Resolve` scope-chain walk, **no inline cache** |
| 27.2 | `jit_<main>` | compiled loop body |
| 15.2 | `JitSetVar` | **variable write by name** |
| 8.6 | `JitGetPropCached` | `o.a`…`o.d` |
| 6.0 | `IntBinary` | the `+`s |
| 3.3 | `AddChild` | building the literal |
| 2.3 | `SequenceEqual` | string name compares |

→ **46% of Objects is name-based variable resolution**, only ~12% is the object
representation. The plan's claim that "only Lever B (object slot arrays) moves Objects" is
**wrong**: the JIT helpers `JitGetVar`/`JitSetVar` do a full `env.Resolve` walk every access
with **no inline cache** (the interpreter has `ResolveCached`; the JIT path skipped it).

**Classes** (`new P(i,i+1).sum()`): cost is spread — `Execute` 42%,
`InvokeVmFunctionFromStack` 10.7%, `_noSetterCache` `Dictionary<(ScriptVar,string),bool>`
**7.2%** (ValueTuple hash+probe on every `this.x=`/`this.y=`), `jit_P` 7%, `InvokeCallable`
5.5%, shape dict lookups (`Shape.Transition` + `Slots`) ~4.6%, `AddChild` 2.9%,
`OsrDeclinedOffsets.Contains` (HashSet, per back-edge) **2.6%**, `CastHelpers.StelemRef`
array-covariance **2.7%** (a cost introduced by **unsealing** `ScriptVar` in T3.1a).

### Revised, evidence-based work list (targeted — NOT the big rewrite)

| # | Win | Target | ~self-time | Risk |
|---|---|---|--:|---|
| W1 | **Inline-cache the JIT variable helpers** (`JitGetVar`/`JitSetVar` cache the resolved link per site, like `ResolveCached`). | Objects, Classes | up to ~46% of Objects | Med |
| W2 | Replace `_noSetterCache` dict with a write-site inline cache / per-prototype no-setter flag. | Classes | ~7% | Low-Med |
| W3 | Cache the OSR-declined decision instead of `HashSet<int>.Contains` per back-edge. | Classes, all loops | ~2.6% | Low |
| W4 | Bypass array-covariance (`StelemRef`) on the operand-stack/args stores (unsafe store, or re-seal strategy) — covariance came from unsealing `ScriptVar`. | all | ~2.7% | Low-Med |

These are additive, independently testable, and far lower risk than Levers A/B. Lever A
(full positional slot frames) remains the *ceiling* for variable resolution, but **W1 likely
captures most of its Objects payoff at a fraction of the risk** — do W1 first and re-measure
before considering the full slot-frame rewrite. Lever B (object slot arrays) is **deprioritised**:
the data shows the object representation is a minor cost on both workloads.

### ⚠️ W1 attempt — naive inline cache REGRESSED (~22%), reverted

Implemented `VarCacheCell` + `JitGetVarCached`/`JitSetVarCached` keyed on (env identity,
version), wired into both JIT back-ends. Full suite green, but bench regressed: Objects
~742 vs ~608, Classes worse, Closures ~33→~85. **Reverted.**

Root cause (confirmed empirically): `CompileFor` puts the block-scoped loop body *inside*
the loop, so `EnterBlock` runs every iteration and `JitEnterBlock` allocates a **fresh
Environment per iteration**. Every in-loop variable access starts from that per-iteration
env, so an env-identity-keyed cache **misses every iteration** — the cache check is then
pure overhead on top of the `env.Resolve` it can't avoid.

**The real W1 is bigger than "add a cache":** the enabler is **stable block-scope
environments** — reuse the block env across loop iterations when the function captures no
closure (`RecyclableFrame`), instead of reallocating it. That makes env identity stable, at
which point both the interpreter's existing `ResolveCached` *and* a JIT var-cache pay off
(and it also removes the per-iteration `Environment`+vars allocation). This touches
`EnterBlock`/`LeaveBlock` in the interpreter and JIT plus a per-block escape check — it is
effectively part of Lever A and carries Lever-A risk (the scope/closure model).

**Reordering:** W2/W3/W4 are independent of W1 and remain valid, confirmed, lower-risk wins.
Do those first; treat "stable block envs + var caching" as its own larger Lever-A-scoped
effort to decide on separately.

### ⚠️ Real-W1 attempt (stable block envs) — abandoned on fragile JIT/OSR interactions

Implemented: `Environment.GetOrCreateReusableBlock(site)` (reuse one block env per site when
`RecyclableFrame`), interpreter `EnterBlock` reuse, and idempotent `let`/`const` re-declare
(`ScriptVarLink.ResetForRedeclare` — reset to undefined, no version bump) in both interpreter
and JIT declare helpers. Small loops worked, but at scale (`for i<1e6`) the workload throws
**`o is const, cannot assign a new value`** once the loop OSRs into JIT.

Root cause class: the interpreter reuses a block env and resets the `const` binding, but the
interpreter↔JIT↔OSR hand-off resolves/initialises the binding against a *different*
environment than the one that was reset (currentEnv vs the frame `env` arg; the JIT still
allocates fresh block envs via `JitEnterBlock`). Making all four paths
(interpreter EnterBlock/Declare, JIT EnterBlock/Declare, OSR resume env, const-init SetVar)
agree on one reused env is exactly the holistic binding-model work of **Lever A** — it cannot
be retrofitted safely as an opportunistic change. This was the **4th** correctness/perf
failure in the scope/closure/JIT area this session (env-pooling, naive var-cache, two here),
which is strong evidence that variable-resolution speed needs the full Lever-A slot-frame
rewrite or nothing. **Reverted.**

**Net for the session:** T3.1b is the one banked win (on master). Objects/Classes are
near the engine's floor without a dedicated, holistic Lever-A rewrite; the remaining safe,
independent wins are W2 (`_noSetterCache`), W3 (OSR HashSet), W4 (covariance).

## Guiding principles (lessons banked this session)

1. **Additive fast path + working fallback.** Never delete the slow path; *deopt* to it.
   The shape system already does this (invalid shape → linked-list walk); keep that model.
2. **Each phase is independently shippable**: its own commit, full suite green on net8.0 +
   net10.0, `bench.ds` before/after recorded, `>10%` regression auto-rejected.
3. **No piecemeal lifecycle/pooling changes outside their enabling structure.** Standalone
   `Environment` pooling corrupted callback/`arguments` state (28 failures) because the
   frame lifetime assumption didn't hold without the slot model around it. Pooling lands
   *with* the structure that makes it sound, not before.
4. **Reversibility is a feature.** Every phase must be `git revert`-able in one commit.
5. New opcodes **appended to the enum end** (project rule); behaviour stays ES-invisible.

## The two structural levers

### Lever B — Object slot arrays  *(the Objects lever; also Classes field access)*

**Now:** a shape-tracked object stores property values in `ScriptVarLink`s; the linked list
is the source of truth; reads walk `_shapeRoot` by `Next` (`ScriptVar.Shaped.cs`,
`PropCacheCell.cs:104`); each property is one pooled link; metadata
(`Enumerable`/`Writable`/`Configurable`/`Getter`/`Setter`/`IsConst`) lives on the link.

**Target:** shape-tracked objects keep property **values** in a flat `ScriptVar[] _slots`
indexed by the shape's slot index. O(1) reads; no per-property link in the common case
(own data property, no accessor, no delete).

**Why this (and only this) moves Objects:** the Objects benchmark has no function calls —
only the object representation matters. Confirmed: shape-tracking alone (transitions, no
slot array) *regressed*; the win requires the value array, not just the hidden class.

**What depends on the linked list being authoritative** (all must keep working — audited):
for-in (`EnumKeys`), `JSON.stringify` (`ScriptVar.AppendJson:1624`), `Object.keys/values/
entries` (`ProviderHelpers.ForEachEnumerableChild:55`), `delete` (`RemoveLink`), freeze/seal,
`CopyValue`/`DeepCopy`, debugger introspection, function param-name enumeration
(`GetParsableString`), bytecode (de)serialization. Plus per-property metadata + accessors.

**Design options:**
- **B1 — dual representation (lower risk, partial win).** Keep the linked list for order +
  metadata; add `_slots[]` carrying values for shape-tracked data properties. Reads become
  `_slots[idx]` (true O(1), kills the `WalkShapeRoot` chase); writes update both. *Does not*
  remove the link allocation, but removes the walk and is a safe stepping stone.
- **B2 — slots-primary, lazy list (higher risk, full win).** `_slots[]` is the value store;
  the linked list is materialised lazily only when a consumer (enumeration / metadata /
  deopt) needs it. Removes the per-property link allocation — the real Objects win — but
  every list consumer must trigger materialisation correctly.

**Recommendation:** ship **B1**, measure, then take **B2** only if B1's gain justifies the
materialisation seams.

### Lever A — Positional local-slot frames + sound Environment pooling  *(Classes calls, Functions, Closures)*

**Now:** params/locals are bound by named `AddChild` into a per-call `vars` `ScriptVar`;
resolution walks the `Environment.Parent` chain by name with a per-site cache
(`ResolveCached`, `Chunk.InlineCacheEntry`); closures capture the defining `Environment`
(`VmFunction.Captured`) and resolve free vars by name; an `Environment` is allocated per
call. `this` is a named `"this"` child.

**Target:** the compiler resolves params/locals to **integer slots**; the frame is a
`ScriptVar[]`; **captured** variables are boxed into cells so the frame itself can be pooled;
closures capture cells, not the frame — which is what makes `Environment` pooling sound.

**Hard parts (why this is the real T3.2):** upvalue **cells** for captured variables (decided
at compile time), plus `arguments`, `eval` (dynamic declarations), the debugger's variable
view, and **two** JIT back-ends (`ReflectionEmitJitCompiler`, `ClosureThreadedJitCompiler`)
that resolve params by name today and would read slots / `JitDelegate.args`.

## Phased sequence

Ordered so the cheap, broad win lands first and the Objects-critical work precedes the
call-path work (only Lever B moves Objects; Lever A only helps call-heavy workloads).

| Phase | What | Risk | Moves | Reversible |
|---|---|---|---|---|
| **0a** | **Instance `ScriptVar` pooling.** Recycle `Dispose`d objects via a bounded pool, exactly like the `ScriptVarLink` pool. Attacks the un-pooled instance allocation for **both** Objects and Classes with no representation change. Sound by the same argument link-pooling is: only `refs→0` objects dispose, and pure stack temporaries never dispose, so no use-after-free. | Med | Objects, Classes | ✓ |
| **B1** | Object slot-array **reads** (O(1)), dual representation. | Med-High | Objects, Classes field reads | ✓ |
| **B2** | Slots-primary + lazy link materialisation (drop per-property link alloc). | High | Objects, Classes | ✓ |
| **A1** | Compiler slot resolution + `GetLocal`/`SetLocal` opcodes for non-captured params/locals; frame `ScriptVar[]`; named path retained for captured/eval/debug. | High | Functions, Classes, Closures | ✓ |
| **A2** | Upvalue **cells** for captured vars → enables sound `Environment` pooling. | High | Closures, Classes | ✓ |
| **A3** | Both JIT back-ends read slots + use `JitDelegate.args`. | High | all calls | ✓ |

**Recommended path:** `0a → B1 → re-measure → B2 and/or A1 → A2 → A3`. Re-measure after B1:
if Objects/Classes hit target, B2 / Lever A may be deferrable.

## Risk register & mitigations

- **Pool use-after-free / cycles** (CLAUDE.md object-graph rule): gate pooling on `refs==0`
  dispose; bound pool size; the full suite catches lifetime bugs (it caught the env-pooling
  ones in <60 s). Run the suite after every pooling wiring change.
- **Slot-array deopt correctness:** B1 keeps the linked list authoritative; heavy tests for
  `delete`, accessor install, for-in order, JSON order, freeze/seal, spread, sparse arrays.
- **Closure capture (A2) — the single riskiest item:** dedicated matrix — counter closures,
  `let` captured in loops (per-iteration binding), recursion, generators/async capturing
  params, `arguments` captured by a nested arrow.
- **Two JIT back-ends drift:** change both in A3; OSR int/loop tiers already cache guarded
  params in CLR locals and are largely unaffected, but verify deopt paths.
- **bench.ds variance** is high (Spread/TypedArrays swing 20%+ run-to-run). Use best-of-N
  interleaved A/B, not single runs, before accepting/rejecting a phase.

## Rough effort

0a ≈ 0.5 d · B1 ≈ 2–3 d · B2 ≈ 3–5 d · A1 ≈ 2 d · A2 ≈ 3–4 d · A3 ≈ 2–3 d.

## Open decisions for the user

1. **Appetite / stopping point:** is the goal "ship 0a + B1 and re-measure" (likely the best
   effort/risk ratio), or commit to the full `0a → B2 → A` programme?
2. **Branch strategy:** land each phase on `master` behind its gates, or stage the whole
   programme on a `perf/objects-classes` branch and merge once green end-to-end?
3. **B2 vs stop at B1:** accept the higher-risk lazy-materialisation rewrite for the full
   Objects allocation win, or stop at B1's O(1) reads if that alone closes enough of the gap?
