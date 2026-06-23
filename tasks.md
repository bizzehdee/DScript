# DScript — ES2020+ Conformance Tasks

Tasks are ordered by effort-to-impact ratio. Each phase is independently shippable and must be committed separately. See `plan.md` for full design notes.

Status: `[ ]` todo · `[~]` in progress · `[x]` done

---

## Phase 1 — Promise combinators + `finally` (ES2018/2021)

Find the existing Promise registration and extend it.

- [ ] `Promise.resolve(val)` — return an already-resolved Promise
- [ ] `Promise.reject(reason)` — return an already-rejected Promise
- [ ] `Promise.all(arr)` — resolve when all resolve; reject on first rejection
- [ ] `Promise.allSettled(arr)` — always resolve with `[{status, value/reason}]`
- [ ] `Promise.race(arr)` — settle with the first input to settle
- [ ] `Promise.any(arr)` — resolve with first fulfillment; reject with `AggregateError` if all reject
- [ ] `Promise.prototype.finally(fn)` — run `fn` on both paths; propagate original value/reason
- [ ] Tests: happy path for each method, rejection propagation, `AggregateError` from `Promise.any`
- [ ] Update `wiki/Standard-Library.md` and `README.md`

---

## Phase 2 — Logical assignment operators (ES2021)

Changes to `ScriptLex.cs` and `ScriptCompile.cs` only.

- [ ] Add `LexTypes.LogicalAndAssign` — lex `&&=`
- [ ] Add `LexTypes.LogicalOrAssign` — lex `||=`
- [ ] Add `LexTypes.NullCoalesceAssign` — lex `??=`
- [ ] Compile `a &&= b` → assign only if `a` is truthy (short-circuit; no re-evaluate of `a`)
- [ ] Compile `a ||= b` → assign only if `a` is falsy
- [ ] Compile `a ??= b` → assign only if `a` is `null`/`undefined`
- [ ] Tests: truthy/falsy guard, nullish guard, side-effect count (RHS only evaluated when needed)
- [ ] Update `wiki/Language.md` operators table

---

## Phase 3 — `globalThis` (ES2020)

One-liner native registration.

- [ ] Register `globalThis` at root level pointing to the engine root `ScriptVar`
- [ ] `globalThis === globalThis` evaluates to `true`
- [ ] Test: access a variable via `globalThis.x` after setting `var x = 1`
- [ ] Update `wiki/Language.md`

---

## Phase 4 — Numeric separators (ES2021)

Lexer-only change in `ScriptLex.cs`.

- [ ] Skip `_` between digits in integer and float literals (`1_000`, `1_000.5`, `1_000e2`)
- [ ] Skip `_` in hex (`0xFF_FF`), binary (`0b1010_0001`), octal (`0o7_7`) literals
- [ ] Reject `_` at start, end, adjacent to `.`, `e`/`E`, or `x`/`b`/`o` prefix — throw `SyntaxError`
- [ ] Tests: valid separators in all literal forms, invalid positions throw
- [ ] Update `wiki/Language.md`

---

## Phase 5 — `Symbol` (ES2015, required for iterable protocol)

Core VM change. Requires touching `ScriptVar`, the lexer, and property-key lookup.

- [ ] Add `SymbolType` value kind to `ScriptVar` (opaque unique identity; backed by a `ulong` counter or `Guid`)
- [ ] `Symbol(description?)` factory — calling `new Symbol()` throws `TypeError`
- [ ] `Symbol.for(key)` / `Symbol.keyFor(sym)` — global symbol registry
- [ ] `typeof sym` returns `"symbol"`
- [ ] Symbols usable as property keys: `obj[Symbol.iterator] = fn`
- [ ] Child lookup in `ScriptVar` must support symbol-keyed children (separate map from string-keyed)
- [ ] Well-known symbol `Symbol.iterator` — expose as static property; wire into `for...of` / spread iterable path
- [ ] Well-known symbol `Symbol.hasInstance` — wire into `instanceof` operator
- [ ] Well-known symbol `Symbol.toPrimitive` — wire into type coercion path
- [ ] Well-known symbol `Symbol.toStringTag` — wire into `Object.prototype.toString`
- [ ] Tests: uniqueness, registry, typeof, property key, Symbol.iterator on custom object
- [ ] Update `wiki/Language.md` and `wiki/Standard-Library.md`

---

## Phase 6 — `WeakMap`, `WeakSet`, `WeakRef` (ES2015/2021)

Pure provider work; no VM changes.

- [ ] `WeakMap` — `new WeakMap()`, `.get(k)`, `.set(k,v)`, `.has(k)`, `.delete(k)`
- [ ] `WeakSet` — `new WeakSet()`, `.add(o)`, `.has(o)`, `.delete(o)`
- [ ] `WeakRef` — `new WeakRef(target)`, `.deref()` (always returns live object — GC-less engine)
- [ ] Tests: basic CRUD for each; `WeakRef.deref()` returns the object
- [ ] Update `wiki/Standard-Library.md`

---

## Phase 7 — Static class initialisation blocks (ES2022)

Compiler-only change; no new opcodes needed.

- [ ] In class-body compiler, recognise `static {` (not followed by an identifier or `(`)
- [ ] Compile the block body and emit it after the class object is constructed
- [ ] Tests: static block runs once; can reference static fields; runs before first instance creation
- [ ] Update `wiki/Language.md`

---

## Phase 8 — Private class fields and methods (ES2022)

Lexer + compiler + runtime change.

- [ ] Add `LexTypes.PrivateName` — lex `#identifier` as a single token
- [ ] In class-body compiler, treat `#name` declarations as private slots stored separately from public properties
- [ ] Private instance fields: initialised per-instance in the constructor
- [ ] Private instance methods: stored on the class, accessible via `this.#method()`
- [ ] Private static fields and methods: `static #x`
- [ ] `#name in obj` existence check (ES2022) — new `in` branch in the compiler
- [ ] Accessing `obj.#field` outside the class body throws `SyntaxError` at compile time
- [ ] Tests: read/write private field, private method call, static private, out-of-class access throws, `#name in obj`
- [ ] Update `wiki/Language.md`

---

## Phase 9 — `import.meta` (ES2020)

Small compiler + VM change.

- [ ] Add `LexTypes.ImportMeta` or handle `import.meta` as a special case in the expression compiler
- [ ] Emit a `PushImportMeta` opcode (or reuse existing machinery)
- [ ] VM resolves `import.meta` to an object with `url`, `filename`, `dirname` populated from the current module context
- [ ] Tests: `import.meta.url` contains the module path; works inside a `require`d module
- [ ] Update `wiki/Modules.md`

---

## Phase 10 — Dynamic `import()` (ES2020)

New opcode + Promise integration.

- [ ] Parser: when `import` appears in expression position followed by `(`, parse as a call expression (not a declaration)
- [ ] Compiler: emit `DynamicImport` opcode with the specifier expression
- [ ] VM: handle `DynamicImport` — resolve via `ModuleLoader`, compile and cache, return `Promise<exports>`
- [ ] Tests: `await import("./mod")` resolves with the module exports; missing module rejects the Promise
- [ ] Update `wiki/Modules.md`

---

## Phase 11 — Top-level `await` (ES2022)

Compiler change; the VM's async machinery already exists.

- [ ] Detect `await` at module scope (outside any function body) in the compiler
- [ ] When detected, wrap the entire module body in an implicit async wrapper before compilation
- [ ] Propagate the returned Promise so the host can await module completion
- [ ] Tests: top-level `await` resolves a `Promise`; value is accessible after module load
- [ ] Update `wiki/Language.md` and `wiki/Modules.md`

---

## Phase 12 — BigInt (ES2020)

Significant VM change. Do after Phase 5 (Symbol) since typeof handling follows the same pattern.

- [ ] Add `LexTypes.BigInt` — lex trailing `n` on integer literals (`123n`, `0xFFn`, `0b101n`, `0o77n`)
- [ ] Numeric separators also apply inside BigInt literals (`1_000n`)
- [ ] Add `BigInteger` slot to `ScriptVar` (via `System.Numerics.BigInteger`)
- [ ] `typeof 1n` returns `"bigint"`
- [ ] `BigInt(val)` factory — `new BigInt()` throws `TypeError`
- [ ] Arithmetic opcodes: `+`, `-`, `*`, `/`, `%`, `**` — add BigInt branch; cross-type with Number throws `TypeError`
- [ ] Unary `-`, bitwise `&`, `|`, `^`, `~`, `<<`, `>>`
- [ ] Comparison: `==`, `===`, `<`, `>`, `<=`, `>=`; cross-type `==` coerces; cross-type `===` is always `false`
- [ ] `BigInt.prototype.toString(radix?)`, `.valueOf()`, `.toLocaleString()`
- [ ] `Number(bigint)` explicit conversion
- [ ] Tests: literals, arithmetic, comparisons, typeof, cross-type TypeError, toString radix
- [ ] Update `wiki/Language.md` and `wiki/Standard-Library.md`

---

## Phase 13 — `Proxy` and `Reflect` (ES2015)

Deep VM change. Implement after all other phases are stable.

### Reflect (standalone, no VM changes needed)
- [ ] `Reflect.apply(fn, thisArg, args)`
- [ ] `Reflect.construct(target, args, newTarget?)`
- [ ] `Reflect.get(target, key, receiver?)`
- [ ] `Reflect.set(target, key, val, receiver?)`
- [ ] `Reflect.has(target, key)` — `key in target`
- [ ] `Reflect.deleteProperty(target, key)`
- [ ] `Reflect.ownKeys(target)`
- [ ] `Reflect.defineProperty(target, key, desc)`
- [ ] `Reflect.getOwnPropertyDescriptor(target, key)`
- [ ] `Reflect.getPrototypeOf(target)` / `Reflect.setPrototypeOf(target, proto)`
- [ ] `Reflect.isExtensible(target)` / `Reflect.preventExtensions(target)`

### Proxy (VM-pervasive change)
- [ ] `new Proxy(target, handler)` — constructor
- [ ] Every property-get path in the VM checks for Proxy and calls `handler.get` trap if present
- [ ] Every property-set path checks for Proxy and calls `handler.set` trap
- [ ] `in` operator checks for `handler.has` trap
- [ ] `delete` operator checks for `handler.deleteProperty` trap
- [ ] Function calls check for `handler.apply` trap
- [ ] `new` expression checks for `handler.construct` trap
- [ ] `Object.keys()` / `for...in` checks for `handler.ownKeys` trap
- [ ] Tests: get/set/has/delete traps; revocable proxy (`Proxy.revocable`); transparent forwarding via Reflect
- [ ] Update `wiki/Standard-Library.md` and `wiki/Language.md`

---

## Phase 14 — `Intl` (ES2020+)

Backed entirely by .NET globalization APIs.

- [ ] `Intl.getCanonicalLocales(locales)` — normalise locale tags
- [ ] `new Intl.Collator(locale?, opts?)` — `.compare(a, b)`, `.resolvedOptions()`
- [ ] `new Intl.NumberFormat(locale?, opts?)` — `.format(n)`, `.formatToParts(n)`, `.resolvedOptions()`
- [ ] `new Intl.DateTimeFormat(locale?, opts?)` — `.format(date)`, `.formatToParts(date)`, `.resolvedOptions()`
- [ ] `new Intl.DisplayNames(locale, {type})` — `.of(code)` for language/region/script/currency display names
- [ ] `new Intl.PluralRules(locale?, opts?)` — `.select(n)` → `"one"|"few"|"many"|"other"` etc.
- [ ] Tests: formatting numbers with locale, sorting with collator, date formatting
- [ ] Update `wiki/Standard-Library.md`
