# ECMAScript Compatibility Matrix

Status legend: ✅ Implemented · ⚠️ Partial · ❌ Not implemented

---

## ES5 (2009)

| Feature | Status | Notes |
|---|---|---|
| `var` declarations (hoisted, function-scoped) | ✅ | |
| Named and anonymous functions | ✅ | |
| Closures | ✅ | |
| Prototype-based inheritance | ✅ | |
| `try` / `catch` / `finally` | ✅ | |
| `throw` any value | ✅ | |
| `typeof` | ✅ | |
| `instanceof` | ✅ | Custom `Symbol.hasInstance` trap supported |
| `in` operator | ✅ | |
| `delete` operator | ✅ | |
| `for...in` | ✅ | |
| Comma operator | ✅ | |
| Ternary (`?:`) | ✅ | |
| `void` operator | ✅ | |
| Bitwise operators (`&` `\|` `^` `~` `<<` `>>` `>>>`) | ✅ | |
| `arguments` object | ✅ | Available in all non-arrow functions; `.length`, indexed access, `Array.from(arguments)` all work; arrow functions correctly have no `arguments` binding |
| Getter / setter (`get`/`set`) | ✅ | Object literals, class bodies, and `Object.defineProperty` |
| `Object.create(proto)` | ✅ | |
| `Object.keys(obj)` | ✅ | Respects `enumerable` descriptor |
| `Object.values(obj)` | ✅ | Respects `enumerable` descriptor |
| `Object.entries(obj)` | ✅ | Respects `enumerable` descriptor |
| `Object.assign(target, …src)` | ✅ | |
| `Object.freeze` / `Object.isFrozen` | ✅ | |
| `Object.seal` / `Object.isSealed` | ✅ | |
| `Object.isExtensible` / `Object.preventExtensions` | ✅ | |
| `Object.getPrototypeOf` | ✅ | |
| `Object.setPrototypeOf` | ✅ | |
| `Object.defineProperty` | ✅ | `value`, `writable`, `enumerable`, `configurable`, `get`, `set` |
| `Object.defineProperties` | ✅ | |
| `Object.getOwnPropertyDescriptor` | ✅ | |
| `Object.getOwnPropertyDescriptors` | ✅ | |
| `Array.isArray(val)` | ✅ | |
| `Array.prototype` iteration methods (`forEach`, `map`, `filter`, `reduce`, `every`, `some`, `find`, `findIndex`, `includes`, `indexOf`, `lastIndexOf`) | ✅ | |
| `Array.prototype.slice` / `splice` / `concat` / `join` / `reverse` / `sort` | ✅ | |
| `Array.prototype.flat` / `flatMap` | ✅ | |
| `String.prototype` (`split`, `replace`, `trim`, `substring`, `slice`, `charAt`, `indexOf`, `lastIndexOf`, `startsWith`, `endsWith`, `includes`, `repeat`, `padStart`, `padEnd`, `toUpperCase`, `toLowerCase`, `charCodeAt`) | ✅ | |
| `String.prototype.replaceAll` | ✅ | |
| `String.prototype.matchAll` | ✅ | Requires `g` flag on RegExp argument; returns array with `.index`, `.input`, `.groups` per match |
| `String.prototype.at` | ✅ | |
| `RegExp` literals and `new RegExp` | ✅ | Flags: `g`, `i`, `m`, `s` (dotAll); `u`/`d`/`v`/`y` accepted |
| `RegExp.prototype.test` / `exec` | ✅ | `.groups` populated for named captures |
| Named capture groups in RegExp | ✅ | `(?<name>...)` syntax; `.groups` on exec/match result |
| Lookahead / lookbehind assertions | ✅ | `(?=...)`, `(?!...)`, `(?<=...)`, `(?<!...)` all supported |
| `Math` object (all standard methods and constants) | ✅ | |
| `JSON.parse` / `JSON.stringify` | ✅ | |
| `parseInt` / `parseFloat` | ✅ | |
| `isNaN` / `isFinite` | ✅ | |
| `Number.isNaN` / `Number.isFinite` / `Number.isInteger` | ✅ | |
| `Number.parseInt` / `Number.parseFloat` | ✅ | |
| `Number.EPSILON` / `MAX_SAFE_INTEGER` / `MIN_SAFE_INTEGER` | ✅ | |
| `Date` object | ✅ | Constructor, static `now`/`parse`/`UTC`, all get/set/format methods |
| `Error` (and subclasses `TypeError`, `RangeError`, etc.) | ✅ | Constructable via `new` or call; `message`, `name`, `stack` set; `instanceof Error` works through prototype chain; `Error.cause` (ES2022) not implemented |
| `Function.prototype.bind` / `call` / `apply` | ✅ | |
| Strict mode (`"use strict"`) | ✅ | Directive detected; compile-time errors (dup params, `eval`/`arguments` as binding, `delete <id>`, octals); `this=undefined` in plain calls; `arguments.callee`/`caller` poison pills; undeclared assignment `ReferenceError`; non-writable property write `TypeError`; block-scoped function declarations |
| Eval | ⚠️ | `eval(str)` executes code; indirect eval semantics not guaranteed |

---

## ES2015 (ES6)

| Feature | Status | Notes |
|---|---|---|
| `let` and `const` (block-scoped) | ✅ | |
| Arrow functions | ✅ | Expression and block bodies; lexical `this` |
| Default parameters | ✅ | |
| Rest parameters (`...rest`) | ✅ | |
| Spread in calls (`fn(...arr)`) | ✅ | |
| Spread in array literals (`[...a, ...b]`) | ✅ | |
| Object spread (`{...obj}`) | ✅ | |
| Template literals (tagged and untagged) | ✅ | Nested expressions supported |
| Tagged template literals | ✅ | Tag receives `strings` array with `.raw` property; interpolated values passed as further args |
| Array destructuring | ✅ | Including rest element and defaults |
| Object destructuring | ✅ | Including rename, nested, and defaults |
| Destructuring in parameters | ✅ | |
| Shorthand property names | ✅ | |
| Computed property names | ✅ | |
| Method shorthand in object literals | ✅ | |
| `class` syntax | ✅ | |
| `extends` and `super` | ✅ | |
| Static methods and properties | ✅ | |
| Static initialisation blocks | ✅ | |
| Private fields and methods (`#name`) | ✅ | Stored as string key `"#name"` |
| `for...of` | ✅ | Arrays, generators, custom iterables |
| `Symbol` | ✅ | `Symbol()`, `Symbol.for`, `Symbol.keyFor`, `Symbol.iterator`, `Symbol.asyncIterator`, `Symbol.hasInstance`, `Symbol.toPrimitive`, `Symbol.toStringTag` |
| Custom iterables (`[Symbol.iterator]()`) | ✅ | |
| Generators (`function*`, `yield`) | ✅ | Including `yield*` delegation |
| `Map` | ✅ | |
| `Set` | ✅ | |
| `WeakMap` | ✅ | `set`/`get`/`has`/`delete`; primitive keys throw `TypeError` |
| `WeakSet` | ✅ | `add`/`has`/`delete`; primitive values throw `TypeError` |
| `Proxy` | ✅ | get, set, has, deleteProperty, apply, construct, ownKeys traps |
| `Proxy.revocable` | ✅ | |
| `Reflect` | ✅ | apply, construct, get, set, has, deleteProperty, ownKeys, defineProperty, getOwnPropertyDescriptor, getPrototypeOf, setPrototypeOf, isExtensible, preventExtensions |
| `Promise` constructor | ✅ | |
| `Promise.prototype.then` / `catch` | ✅ | |
| `Promise.resolve` / `Promise.reject` | ✅ | |
| `Promise.all` | ✅ | |
| `Promise.race` | ✅ | |
| `Array.from` | ✅ | |
| `Array.of` | ✅ | |
| `Array.prototype.fill` | ✅ | |
| `Array.prototype.keys` / `values` / `entries` | ✅ | |
| `Object.assign` | ✅ | |
| `Object.is` | ✅ | |
| `String.raw` | ✅ | Works as a template tag; raw strings preserve escape sequences |
| `String.fromCharCode` / `fromCodePoint` | ✅ | |
| `Number.EPSILON` etc. | ✅ | |
| Binary (`0b`) and octal (`0o`) literals | ✅ | |
| `import` / `export` (static ES modules) | ✅ | Named, namespace (`* as`), default, and re-export forms |

---

## ES2016

| Feature | Status | Notes |
|---|---|---|
| Exponentiation operator (`**`) | ❌ | Lexer and compiler do not implement `**`; use `Math.pow(a, b)` |
| `Array.prototype.includes` | ✅ | |

---

## ES2017

| Feature | Status | Notes |
|---|---|---|
| `async` / `await` | ✅ | Async functions, async arrow functions |
| `Object.entries` | ✅ | |
| `Object.values` | ✅ | |
| `Object.getOwnPropertyDescriptors` | ✅ | |
| `String.prototype.padStart` / `padEnd` | ✅ | |
| Trailing commas in function parameter lists | ✅ | |
| Shared memory and atomics (`SharedArrayBuffer`, `Atomics`) | ❌ | Out of scope — requires multi-threading and typed arrays; see notes |

---

## ES2018

| Feature | Status | Notes |
|---|---|---|
| `Promise.prototype.finally` | ✅ | |
| Async generators (`async function*`) | ✅ | `.next()` returns a Promise resolving to `{value, done}`; `[Symbol.asyncIterator]` returns `this` |
| `for await...of` | ✅ | Requires async context; falls back to `Symbol.iterator` / array iteration for sync iterables |
| Rest/spread in object literals | ✅ | |
| Named capture groups in RegExp | ✅ | `(?<name>...)` syntax; `.groups` on exec/match result |
| `s` (dotAll) RegExp flag | ✅ | `dotAll` property exposed on RegExp instances |
| Unicode property escapes in RegExp (`\p{...}`) | ✅ | `u`/`v` flag accepted; `\p{Category}`, `\p{Script=X}`, and two-letter Unicode category codes translated to .NET equivalents |
| Lookbehind assertions in RegExp | ✅ | `(?<=...)` and `(?<!...)` both supported |

---

## ES2019

| Feature | Status | Notes |
|---|---|---|
| `Array.prototype.flat` | ✅ | |
| `Array.prototype.flatMap` | ✅ | |
| `Object.fromEntries` | ✅ | |
| `String.prototype.trimStart` / `trimEnd` | ✅ | |
| Optional `catch` binding (`catch { }`) | ✅ | |
| `Symbol.prototype.description` | ✅ | |
| `Array.prototype.sort` stability | ✅ | Backed by .NET's stable sort |
| `Function.prototype.toString` | ✅ | Compiled functions return source; native functions return `function name() { [native code] }` |
| Well-formed `JSON.stringify` | ✅ | |

---

## ES2020

| Feature | Status | Notes |
|---|---|---|
| `BigInt` literals (`42n`) | ✅ | Decimal, hex, binary, and octal forms; numeric separators |
| `BigInt()` factory | ✅ | |
| BigInt arithmetic and comparisons | ✅ | `+` `-` `*` `/` `%` `&` `\|` `^` `~` `-` (unary) `<` `>` `<=` `>=` `==` `!=` |
| BigInt mixed-type TypeError | ✅ | |
| `globalThis` | ✅ | Refers to the engine root; no circular reference; `globalThis === globalThis` is stable |
| Optional chaining (`?.`) | ✅ | Member access, index access, and function calls |
| Nullish coalescing (`??`) | ✅ | |
| `Promise.allSettled` | ✅ | |
| `Promise.any` | ✅ | Rejects with `AggregateError` |
| `AggregateError` | ✅ | |
| `String.prototype.matchAll` | ✅ | Requires `g` flag on RegExp argument; returns array with `.index`, `.input`, `.groups` per match |
| Dynamic `import()` | ✅ | Returns a Promise; module caching applies |
| `import.meta` | ✅ | `.url`, `.filename`, `.dirname` |
| `for...in` ordering guarantee | ✅ | Property insertion order is preserved |
| Nullish coalescing assignment (`??=`) | ✅ | |

---

## ES2021

| Feature | Status | Notes |
|---|---|---|
| Logical assignment operators (`&&=`, `\|\|=`, `??=`) | ✅ | Including property and index targets |
| Numeric separators (`1_000`) | ✅ | Integer, float, hex, binary, octal, and BigInt literals |
| `Promise.any` | ✅ | |
| `String.prototype.replaceAll` | ✅ | |
| `WeakRef` | ✅ | `deref()` returns the target object |
| `FinalizationRegistry` | ❌ | Will not be implemented — requires a tracing GC; see notes |

---

## ES2022

| Feature | Status | Notes |
|---|---|---|
| Class private instance fields (`#x`) | ✅ | |
| Class private instance methods (`#fn()`) | ✅ | |
| Class private static fields and methods | ✅ | |
| Static class initialisation blocks (`static { }`) | ✅ | |
| Top-level `await` | ✅ | Auto-detected; variables remain at global scope |
| `Array.prototype.at` | ✅ | |
| `String.prototype.at` | ✅ | |
| `Object.hasOwn(obj, key)` | ✅ | |
| `Error.cause` | ✅ | `new Error('msg', { cause: err })` — `cause` stored on the error object |
| `RegExp` `d` (indices) flag | ✅ | `exec()` and `match()` populate `.indices[i]=[start,end]` and `.indices.groups` when `d` flag is set |
| `#x in obj` (ergonomic brand checks) | ❌ | Will not be implemented for now — requires the parser to distinguish `#ident in expr` from string-keyed `in`, the compiler to resolve the private-field mangled name from the enclosing class scope, and a new `in` opcode variant; complexity outweighs current value |

---

## ES2023

| Feature | Status | Notes |
|---|---|---|
| `Array.prototype.findLast` / `findLastIndex` | ✅ | |
| `Array.prototype.toReversed` / `toSorted` / `toSpliced` / `with` | ✅ | |
| `Array.from` with `{ from }` static | ✅ | Basic form only |
| Hashbang (`#!`) in scripts | ✅ | |
| `Symbol.prototype.description` (read-only) | ✅ | |

---

## ES2024

| Feature | Status | Notes |
|---|---|---|
| `Promise.withResolvers` | ❌ | |
| `Object.groupBy` / `Map.groupBy` | ✅ | |
| `ArrayBuffer.prototype.resize` | ❌ | Out of scope — requires typed array / ArrayBuffer support |
| `Atomics.waitAsync` | ❌ | Out of scope — requires multi-threading infrastructure; see ES2017 notes |
| RegExp `v` flag and set notation | ❌ | |
| `String.prototype.isWellFormed` / `toWellFormed` | ❌ | |

---

## ES2025

| Feature | Status | Notes |
|---|---|---|
| Iterator helpers (`Iterator.prototype.map`, `filter`, `take`, etc.) | ❌ | |
| `Set` methods (`union`, `intersection`, `difference`, etc.) | ❌ | |
| `RegExp.escape` | ❌ | |
| `Promise.try` | ❌ | |
| Import attributes (`import … with { type: 'json' }`) | ❌ | |
| `Float16Array` | ❌ | Out of scope |

---

## Non-standard built-ins (DScript-specific)

| Feature | Notes |
|---|---|
| `require(path)` / CommonJS `module.exports` | CommonJS-style module system |
| `console.log` / `.warn` / `.error` | |
| `trace(val)` | Debug dump of any value |
| `exec(str)` | Alias of `eval` for statements |
| `charToInt(ch)` | Unicode code point of first character |
| `structuredClone(val)` | Deep clone |
| `queueMicrotask(fn)` | Enqueue on the microtask queue |
| `btoa` / `atob` | Base-64 encode / decode |
| `encodeURIComponent` / `decodeURIComponent` / `encodeURI` / `decodeURI` | |
| `Intl.Collator` / `NumberFormat` / `DateTimeFormat` / `DisplayNames` / `PluralRules` | Backed by .NET globalization |

---

## Module system

| Feature | Status | Notes |
|---|---|---|
| `require(path)` (CommonJS) | ✅ | |
| `module.exports` / `exports` | ✅ | |
| `__filename` / `__dirname` | ✅ | |
| `import … from` (named) | ✅ | |
| `import * as ns from` (namespace) | ✅ | |
| `import defaultExport from` | ✅ | |
| `export` / `export default` | ✅ | |
| `export { x } from` (re-export) | ✅ | |
| `import()` (dynamic) | ✅ | Returns Promise |
| `import.meta` | ✅ | `.url`, `.filename`, `.dirname` |
| Module caching | ✅ | Each path compiled and executed once |
| Circular dependency handling | ✅ | Partial exports returned to break cycle |
| Top-level `await` in modules | ✅ | |

---

## Known limitations and out-of-scope features

- **Regular expression `v` flag sticky semantics**: The `v` flag is accepted and triggers Unicode property escape translation but does not implement the full set-notation difference from `u` (e.g. `[A--Z]` syntax is not supported).
- **Typed arrays** (`Uint8Array`, `Int32Array`, `Float64Array`, etc.): Not implemented.
- **ArrayBuffer** / **SharedArrayBuffer** / **Atomics**: Will not be implemented. `SharedArrayBuffer` requires multiple concurrently-executing VM instances sharing an address space, and `Atomics` only operates through typed array views. DScript is a single-threaded embedded engine with no Worker/thread model, so there is nothing to synchronise across. The prerequisites (typed arrays, thread-safe `ScriptVar` and scope chain, `Atomics.wait` blocking without deadlocking the host) make this impractical without a fundamental redesign of the engine.
- **Async generators — `await` inside body**: `await` inside an `async function*` body is compiled as `yield` and driven as a plain yield, not as a true awaited Promise. Code that `yield`s values works correctly; code that `await`s Promises inside the body may not produce the expected interleaving.
- **`FinalizationRegistry`**: Will not be implemented. `FinalizationRegistry` requires the engine to fire a callback at the moment a registered object becomes unreachable. DScript uses explicit reference counting (`ScriptVar` carries `AddRef`/`Release` and suppresses the .NET finalizer via `GC.SuppressFinalize`), so object lifetimes are deterministic and there is no "object was just collected" hook point. Implementing it correctly would require replacing the ref-count model with a tracing (mark-and-sweep or generational) garbage collector over the entire `ScriptVar` graph — a complete redesign of memory management that is out of scope.
