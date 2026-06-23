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
| `arguments` object | ⚠️ | Not available inside arrow functions; basic function use only |
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
| `Error` (and subclasses `TypeError`, `RangeError`, etc.) | ⚠️ | `Error`, `TypeError`, `RangeError`, `SyntaxError` constructable; `error.message` and `error.name` set; `Error.cause` (ES2022) not implemented; no `stack` trace |
| `Function.prototype.bind` / `call` / `apply` | ✅ | |
| Strict mode (`"use strict"`) | ❌ | Parsed and ignored |
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
| Tagged template literals | ⚠️ | Untagged fully supported; tagged templates (function call on a template) compile but tag function receives a flat string, not a `strings` array with `raw` |
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
| `Symbol` | ✅ | `Symbol()`, `Symbol.for`, `Symbol.keyFor`, `Symbol.iterator`, `Symbol.hasInstance`, `Symbol.toPrimitive`, `Symbol.toStringTag` |
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
| `String.raw` | ❌ | |
| `String.fromCharCode` / `fromCodePoint` | ✅ | |
| `Number.EPSILON` etc. | ✅ | |
| Binary (`0b`) and octal (`0o`) literals | ✅ | |
| `import` / `export` (static ES modules) | ✅ | Named, namespace (`* as`), default, and re-export forms |

---

## ES2016

| Feature | Status | Notes |
|---|---|---|
| Exponentiation operator (`**`) | ✅ | |
| `Array.prototype.includes` | ✅ | |

---

## ES2017

| Feature | Status | Notes |
|---|---|---|
| `async` / `await` | ✅ | Async functions, async arrow functions |
| `Object.entries` | ✅ | |
| `Object.values` | ✅ | |
| `Object.getOwnPropertyDescriptors` | ❌ | |
| `String.prototype.padStart` / `padEnd` | ✅ | |
| Trailing commas in function parameter lists | ✅ | |
| Shared memory and atomics | ❌ | Out of scope |

---

## ES2018

| Feature | Status | Notes |
|---|---|---|
| `Promise.prototype.finally` | ✅ | |
| Async generators (`async function*`) | ❌ | Async and generators work independently; combined is not implemented |
| `for await...of` | ❌ | |
| Rest/spread in object literals | ✅ | |
| Named capture groups in RegExp | ❌ | |
| `s` (dotAll) RegExp flag | ❌ | |
| Unicode property escapes in RegExp (`\p{...}`) | ❌ | |
| Lookbehind assertions in RegExp | ❌ | |

---

## ES2019

| Feature | Status | Notes |
|---|---|---|
| `Array.prototype.flat` | ✅ | |
| `Array.prototype.flatMap` | ✅ | |
| `Object.fromEntries` | ✅ | |
| `String.prototype.trimStart` / `trimEnd` | ✅ | |
| Optional `catch` binding (`catch { }`) | ⚠️ | `catch` without a binding variable compiles but the error value is inaccessible |
| `Symbol.prototype.description` | ⚠️ | `Symbol('desc').description` returns the description string when accessed via `.String`; no dedicated `.description` property exposed |
| `Array.prototype.sort` stability | ✅ | Backed by .NET's stable sort |
| `Function.prototype.toString` | ⚠️ | Returns the source string of compiled functions; native functions return an empty string |
| Well-formed `JSON.stringify` | ✅ | |

---

## ES2020

| Feature | Status | Notes |
|---|---|---|
| `BigInt` literals (`42n`) | ✅ | Decimal, hex, binary, and octal forms; numeric separators |
| `BigInt()` factory | ✅ | |
| BigInt arithmetic and comparisons | ✅ | `+` `-` `*` `/` `%` `&` `\|` `^` `~` `-` (unary) `<` `>` `<=` `>=` `==` `!=` |
| BigInt mixed-type TypeError | ✅ | |
| `globalThis` | ❌ | Global scope accessible via engine root but not exposed as `globalThis` |
| Optional chaining (`?.`) | ✅ | Member access, index access, and function calls |
| Nullish coalescing (`??`) | ✅ | |
| `Promise.allSettled` | ✅ | |
| `Promise.any` | ✅ | Rejects with `AggregateError` |
| `AggregateError` | ✅ | |
| `String.prototype.matchAll` | ❌ | |
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
| `WeakRef` | ❌ | |
| `FinalizationRegistry` | ❌ | No GC hooks available |

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
| `Error.cause` | ❌ | `new Error('msg', { cause: err })` — cause is not stored |
| `RegExp` `d` (indices) flag | ❌ | |
| `#x in obj` (ergonomic brand checks) | ❌ | Private field existence check via `in` not implemented |

---

## ES2023

| Feature | Status | Notes |
|---|---|---|
| `Array.prototype.findLast` / `findLastIndex` | ❌ | |
| `Array.prototype.toReversed` / `toSorted` / `toSpliced` / `with` | ❌ | |
| `Array.from` with `{ from }` static | ✅ | Basic form only |
| Hashbang (`#!`) in scripts | ❌ | |
| `Symbol.prototype.description` (read-only) | ⚠️ | See ES2019 note |
| Change array by copy (`toSorted`, `toSpliced`, etc.) | ❌ | |

---

## ES2024

| Feature | Status | Notes |
|---|---|---|
| `Promise.withResolvers` | ❌ | |
| `Object.groupBy` / `Map.groupBy` | ❌ | |
| `ArrayBuffer.prototype.resize` | ❌ | Out of scope |
| `Atomics.waitAsync` | ❌ | Out of scope |
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

- **Date**: No `Date` object. Use a host-registered callback or pass timestamps as plain numbers.
- **Regular expression advanced features**: Named capture groups, lookahead/lookbehind, Unicode property escapes, and the `d`/`v` flags are not supported. Patterns are compiled with `RegexOptions.ECMAScript` which limits features to ECMAScript 3 semantics.
- **Typed arrays** (`Uint8Array`, `Int32Array`, `Float64Array`, etc.): Not implemented.
- **ArrayBuffer** / **SharedArrayBuffer**: Not implemented.
- **Atomics**: Not implemented.
- **`Function.prototype.bind` / `call` / `apply`**: Implemented in Phase 2.
- **`Object.create`, `Object.defineProperty`, `Object.getOwnPropertyDescriptor`, getter/setter syntax**: Implemented in Phase 1.
- **`WeakRef` / `FinalizationRegistry`**: Not implemented (no GC hooks in the VM).
- **Async generators** (`async function*`) and `for await...of`: Not implemented.
- **`Error.cause`**: Constructable errors exist, but the `cause` option is ignored.
- **`globalThis`**: The global scope is accessible from C# via `engine.Root`, but `globalThis` is not exposed as a script-level variable.
