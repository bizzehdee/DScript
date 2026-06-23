# DScript — ES2020+ Conformance Plan

Phases 1–20 (standard library expansion) are complete. This plan covers the remaining language and built-in gaps needed to reach ES2020+ conformance.

Features are grouped by implementation complexity. Each phase is independently shippable.

---

## Phase 1 — Promise completeness (ES2018–2021)

The base `Promise` type exists but all static factory methods and `.finally()` are missing. These are the most-blocked missing pieces; virtually every real async codebase uses `Promise.all` or `.finally`.

### Static factory methods
| Method | Behaviour |
|---|---|
| `Promise.resolve(val)` | Returns a promise already resolved with `val` |
| `Promise.reject(reason)` | Returns a promise already rejected with `reason` |
| `Promise.all(arr)` | Resolves when all resolve; rejects on first rejection |
| `Promise.allSettled(arr)` | Always resolves with `[{status, value/reason}]` |
| `Promise.race(arr)` | Settles with the first input to settle |
| `Promise.any(arr)` | Resolves with first fulfillment; rejects with `AggregateError` if all reject |

### Instance method
- `Promise.prototype.finally(fn)` — runs `fn` on both fulfillment and rejection; propagates the original value/reason

**Implementation note**: Find the current Promise registration (likely `PromiseFunctionProvider` or inside the VM micro-task queue). Add static methods as class-level functions on the `Promise` script object. `Promise.any` depends on `AggregateError` already existing.

**Effort:** Medium (1 day).

---

## Phase 2 — Logical assignment operators (ES2021)

Three compound assignment operators are missing from the lexer. Only the bitwise equivalents (`&=`, `|=`, `^=`) exist.

| Operator | Semantics |
|---|---|
| `a &&= b` | `a && (a = b)` — assign only if `a` is truthy |
| `a \|\|= b` | `a \|\| (a = b)` — assign only if `a` is falsy |
| `a ??= b` | `a ?? (a = b)` — assign only if `a` is `null`/`undefined` |

**Implementation note**: Add three new `LexTypes` tokens (`LogicalAndAssign`, `LogicalOrAssign`, `NullCoalesceAssign`) in `ScriptLex.cs`. In `ScriptCompile.cs`, handle them in the assignment expression compiler similarly to `+=`/`-=` but using the existing `&&`/`||`/`??` short-circuit logic.

**Effort:** Small (half a day).

---

## Phase 3 — `globalThis` (ES2020)

`globalThis` is a universal way to refer to the global scope object regardless of context (script, module, worker). DScript has an internal root object but does not expose it as `globalThis`.

**Implementation note**: Register a root-level `globalThis` property (AppearAtRoot) that returns the engine's root `ScriptVar`. This is a one-line native registration.

**Effort:** Trivial (< 1 hour).

---

## Phase 4 — Numeric separators (ES2021)

Underscores in numeric literals (`1_000_000`, `0xFF_FF`, `0b1010_0001`) are purely cosmetic — they are stripped at lex time and do not change the value.

**Implementation note**: In `ScriptLex.cs`, inside the numeric literal lexing path, skip `_` characters that appear between digits. Validate that a separator does not appear at the start, end, or adjacent to the decimal point or exponent marker.

**Effort:** Trivial (< 1 hour).

---

## Phase 5 — `Symbol` (ES2015, required for ES2020+ iterator protocol)

`Symbol` is a prerequisite for a correct `Symbol.iterator` implementation, which in turn enables custom iterables (`for...of` over user-defined objects, spread on custom collections, destructuring assignment). Without it many ES2020+ patterns break silently.

### Core
- `Symbol(description?)` — factory (not a constructor; `new Symbol()` throws)
- `Symbol.for(key)` / `Symbol.keyFor(sym)` — global symbol registry
- `typeof sym === "symbol"`
- Symbols as property keys: `obj[Symbol.iterator]`

### Well-known symbols (minimum set)
| Symbol | Use |
|---|---|
| `Symbol.iterator` | Iterable protocol — `for...of`, spread, destructuring |
| `Symbol.hasInstance` | `instanceof` customisation |
| `Symbol.toPrimitive` | Custom type coercion |
| `Symbol.toStringTag` | `Object.prototype.toString` tag |

**Implementation note**: Add a `SymbolType` internal value type to `ScriptVar` (alongside the existing int/float/string/bool slots). A `Symbol` is an opaque identity — two calls to `Symbol()` always produce distinct values. Symbols must be usable as property keys (the `ScriptVar` child lookup needs to support a symbol-keyed child map).

**Effort:** Large (2–3 days). Core VM change.

---

## Phase 6 — `WeakMap`, `WeakSet`, `WeakRef` (ES2015/ES2021)

### WeakMap
- `new WeakMap()` — key-value map; keys must be objects; keys are held weakly
- `.get(key)`, `.set(key, val)`, `.has(key)`, `.delete(key)`

### WeakSet
- `new WeakSet()` — set of objects; members are held weakly
- `.add(obj)`, `.has(obj)`, `.delete(obj)`

### WeakRef (ES2021)
- `new WeakRef(target)` — holds a weak reference to `target`
- `.deref()` — returns the target if still alive, otherwise `undefined`

**Implementation note**: In a GC-less scripting engine, true GC-pressure weak references are not meaningful. Implement `WeakMap`/`WeakSet` as strong-reference wrappers (same semantics for script code, just no GC benefit). `WeakRef.deref()` always returns the live object. This is acceptable for a scripting engine — the spec permits this.

**Effort:** Small (half a day). No VM changes; pure provider work.

---

## Phase 7 — Private class fields and methods (ES2022)

Private class members (`#field`, `#method()`) are inaccessible outside the class body. The `#` sigil is currently not lexed.

### What needs to change
1. **Lexer** (`ScriptLex.cs`): recognise `#identifier` as a new token type `LexTypes.PrivateName`
2. **Compiler** (`ScriptCompile.cs`): in class body compilation, treat `#name` declarations as private slots; store them in a per-class private slot map, not in the public property list
3. **Runtime**: accessing `this.#field` from inside the class works; accessing it from outside throws a `SyntaxError`

### In-scope
- Private instance fields (`#x`)
- Private instance methods (`#method()`)
- Private static fields and methods (`static #x`)
- `#name in obj` existence check (ES2022)

**Effort:** Large (2–3 days). Requires lexer, compiler, and runtime changes.

---

## Phase 8 — Static class initialisation blocks (ES2022)

A `static { ... }` block inside a class body runs once when the class is created, allowing complex static initialisation that cannot be expressed in a field initialiser.

```js
class Config {
    static timeout;
    static {
        Config.timeout = parseInt(process.env.TIMEOUT ?? "5000");
    }
}
```

**Implementation note**: In the class-body compiler, when a `static` keyword is followed by `{` (not a method or field name), compile the block body and emit it after the class object is constructed. No new opcodes needed.

**Effort:** Small (half a day). Compiler-only change.

---

## Phase 9 — BigInt (ES2020)

`BigInt` provides arbitrary-precision integers. It is a distinct type from `Number`; mixing them in arithmetic throws a `TypeError`.

### Syntax
- Literals: `123n`, `0xFFn`, `0b101n`, `0o77n`
- Numeric separators also apply: `1_000n`

### API
- `BigInt(val)` — constructor/coercion (not `new BigInt()`)
- `typeof 1n === "bigint"`
- Arithmetic: `+`, `-`, `*`, `/`, `%`, `**`, unary `-`, bitwise `&`, `|`, `^`, `~`, `<<`, `>>`
- Comparison: `==`, `===`, `<`, `>`, `<=`, `>=` (cross-type `==` works; cross-type arithmetic throws)
- `.toString(radix?)`, `.valueOf()`
- `Number(bigint)` — explicit conversion (may lose precision)

**Implementation note**: Add a `BigInteger` slot to `ScriptVar` (via `System.Numerics.BigInteger`). Add `LexTypes.BigInt` in the lexer (detect trailing `n` after an integer literal). Arithmetic opcodes need a BigInt branch. This is a substantial VM change.

**Effort:** Large (3–4 days). Lexer + VM arithmetic + type coercion rules.

---

## Phase 10 — Dynamic `import()` (ES2020)

`import(specifier)` is an expression (not a declaration) that loads a module asynchronously and returns a `Promise`.

```js
const mod = await import("./plugin.ds");
mod.run();
```

### What needs to change
1. **Lexer/Parser**: when `import` appears in expression position followed by `(`, treat it as a call expression rather than a declaration
2. **Compiler**: emit a new `DynamicImport` opcode
3. **VM**: handle `DynamicImport` — resolve the module path, load and compile the chunk (via the existing `ModuleLoader` callback), return a `Promise` that resolves with the exports object

**Effort:** Medium (1–2 days). Requires a new opcode and Promise integration.

---

## Phase 11 — Top-level `await` (ES2022)

Currently `await` is only legal inside `async function` bodies. Top-level `await` allows module-level code to pause execution:

```js
const config = await loadConfig();
```

**Implementation note**: The compiler needs to detect when `await` appears in module-scope (not inside a function). At module scope, the entire module body must be treated as an implicit async function. The VM already has the generator/yield machinery that backs `async`/`await`; the compiler change is to wrap module bodies in an implicit async wrapper when top-level `await` is detected.

**Effort:** Medium (1–2 days). Compiler + minor VM change.

---

## Phase 12 — `import.meta` (ES2020)

`import.meta` is an object available inside ES modules exposing host-defined metadata.

```js
console.log(import.meta.url);  // file URL of the current module
```

**Minimum implementation**: expose `import.meta.url` (the `file://` URL of the current module file) and `import.meta.filename` / `import.meta.dirname` (mirrors of `__filename`/`__dirname`).

**Implementation note**: In the compiler, when `import` is followed by `.meta`, emit a `PushImportMeta` opcode. The VM resolves it to the current module's metadata object.

**Effort:** Small (half a day).

---

## Phase 13 — `Proxy` and `Reflect` (ES2015/ES2020)

`Proxy` wraps an object and intercepts all fundamental operations (property get/set, `in`, `delete`, function calls, `new`, etc.) via a handler object of traps.

`Reflect` provides standalone functions mirroring each proxy trap, making it easy to forward operations to the target.

### Proxy traps (minimum useful set)
`get`, `set`, `has`, `deleteProperty`, `apply`, `construct`, `ownKeys`

**Implementation note**: This is the deepest VM change in the list. Every property access, assignment, `in` check, and function call in the VM dispatch loop must check whether the target is a Proxy and invoke the corresponding trap if so. Without pervasive changes, Proxy cannot be fully spec-compliant. A partial implementation covering `get`/`set`/`has` traps would satisfy 90% of real use cases.

**Effort:** Extra large (4–5 days). Affects every property-access path in the VM.

---

## Phase 14 — `Intl` (ES2020+)

The Internationalisation API: locale-aware sorting, formatting, and display.

### Minimum useful subset
| API | .NET backing |
|---|---|
| `Intl.Collator(locale?)` | `StringComparer.Create(CultureInfo, ...)` |
| `Intl.DateTimeFormat(locale?, opts?)` | `CultureInfo` + `DateTime.ToString(format, culture)` |
| `Intl.NumberFormat(locale?, opts?)` | `CultureInfo` + `double.ToString(format, culture)` |
| `Intl.DisplayNames(locale, {type})` | `CultureInfo` display name lookups |
| `Intl.getCanonicalLocales(locales)` | `CultureInfo` normalisation |

**Effort:** Large (2–3 days). Large surface area but all backed by .NET globalization.

---

## Priority summary

| Phase | Feature | ES | Effort | Impact |
|---|---|---|---|---|
| 1 | Promise combinators + `.finally()` | ES2018/2021 | 1 day | Critical — blocks all real async code |
| 2 | Logical assignment (`&&=`, `\|\|=`, `??=`) | ES2021 | Half day | High — common in modern code |
| 3 | `globalThis` | ES2020 | < 1 hour | High — trivial, widely expected |
| 4 | Numeric separators | ES2021 | < 1 hour | High — trivial, parse error without |
| 5 | `Symbol` + well-known symbols | ES2015 | 2–3 days | High — unlocks correct iterable protocol |
| 6 | `WeakMap`, `WeakSet`, `WeakRef` | ES2015/2021 | Half day | Medium — needed for some patterns |
| 7 | Private class fields/methods (`#field`) | ES2022 | 2–3 days | Medium — common in modern class code |
| 8 | Static class initialisation blocks | ES2022 | Half day | Low–medium |
| 9 | BigInt | ES2020 | 3–4 days | Medium — rarely needed in scripting |
| 10 | Dynamic `import()` | ES2020 | 1–2 days | Medium — needed for plugin systems |
| 11 | Top-level `await` | ES2022 | 1–2 days | Medium — expected in module code |
| 12 | `import.meta` | ES2020 | Half day | Low–medium |
| 13 | `Proxy` / `Reflect` | ES2015 | 4–5 days | Low — niche, deep VM impact |
| 14 | `Intl` | ES2020+ | 2–3 days | Low — niche for embedded scripting |
