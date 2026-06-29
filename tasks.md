# Tasks — Arrays & Objects performance (levers 4 → 3 → 1 → 2)

Execution order is top to bottom. See `plan.md` for rationale, approach, and risks.
Conventions: `[ ]` todo · `[~]` in progress · `[x]` done. Every code task ends at a
**commit gate** — full suite green + benchmarked + no >3 % regression (CLAUDE.md policy).

## Lever 4 — `Array.from` fast path (+ `filter` copy audit)

- [x] **4.0 Baseline.** Record `bench.ds` Arrays (median of ≥3) on RE and on a published
      AOT binary. Stage-time `Array.from` alone, `+filter`, `+map`, `+reduce`.
- [x] **4.1** Read `Array.from` impl + the array backing grow path (`SetArrayIndex`,
      `_elements`). Confirm the growth/realloc cost and where pre-sizing can apply.
- [x] **4.2** Pre-size the backing `ScriptVar[]` to a known length for `Array.from` when the
      source length is known (array-like `length`, array, string); fill by index.
- [x] **4.3** Audit `ArrayFilterImpl`'s per-element `DeepCopy`; check interned/primitive
      short-circuit; skip the copy where provably unnecessary.
- [x] **4.4 Tests** (`DScript.Test`, NUnit): `Array.from` happy path + edge cases (mapper
      arity, `this` arg, non-integer/zero/missing `length`, iterable vs array-like, holes);
      `filter` non-aliasing where required. Coverage stays ≥90 %.
- [x] **4.5 Bench + commit gate.** Re-run 4.0; confirm Arrays improves, no >3 % regression
      elsewhere (both back-ends). Full suite green. Commit (e.g. `Array.from pre-sizing`).

## Lever 3 — Array pipeline fusion (filter→map→reduce, one pass)

- [x] **3.0 Baseline.** Re-capture Arrays after Lever 4 (RE + AOT).
- [x] **3.1 Design review.** Choose lazy-view/transducer vs. peephole fusion; write the
      escape/observability fallback rules (when an intermediate is captured, mutated,
      indexed, `.length`-queried, iterated, or consumed out of order → fall back to eager).
- [x] **3.2** Implement the fused path behind a `Disable*` kill switch.
- [x] **3.3 Adversarial tests** proving fused ≡ eager: result equality **and** callback
      side-effect order for — escape intermediate to a var, mutate the intermediate, early
      terminate, throwing callback, nested chains, empty/one-element arrays.
- [x] **3.4 Bench + commit gate.** Arrays improves; suite green; no >3 % regression; both
      back-ends. Commit.

## Lever 1 — Flat slot storage for shaped objects

- [x] **1.0 Baseline.** Objects ~620 ms, Classes ~518 ms (median of 3 RE runs post-Lever-3).
- [x] **1.1 Design** the flat-slot layout for `ShapedScriptVar`: keep linked list as source of
      truth; add `_slots: ScriptVarLink[]` indexed by slot number as acceleration layer.
      Linked view kept for enumeration, serialisation, delete, getters/setters.
- [x] **1.2** Implement flat storage + shape transition wiring; keep enumeration **order**
      and the `ShapeTracked ⇔ ShapedScriptVar` invariant.
- [x] **1.3** Make the interpreter `GetProp`/`SetProp` shape path O(1) (index, no `Next`
      walk); make the JIT `PropCacheCell` / field-read/-write paths use the slot index.
- [x] **1.4 Correctness tests**: `FlatSlotStorageTests.cs` — slot reads/writes, enumeration
      order, shape invalidation on delete/getter, inline-cache hits (mono/bi-morphic),
      prototype chain, inheritance. Suite: 2423/2423 net8+net10.
- [x] **1.5** Re-evaluate opting `{}` literals into shape tracking (`NewObject` →
      `CreateShapeTracked`) **now that reads are O(1)**; measure; keep only if it wins.
      **Result: reverted.** Shape-transition overhead on 4× `InitProp` writes per object
      exactly cancels the O(1) read savings → Objects stays at ~620 ms. Not a win.
- [x] **1.6 Bench + commit gate.**
      RE JIT (10-run median): Classes 621→551 ms (-11 %), Objects 621→621 ms (0 %).
      AOT (3-run median): Classes 358→345 ms (-4 %), Objects 584→597 ms (+2 %, noise).
      All other workloads within noise on both back-ends. Suite 2423/2423 (net8+net10). Commit.

## Lever 2 — Escape analysis / scalar replacement

- [x] **2.0 Baseline.** Objects after Lever 1 (RE + AOT): RE ~621ms, AOT ~597ms.
- [x] **2.1 Design** the escape proof (conservative, default-to-escape). Define the eligible
      scope: single-block lifetime, statically-known shape, only direct `o.field`
      reads/writes, no dynamic keys/spread/`delete`/method calls/identity compare.
- [x] **2.2** Implement scalar replacement for non-escaping literals in RE JIT int-loop tier.
      Behind `DisableScalarObjectReplacement` kill switch. `ScalarObjectReplacements` counter.
- [x] **2.3** Escape routes are statically detected (no runtime deopt needed): non-GetProp reads,
      multiple assignments, callee passes, identity compares all decline scalar replacement.
- [x] **2.4 Adversarial escape-route tests** (`ScalarObjectReplacementTests.cs`): 14 tests
      covering happy path, kill switch, store-to-outer, return, callee pass, identity compare,
      mixed read/non-prop use, nested arithmetic, same property read twice.
- [x] **2.5 Bench + commit gate.** RE Objects: 618ms median (−0.5%, within noise; bench.ds
      uses 1e6 Double bound → Int+Double profile → int-loop tier declined there). Suite
      2437/2437 net8+net10. Committed: "Scalar object replacement in speculative-int-loop JIT tier".
      Note: `Pop` opcode (from i++ post-increment) added to int-loop tier as part of this work.
- [x] **2.6 Extend scalar replacement to OSR long-loop tier.** bench.ds Objects is called
      once with 1e6 iterations → always goes through OSR, not the invocation-count tier.
      Added `TryScalarReplaceObjects` call to `TryCompileOsrLongLoop` (with resumeIndex remap),
      `EnterBlock`/`LeaveBlock` as allowed no-ops in the region opcode check, `AnalyzeLongLoopCalls`,
      and emitter; synthetic scalar registers (`:` names) initialised to 0 at OSR entry (not
      loaded from env); not written back on exit. VirtualMachine.cs: exact-int Double constants
      (e.g. 1e6) profile as Int, enabling the speculative-int tier for such loops too.
      RE Objects: ~21ms (30× vs 621ms baseline, −97%); AOT Objects: ~600ms (within noise,
      no regression). Suite 2438/2438 net8+net10.

## Cross-cutting (per CLAUDE.md)

- [ ] Update `wiki/JIT.md` (and `Standard-Library.md` if `Array.from`/`filter` behaviour
      notes change) when each lever lands; bump the wiki submodule pointer.
- [ ] `ES-COMPATIBILITY.md`: only if a feature's support status actually changes (perf alone
      does not — expected to stay untouched).
- [ ] Keep `DScript.LanguageServer` unaffected (no lexer/parser/public-API change expected).
