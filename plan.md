# DScript — Optimisation Plan

This document describes each planned performance optimisation with scope, design
notes, and effort estimates. See `tasks.md` for the ordered implementation checklist.

---

## 1. Compound-assignment `BinaryIntConst` peephole

**Problem**  
`x += 1` currently emits `GetVar x; Constant 1; Binary +; SetVar x`. The existing
`TryUpgradeBinaryConstToInt` peephole only fires on standalone binary expressions.
Compound assignments in `CompileIdentifierChain` and `CompileMemberChain` bypass it
entirely, so `i++`, `x += n`, `i -= 1`, etc. never use the fast inline-int path.

**Fix**  
After emitting the `Binary` op inside the compound-assignment branch of
`CompileIdentifierChain` and `CompileMemberChain`, call
`chunk.TryFuseConstantBinary` + `chunk.TryUpgradeBinaryConstToInt` exactly as
`EmitBinary` does. No new opcodes needed — pure compiler change in
`Compiler.Factor.cs`.

**Effort:** Small (half a day).  
**Impact:** High — every `i++`, `i--`, and `x += literal` in a hot loop.

---

## 2. `GetProp` cache hash improvement

**Problem**  
The inline property cache is 256 slots direct-mapped by `nameIndex & 0xFF`. Two
property names whose indices share the same low byte evict each other on every
access, even if the objects and names are completely unrelated.

**Fix**  
Replace the index function with Fibonacci / multiplicative hashing:

```csharp
int slot = (int)((uint)nameIndex * 2654435761u >> 24);
```

This spreads indices uniformly across all 256 slots, reducing collision rate from
O(names/256) to near-O(1). Change is a single line in the `GetProp` handler of
`VirtualMachine.cs`. Optionally upgrade to a 2-way associative cache (store two
`(object, shapeVersion, nameIndex, link)` entries per slot) to handle the remaining
collisions.

**Effort:** Trivial (< 1 hour).  
**Impact:** Medium — noticeably fewer cache misses in property-heavy loops.

---

## 3. Spread operation single-pass helper

**Problem**  
`PushSpread`, `MergeObject`, `CallSpread`, and `CallMethodSpread` all iterate the
`ScriptVar` linked-list child chain using `FindChild(IndexName(i))` — one O(n) linked-
list walk per element plus a string conversion on every index. Spreading an array of
length n costs O(n²) list traversals.

**Fix**  
Add a private `ScriptVar[] ExtractArrayElements(ScriptVar arr)` helper in
`VirtualMachine.cs` that does a single pass: walk `arr.FirstChild` / `.Next` once,
collect into a `ScriptVar[]`, skip non-numeric-key children. Use this in all four
spread-related opcode handlers instead of the per-element `FindChild` loop. Also
remove the per-element `string.Format` / `IndexName` call in the tight path.

**Effort:** Small (1 day).  
**Impact:** High — O(n²) → O(n) for any spread operation.

---

## 4. `for...of` iterator protocol fused opcode

**Problem**  
Each `for...of` iteration emits five separate bytecode operations:
`GetVar $iter; GetProp next; Call 0; GetProp done; JumpIfTrue end; GetProp value`.
That is six dispatch cycles plus three property lookups per loop iteration, all for
a fixed protocol.

**Fix**  
Add a `ForOfStep` opcode (no operands):

```
ForOfStep  // iterator → (value, doneFlag)
           // pops iterator, calls .next(), pushes {done, value}
           // if done: pushes undefined + sets internal flag
```

The `CompileForOf` in `Compiler.Statement.cs` emits `ForOfStep` instead of the
`GetVar/GetProp next/Call/GetProp done/GetProp value` sequence. The VM handler calls
the `.next()` method natively without going through the full call dispatch for each
invocation. The loop condition becomes a single `JumpIfTrue` on a boolean the handler
places on the stack.

New opcode: `ForOfStep` (1 byte, no operands).  
**Effort:** Medium (1–2 days).  
**Impact:** Medium — saves ~4 dispatch cycles per iteration on every `for...of`.

---

## 5. Array literal spread: eliminate double-parse and per-element `GetProp length`

**Problem**  
`CompileArrayLiteral` clones the lexer and pre-scans the entire literal to detect
whether any element is a spread (`...`). If spread is found it then re-parses all
elements. Additionally, for every non-spread element following a spread, it emits
`Dup; GetProp "length"` at runtime to compute the insertion index.

**Fix — detection:**  
Remove the pre-scan clone. Instead parse elements in a single left-to-right pass,
keeping a `bool seenSpread` flag. When the first spread is encountered mid-parse,
emit a one-time `Dup; GetProp "length"` to seed the insertion index, then switch to
spread-aware emit for the remainder. This makes the common no-spread case do zero
extra work.

**Fix — insertion index:**  
Track the compile-time count of static elements before the spread in a local
`int staticCount`. When emitting spread elements, push `staticCount` as a `Constant`
instead of re-reading `arr.length` at runtime. Only fall back to the runtime read
when a prior spread was dynamic.

New opcodes: none.  
**Effort:** Medium (1 day).  
**Impact:** Medium — eliminates one full lexer clone per array literal and O(n)
runtime `GetProp length` calls for mixed static/spread arrays.

---

## 6. Stackless generator state machine

**Problem**  
Each `function*` invocation spawns a background OS thread plus two `SemaphoreSlim`
semaphores. Thread creation costs ~0.5–2 ms and the pair of semaphore signal/wait
operations adds ~1–5 µs of overhead per `yield`. For generators used in tight loops
(e.g. `for...of range(1000)`) this dominates.

**Fix**  
Transform simple `function*` bodies into a stackless state machine at compile time,
matching the pattern C# uses for `yield return`:

1. The compiler numbers each `yield` point (0, 1, 2, …).
2. The body is split into segments between yields; each segment is compiled into a
   separate inner `Chunk` stored on the parent chunk.
3. A `GeneratorState` object (replaces `GeneratorObject`) holds the current segment
   index, the local-variable frame, and the last yielded value — no thread.
4. `.next()` runs the next segment synchronously, returns `{value, done}`.

The thread-based path is kept as a fallback for generators that `await` (async
generators) or capture mutable closures in a way the static analysis cannot model.

New types: `GeneratorState`, `GeneratorSegment` (inner chunks).  
**Effort:** Large (3–5 days).  
**Impact:** High — eliminates thread overhead entirely for the common case.
