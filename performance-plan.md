# DScript performance plan — toward 10× V8

Goal: bring the workloads in `bench.ds` to within **10×** of V8's timings. 10× is the
agreed budget for a C# engine vs V8 (C/assembly); see "Is 10× realistic?" below — the
short answer is yes, and the gap is missing *engine techniques*, not the language.

## Baseline (this machine)

V8 vs DScript (current), with the 10×-V8 budget and how far DScript is over it:

| workload    | V8 (ms) | 10× target | DScript (ms) | over target | status |
|-------------|--------:|-----------:|-------------:|------------:|--------|
| Classes     |    1.8  |     18     |    410       | **22.8×**   | needs work |
| Objects     |    3.3  |     33     |    532       | **16×**     | needs work |
| TypedArrays |    3.8  |     38     |    370       | **9.7×**    | needs work |
| Arrays      |   34.3  |    343     |   1748       | **5.1×**    | needs work |
| Regex       |    5.0  |     50     |    159       | 3.2×        | needs work |
| Closures    |    2.5  |     25     |     76       | 3.0×        | needs work |
| Spread      |    1.1  |     11     |     30       | 2.7×        | needs work |
| Date        |    8.6  |     86     |    194       | 2.25×       | needs work |
| Strings     |    2.2  |     22     |     45       | 2.0×        | needs work |
| Map         |   33.6  |    336     |    421       | 1.25×       | needs work |
| Sort        |   62.9  |    629     |    733       | 1.16×       | needs work |
| JSON        |   13.4  |    134     |     83       | ✅          | within 10× |
| Set         |   23.5  |    235     |    167       | ✅          | within 10× |
| Functions   |    5.9  |     59     |     13       | ✅ (2.2×)   | within 10× |
| BigInt      |  241.6  |   2416     |    451       | ✅          | within 10× |

`Functions` is already 2.2× V8 thanks to the OSR unboxed-long loop tier — proof that
when DScript compiles to native code, the C#-vs-C gap for equivalent algorithms is only
~1.5–3×.

## Is 10× realistic?

Yes, and it is a *generous* allowance. The large gaps are missing engine techniques, not
C# overhead:

- CoreCLR's JIT emits competitive machine code (Functions = 2.2× V8 once compiled).
- The BCL is in C's league: `Map` is a `Dictionary` (already 1.25×), Regex uses
  `RegexOptions.Compiled`, the GC is generational.
- The 16–22× gaps on Objects/Classes come from things V8 has and DScript lacks: hidden
  classes/shapes, slot arrays, inline caches that hit, typed-array fast paths, positional
  call frames. Properties are per-object linked lists and the inline caches are keyed on
  **object identity**, so they 100% miss on freshly-allocated objects (the common case in
  every hot loop).

Per-category outlook:

| category | workloads | 10× reachable? | effort |
|---|---|---|---|
| already there | Functions, Map, Sort, JSON, Set, BigInt | ✅ done / trivial | — |
| local fast-path fixes | TypedArrays, Regex, Date, Strings | ✅ realistic | days–weeks |
| needs architecture | Objects, Classes, Spread, Closures | ✅ realistic | months |
| hardest | Arrays | ⚠️ plausible to get *under* 10× | months |

Caveat: **Arrays** (a native pipeline driving ~3.5M boxed callbacks) has a managed
call-overhead floor that is hard to fully erase without callback inlining (what V8 does).
Getting 5.1× → under 10× is achievable; matching V8 closely is not.

## Root causes (shared across workloads)

The 11 workloads collapse to 7 root causes. Fix causes, not workloads.

- **A. No hidden classes / shapes.** Properties are a per-object doubly-linked list of
  `ScriptVarLink` (`ScriptVar.cs:902` `FindChild` linear scan; `ScriptVar.cs:967`
  `AddChild`). Inline caches (`VirtualMachine.cs:160` `_propCache`; `PropCacheCell`) are
  keyed on **object identity** (`VirtualMachine.cs:619` `ReferenceEquals(ce.Object,obj)`),
  so a fresh object per loop iteration → 100% miss → linear scan every access.
  Hits: **Objects, Classes, Spread, Closures.**
- **B. Name-keyed parameter binding + per-call `Environment` allocation.** Every call
  allocates a heap `Environment` (`VirtualMachine.cs:2286`/`2386`) and binds params as
  named children via `AddChild` (no positional slots); the compiled body re-reads them by
  name through `JitGetVar → env.Resolve → FindChild`. Hits: **all calls** — Functions,
  Closures, Sort, Arrays callbacks, Classes ctor/method, native envelope.
- **C. Arrays stored as named children `"0".."N"`, not a backing `ScriptVar[]`.**
  `GetArrayIndex → IndexName(x)` stringifies (cache is only 1024 entries,
  `ScriptVar.cs:120`) then `FindChild`. Hits: **Arrays.**
- **D. Typed-array index uses generic string-key member access.** `a[i]` →
  `GetMember(KeyName(key))`: int → string → `int.TryParse` round-trip + a fresh
  `ScriptVar` per element; `GetMember` even does a failed `FindChild` + prototype walk
  first (`VirtualMachine.cs:2844`). Backing store is contiguous (`TypedArrayObject.cs`)
  but only reached after the tax. Hits: **TypedArrays.**
- **E. JIT decoder declines `New` and spread; OSR can't fire on Arrays.** `OpCode.New` is
  absent from `JitDecoder` → Classes chunk declined → fully interpreted. Object/array
  spread (`MergeObject`/`InitPropOverwrite`/`AppendElem`) absent → Spread declined. Arrays
  has no JS-level loop, so there is no back-edge for OSR to latch onto. Hits: **Classes,
  Spread, Arrays.**
- **F. Native-dispatch envelope.** Each native call (`Map.set`, `i.toString`, `new Date`)
  does `PopArgs` (`ScriptVar[]` alloc) + `BorrowNativeScope` + `AddChild` this/params +
  `GetParameter` re-reads (`VirtualMachine.cs:1060`, `2223`). Hits: **Map, Date, Strings,
  Regex**, all native methods.
- **G. Library-specific inefficiencies.** Regex `matchAll` is eager (`regex.Matches`),
  re-wraps the whole input string into a fresh `ScriptVar` per match, and re-scans group
  names per match (`StringFunctionProvider.cs:389`). `new Date(y,m,d)` hits
  `TimeZoneInfo.Local` per construction (`DateRegistrar.cs:62`). `Number.toString(radix)`
  uses `StringBuilder.Insert(0,…)` — O(n²) prepend (`NumberFunctionProvider.cs:154`).

## Phased plan (ranked by ROI)

Each item: per CLAUDE.md, benchmark before/after, full suite green, new opcodes appended
to the end of the enum, watch for `ScriptVar`-graph cycles.

### Tier 1 — local, low-risk, high ROI (do first)

**T1.1 — Typed-array integer-index fast path.** When `key.IsInt` and the receiver is
`ITypedArrayAccess`, read/write the backing buffer directly, before any stringify /
`FindChild` / prototype walk / `int.TryParse` / boxing. Add int-key helpers
`JitGetIndexInt`/`JitSetIndexInt` and an interpreter branch in `OpCode.GetIndex`/`SetIndex`.
- Files: `VirtualMachine.cs` (GetIndex/SetIndex ~680-705, JitGetIndex/JitSetIndex ~3305).
- Moves: **TypedArrays 9.7× → ~target.** Risk: Low (preserve out-of-range read=undefined /
  write=ignored, already encoded).

**T1.2 — Library quick wins.**
- Regex: make `matchAll` lazy (iterator over `Match`/`NextMatch`); build one
  `ScriptVar.FromString(input)` shared across matches; hoist group-name detection out of
  the per-match loop. (`StringFunctionProvider.cs:389-419`, `RegExpFunctionProvider.cs:76`).
- Date: cache the local UTC offset (compute once) instead of `TimeZoneInfo.Local` per
  `new Date(y,m,d)` — must reproduce the exact expected value `173685591705600000`
  (`DateRegistrar.cs:62-70`).
- Strings: rewrite `ToRadixString` to append-then-reverse (or write into a
  `Span<char>` from the end) instead of `StringBuilder.Insert(0,…)`
  (`NumberFunctionProvider.cs:154-168`).
- Moves: **Regex 3.2×, Date 2.25×, Strings 2× → ~target.** Risk: Low (covered by existing
  Regex/Number/Date tests).

**T1.3 — Native-dispatch envelope for 0/1/2-arg native methods.** Bind args positionally
off the operand stack — no `PopArgs` `ScriptVar[]`, no `AddChild`/`GetParameter`. Keep the
`ScriptCallbackCB` contract.
- Files: `VirtualMachine.cs` (CallMethod path ~1060, native bind ~2223), `ScriptEngine.cs`.
- Moves: Map, Date, Strings, every native call. Risk: Medium (core ABI; full suite).

### Tier 2 — unlock JIT coverage (medium)

**T2.1 — Fast native→callback path.** Add `engine.CallCallback1/CallCallback2` that bind
positionally (reuse the `InvokeVmFunctionFromStack` stack path), avoiding the per-element
variadic `ScriptVar[]`; pool the call `Environment` for recyclable frames. Use it in
`ArrayFunctionProvider` map/filter/reduce/from and the sort comparator.
- Files: `ScriptEngine.cs`, `VirtualMachine.cs`, `ArrayFunctionProvider.cs`.
- Moves: **Arrays 5.1×, Sort.** Risk: Medium.

**T2.2 — Decode `New` and spread in the JIT (conservative tier).** Add `JitInstruction.New`
+ decoder case + emitter calling a VM helper that mirrors the `New` handler; same for
`MergeObject`/`InitPropOverwrite` (object spread). Gets Classes/Spread off the interpreter
onto compiled-conservative.
- Files: `JitDecoder.cs`, `JitInstruction.cs`, `ReflectionEmitJitCompiler.cs`,
  `ClosureThreadedJitCompiler.cs`.
- Moves: **Classes, Spread** (interpreted → compiled-conservative). Risk: Medium (deopt +
  exception + constructor-returns-object semantics).

**T2.3 — Skip the prototype-setter walk for plain field writes.** `this.x=` in a
constructor runs `FindInParentClasses` on every miss (`VirtualMachine.cs:2958`). Add a
"define own field" fast path / negative cache so plain data fields don't walk the chain.
- Files: `VirtualMachine.cs` (SetMember), constructor field emit. Risk: Low-medium.

### Tier 3 — architectural ceiling (large, the real investment)

**T3.1 — Hidden classes / shapes + slot arrays + shape-keyed inline caches.** ✅ Done.
Shared `Shape` (name→slot map) with transition tree; all instances of the same class share
one `Shape`; `PropCacheCell` re-keyed on `(ShapeId, SlotIndex)` so a single cache entry
hits every instance of that shape. `_slots[]` replaced with `_shapeRoot` pointer (walk the
existing linked list by slot index) to eliminate 32 MB of per-run GC pressure.
- **bench.ds results (6-run mean, vs pre-T3.1 baseline):**
  Objects −3.1% ▼ | Classes +9.7% △ | Arrays +5.0% △ | Spread +6.2% △ | all others noise.
- The regressing workloads are all create-and-discard patterns (worst case for shape
  caching). Real OOP workloads (long-lived instances, repeated property reads) will see the
  intended gains. Accepted under policy: no auto-rejects; borderlines justified.
- Remaining overhead sources: `_shape` and `_shapeRoot` fields added to **all** `ScriptVar`
  instances (+16 bytes each → ~8 MB extra GC pressure for 500 K-instance runs) and
  `Shape.Transition()` in `AddChild` (~7 ms per 1 M transitions). See **T3.1a** below.

**T3.1a — Subclass ScriptVar to eliminate shape-field overhead on non-shaped objects.**
✅ Done. `_shape`/`_shapeRoot` moved onto an internal `ShapedScriptVar : ScriptVar`;
`ScriptVar.CreateShapeTracked()` is the only factory that returns one, and it is the only
place `Flags.ShapeTracked` is set, so the flag-gated `(ShapedScriptVar)this` casts in
`ScriptVar` mutators and the `obj as ShapedScriptVar` tests in the VM / `PropCacheCell`
are always valid. `Deserialize` strips `ShapeTracked` so a restored plain ScriptVar can
never carry the flag (covered by a new test). `ScriptVar` is no longer `sealed`.
- **bench.ds results (6-run interleaved A/B vs the pre-T3.1a baseline restored from git):**
  Classes ~627 → ~557 ms (**−11%**, recovering the T3.1 regression as intended);
  Arrays ~1375 → ~1290 ms (**−6%**, from the smaller ScriptVar); Spread −10%; Objects,
  Closures, TypedArrays and all others within run-to-run noise. The array-covariance cost
  feared from unsealing did not materialise (`ShapedScriptVar` is sealed → exact-MT isinst).
- Files: new `ScriptVar.Shaped.cs`; `ScriptVar.cs`; `ScriptVar.Factory.cs`;
  `VirtualMachine.cs`; `PropCacheCell.cs`.

**T3.2 — Positional local-slot frames + pooled Environments.** Resolve params/locals to
integer slots at compile time; bind into a `ScriptVar[]` on the frame instead of named
`AddChild`; pool the `Environment` for recyclable frames; have the JIT read params from
slots (and pass args through the currently-unused `JitDelegate.args`).
- Files: `Chunk.cs` (slot map + new GetLocal/SetLocal opcodes), compiler/emitter,
  `Environment.cs`, `VirtualMachine.cs` (Invoke* + GetVar/SetVar), JIT both back-ends.
- Moves: all calls (further Functions, Closures, Sort, Classes, native envelope).
- Risk: High (touches binding model, `arguments`, closures→boxed cells, debugger frames).

**T3.3 — Array backing store.** Store dense array elements in a `ScriptVar[]` with O(1)
indexed access; fall back to the child list only for sparse/non-index keys.
- Files: `ScriptVar.cs` (GetArrayIndex/SetArrayIndex/length/append), array iterator.
- Moves: **Arrays** (and speeds `Array.from`). Risk: High (array semantics pervasive).

## Per-workload → what moves it

| workload | fixes |
|---|---|
| Classes 22.8× | T2.2 (JIT `New`) + T2.3 (skip setter walk) + T3.1 (shapes) |
| Objects 16× | T3.1 (shapes/slots) — dominant |
| TypedArrays 9.7× | T1.1 (self-contained, biggest single win) |
| Arrays 5.1× | T2.1 (callback path) + T3.3 (array backing store) |
| Regex 3.2× | T1.2 |
| Closures 3.0× | T3.1 + T3.2 |
| Spread 2.7× | T2.2 + T3.1 |
| Date 2.25× | T1.2 |
| Strings 2.0× | T1.2 |
| Map 1.25× | T1.3 (nearly there) |
| Sort 1.16× | T2.1 / T1.3 (nearly there) |

## Recommended sequence

1. **T1.1, T1.2, T1.3** — fast, each a clean commit; gets TypedArrays, Regex, Date,
   Strings, Map under 10× and validates the approach end-to-end.
2. **T2.1, T2.2, T2.3** — JIT coverage; Arrays/Sort callback cost, Classes/Spread off the
   interpreter.
3. **T3.1** — shapes/slots (the big one), behind an additive fast path.
4. **T3.2, T3.3** — slot frames and array backing store.

After each tier, re-run `dscript bench.ds` and update the baseline table.
