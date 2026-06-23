# DScript — ES5 / ES2015 Compatibility Tasks

Derived from `plan.md`. Each phase must pass the full test suite and 90% coverage gate before being committed. Commit message = phase name.

Status: `[ ]` todo · `[~]` in progress · `[x]` done

---

## Phase 1 — Property descriptors and the Object API (ES5)

**Prerequisite for all other phases.** Adds getter/setter support and the property descriptor model to `ScriptVarLink`.

### ScriptVar / ScriptVarLink changes
- [ ] Add `Getter` and `Setter` (`ScriptVar`) slots to `ScriptVarLink`
- [ ] Add `IsFrozen`, `IsSealed`, `IsExtensible` flags to `ScriptVar`
- [ ] Enforce frozen/sealed in `SetVar` / `AddChild` (silently ignore writes in sloppy mode)
- [ ] Enforce non-extensible in `AddChild` (throw `TypeError`)

### Lexer / compiler changes
- [ ] Parse `get ident() { }` in object literals — emit `DefineGetter` opcode (append to enum)
- [ ] Parse `set ident(v) { }` in object literals — emit `DefineSetter` opcode (append to enum)
- [ ] Parse `get` / `set` accessor methods in class bodies
- [ ] Handle `get` / `set` as contextual keywords (they are valid identifiers too)

### VM changes
- [ ] Handle `DefineGetter` opcode — store getter on the named child link
- [ ] Handle `DefineSetter` opcode — store setter on the named child link
- [ ] Invoke getter in `GetMember` when the link has a `Getter`
- [ ] Invoke setter in `SetMember` when the link has a `Setter`

### Object API (new `ObjectRegistrar.cs` or extend `ObjectExtras.cs`)
- [ ] `Object.defineProperty(obj, key, descriptor)` — honour `value`, `writable`, `enumerable`, `configurable`, `get`, `set`
- [ ] `Object.defineProperties(obj, descriptors)`
- [ ] `Object.getOwnPropertyDescriptor(obj, key)` — return `{value, writable, enumerable, configurable}` or accessor descriptor
- [ ] `Object.getOwnPropertyDescriptors(obj)`
- [ ] `Object.getOwnPropertyNames(obj)` — all own string-keyed property names regardless of enumerability
- [ ] `Object.getPrototypeOf(obj)` — forward to `Reflect.getPrototypeOf`
- [ ] `Object.setPrototypeOf(obj, proto)` — forward to `Reflect.setPrototypeOf`
- [ ] `Object.create(proto)` / `Object.create(proto, descriptors)`
- [ ] `Object.freeze(obj)` / `Object.isFrozen(obj)`
- [ ] `Object.seal(obj)` / `Object.isSealed(obj)`
- [ ] `Object.isExtensible(obj)` / `Object.preventExtensions(obj)`
- [ ] Register all methods in `ScriptEngine` constructor

### Tests (`DScript.Test/PropertyDescriptorTests.cs`)
- [ ] Getter is invoked on property read
- [ ] Setter is invoked on property write
- [ ] Getter-only property ignores writes (sloppy mode)
- [ ] `Object.freeze` prevents property mutation
- [ ] `Object.seal` prevents adding new properties but allows value writes
- [ ] `Object.create(proto)` — child inherits proto methods
- [ ] `Object.getOwnPropertyDescriptor` returns correct descriptor shape
- [ ] `Object.defineProperty` — add non-enumerable property; it does not appear in `for...in`
- [ ] `Object.preventExtensions` — adding new property throws or is silently ignored

### Docs
- [ ] `ES-COMPATIBILITY.md` — flip getter/setter, `Object.create`, `Object.freeze/seal`, `Object.defineProperty`, `Object.getPrototypeOf` rows to ✅
- [ ] `wiki/Language.md` — getter/setter syntax section
- [ ] `wiki/Standard-Library.md` — update Object section

---

## Phase 2 — Function.prototype methods (ES5)

- [ ] `Function.prototype.call(thisArg, ...args)` — invoke function with explicit `this`
- [ ] `Function.prototype.apply(thisArg, argsArray)` — invoke with array of arguments
- [ ] `Function.prototype.bind(thisArg, ...partialArgs)` — return new native callable with fixed `this` and pre-filled args
- [ ] `bind` result has correct `.length` (original length minus partial arg count, min 0)
- [ ] `bind` result has correct `.name` (`"bound " + originalName`)
- [ ] Tests (`DScript.Test/FunctionMethodTests.cs`):
  - [ ] `call` with explicit `this`
  - [ ] `apply` with array args
  - [ ] `bind` basic invocation
  - [ ] `bind` with partial args
  - [ ] `bind` result `.length` and `.name`
  - [ ] Chained `bind` (second `bind` does not override first `this`)
- [ ] `ES-COMPATIBILITY.md` — flip `Function.prototype.bind/call/apply` to ✅
- [ ] `wiki/Standard-Library.md` — add Function.prototype section

---

## Phase 3 — Date object (ES5)

New `DScript/DateRegistrar.cs`. Store timestamp as `double` (Unix ms) in `scriptData`.

### Constructors and static methods
- [ ] `Date()` (no `new`) — returns current date as a string
- [ ] `new Date()` — current timestamp
- [ ] `new Date(ms)` — from Unix milliseconds
- [ ] `new Date(str)` — parse ISO-8601 string
- [ ] `new Date(year, month[, day, h, m, s, ms])` — local time components
- [ ] `Date.now()` — current Unix ms via `DateTimeOffset.UtcNow`
- [ ] `Date.parse(str)` — return Unix ms or `NaN` on failure
- [ ] `Date.UTC(year, month, ...)` — UTC milliseconds

### Prototype getters
- [ ] `getTime()`, `valueOf()`
- [ ] `getFullYear()`, `getMonth()`, `getDate()`, `getDay()`
- [ ] `getHours()`, `getMinutes()`, `getSeconds()`, `getMilliseconds()`
- [ ] `getTimezoneOffset()`
- [ ] UTC variants: `getUTCFullYear()` … `getUTCMilliseconds()`

### Prototype setters
- [ ] `setTime(ms)`
- [ ] `setFullYear(y[, m, d])`, `setMonth(m[, d])`, `setDate(d)`
- [ ] `setHours(h[, m, s, ms])`, `setMinutes(m[, s, ms])`, `setSeconds(s[, ms])`, `setMilliseconds(ms)`
- [ ] UTC variants: `setUTCFullYear()` … `setUTCMilliseconds()`

### Prototype formatters
- [ ] `toISOString()` — `"YYYY-MM-DDTHH:mm:ss.sssZ"`
- [ ] `toUTCString()`, `toDateString()`, `toTimeString()`, `toString()`
- [ ] `toLocaleDateString()`, `toLocaleTimeString()`, `toLocaleString()`
- [ ] `toJSON()` — same as `toISOString()`

### Registration and tests
- [ ] Register `Date` in `ScriptEngine` constructor
- [ ] Tests (`DScript.Test/DateTests.cs`):
  - [ ] `typeof Date.now() === 'number'`
  - [ ] `new Date(0).toISOString() === '1970-01-01T00:00:00.000Z'`
  - [ ] `new Date(2024, 0, 15).getFullYear() === 2024`
  - [ ] `Date.parse('2024-01-01T00:00:00.000Z') === 1704067200000`
  - [ ] Round-trip: set then get year/month/date
  - [ ] `new Date('invalid').getTime()` is `NaN`
- [ ] `ES-COMPATIBILITY.md` — flip `Date` to ✅
- [ ] `wiki/Standard-Library.md` — add Date section

---

## Phase 4 — RegExp enhancements (ES5 / ES2018 / ES2020)

**Root cause fix:** replace `RegexOptions.ECMAScript` with `RegexOptions.None` + explicit per-flag options.

### RegExp compilation and flags
- [ ] Switch from `RegexOptions.ECMAScript` to `RegexOptions.None` in RegExp construction
- [ ] Map `i` → `RegexOptions.IgnoreCase`, `m` → `RegexOptions.Multiline`, `s` → `RegexOptions.Singleline`
- [ ] `u` flag — accepted and stored; no additional .NET option needed for basic support
- [ ] `g` flag — already handled; verify it still works after the options switch
- [ ] Expose `RegExp.prototype.flags` — sorted string of active flags (e.g. `"gim"`)
- [ ] Expose `RegExp.prototype.source` — the raw pattern string

### Named capture groups
- [ ] `(?<name>...)` syntax compiles without error
- [ ] `exec` / `match` result has a `.groups` object with named group values
- [ ] Unnamed groups still work alongside named groups

### Lookahead and lookbehind
- [ ] `(?=...)` positive lookahead works
- [ ] `(?!...)` negative lookahead works
- [ ] `(?<=...)` positive lookbehind works
- [ ] `(?<!...)` negative lookbehind works

### String.prototype.matchAll
- [ ] `str.matchAll(regexp)` — requires `g` flag; throws `TypeError` without it
- [ ] Returns an iterator of match objects (each with `.index`, `.input`, `.groups`)
- [ ] `[...str.matchAll(/ab/g)]` produces one entry per match

### Tests (`DScript.Test/RegExpTests.cs`)
- [ ] Named capture group — `groups.year` returns correct value
- [ ] `matchAll` length matches expected number of matches
- [ ] Positive/negative lookahead
- [ ] Positive/negative lookbehind
- [ ] `s` flag — `.` matches `\n`
- [ ] `.flags` property returns sorted flag string
- [ ] `.source` property returns the original pattern
- [ ] `matchAll` without `g` flag throws `TypeError`

### Docs
- [ ] `ES-COMPATIBILITY.md` — flip named captures, lookahead/lookbehind, `matchAll`, `s`/`u` flags to ✅
- [ ] `wiki/Language.md` — update RegExp section

---

## Phase 5 — WeakMap and WeakSet (ES2015)

New `DScript/WeakCollectionRegistrar.cs`. Back with `ConditionalWeakTable<ScriptVar, …>`.

- [ ] `new WeakMap()` constructor
- [ ] `WeakMap.prototype.set(key, value)` — key must be object; throws `TypeError` for primitives
- [ ] `WeakMap.prototype.get(key)` — returns value or `undefined`
- [ ] `WeakMap.prototype.has(key)` — returns boolean
- [ ] `WeakMap.prototype.delete(key)` — returns boolean
- [ ] `new WeakSet()` constructor
- [ ] `WeakSet.prototype.add(value)` — value must be object; throws `TypeError` for primitives
- [ ] `WeakSet.prototype.has(value)` — returns boolean
- [ ] `WeakSet.prototype.delete(value)` — returns boolean
- [ ] Register both in `ScriptEngine` constructor
- [ ] Tests (`DScript.Test/WeakCollectionTests.cs`):
  - [ ] WeakMap CRUD — set/get/has/delete round-trip
  - [ ] WeakMap `has` returns false after `delete`
  - [ ] WeakMap primitive key throws `TypeError`
  - [ ] WeakSet add/has/delete round-trip
  - [ ] WeakSet primitive value throws `TypeError`
  - [ ] Two different object keys are independent entries
- [ ] `ES-COMPATIBILITY.md` — flip `WeakMap` and `WeakSet` to ✅
- [ ] `wiki/Standard-Library.md` — add WeakMap/WeakSet section

---

## Phase 6 — Tagged template literals and String.raw (ES2015)

### Lexer
- [ ] During template literal lexing, capture raw (escape-preserved) text alongside cooked text for each segment

### Compiler (`Compiler.Expressions.cs`)
- [ ] Detect tagged template: `expression` immediately before a template literal with no whitespace
- [ ] Build a frozen array of cooked string segments
- [ ] Build a parallel `.raw` array of raw string segments and attach it to the cooked array
- [ ] Emit new `TaggedTemplate` opcode (append to enum) carrying: tag expression, strings-array, interpolated values

### VM
- [ ] Handle `TaggedTemplate` opcode — pop tag + strings-array + N values, invoke tag as `tag(strings, v0, v1, …)`

### String.raw
- [ ] `String.raw(strings, ...subs)` — read `strings.raw`, interleave `subs`, return joined string
- [ ] Register as static method on `String`

### Tests (`DScript.Test/TemplateLiteralTests.cs`)
- [ ] Tag function receives correct number of string segments (interpolations + 1)
- [ ] `strings[0]` is the cooked first segment (escape sequences processed)
- [ ] `strings.raw[0]` is the raw first segment (escape sequences literal)
- [ ] `String.raw` returns unescaped content
- [ ] `String.raw` with substitutions interleaves correctly
- [ ] Untagged template literals still work unchanged

### Docs
- [ ] `ES-COMPATIBILITY.md` — flip tagged template literals to ✅; add `String.raw` ✅
- [ ] `wiki/Language.md` — update template literal section
- [ ] `wiki/Standard-Library.md` — add `String.raw`

---

## Phase 7 — arguments object (ES5)

**Investigation first:** run the acceptance tests below before any VM changes to find which fail.

- [ ] Write `DScript.Test/ArgumentsTests.cs` with all cases below and run — record which pass/fail
- [ ] Fix: `arguments` materialised on every non-arrow `Call` with correct `.length`
- [ ] Fix: `arguments[i]` correct for all positions including extras beyond declared parameter count
- [ ] Fix: `Array.from(arguments)` works (iterable or array-like with `.length`)
- [ ] Fix: `[...arguments]` spread works
- [ ] Verify: arrow functions still have no `arguments` binding (must stay `undefined` or `ReferenceError`)
- [ ] Tests:
  - [ ] `sum(1, 2, 3) === 6` via `arguments` loop
  - [ ] `sum()` (no args) returns 0
  - [ ] Extra arg beyond declared params accessible at `arguments[2]`
  - [ ] `Array.from(arguments)[0]` returns first argument
  - [ ] Arrow function: `typeof arguments === 'undefined'`
- [ ] `ES-COMPATIBILITY.md` — update `arguments` row to ✅
- [ ] `wiki/Language.md` — note `arguments` availability

---

## Phase 8 — Error improvements (ES5)

- [ ] In `VirtualMachine.cs` `Throw` opcode: capture current call-frame stack; build a stack string `"ErrorType: message\n  at <script>:line"` and set it as `.stack` on the thrown error object
- [ ] Verify prototype chain: `TypeError.prototype.__proto__ === Error.prototype`
- [ ] Verify `new TypeError('msg') instanceof Error === true`
- [ ] Verify `new TypeError('msg') instanceof TypeError === true`
- [ ] Same chain check for `RangeError`, `SyntaxError`, `ReferenceError`
- [ ] Tests (`DScript.Test/ErrorTests.cs`):
  - [ ] `e instanceof TypeError` is true
  - [ ] `e instanceof Error` is true
  - [ ] `typeof e.stack === 'string'`
  - [ ] `e.stack` includes the error type name
  - [ ] `e.message` is preserved
  - [ ] `e.name` is `"TypeError"` etc.
- [ ] `ES-COMPATIBILITY.md` — update Error row to ✅
- [ ] `wiki/Standard-Library.md` — update Error section

---

## Phase 9 — globalThis (ES2020)

- [ ] In the VM's variable-lookup path, intercept `"globalThis"` and push `engine.Root` directly — **do not** call `root.AddChild("globalThis", root)` (would create a circular reference)
- [ ] Tests (`DScript.Test/GlobalThisTests.cs` — file may already exist):
  - [ ] `typeof globalThis === 'object'`
  - [ ] `globalThis.Math === Math`
  - [ ] Set `var x = 42`; then `globalThis.x === 42`
  - [ ] `globalThis === globalThis` (identity stable)
- [ ] `ES-COMPATIBILITY.md` — flip `globalThis` to ✅
- [ ] `wiki/Language.md` — add `globalThis` note

---

## Phase 10 — Documentation update

Run after all previous phases are committed and green.

- [ ] `ES-COMPATIBILITY.md` — audit every row changed by Phases 1–9; confirm all are ✅
- [ ] `wiki/Language.md` — getter/setter syntax; `arguments`; tagged template literals; `globalThis`
- [ ] `wiki/Standard-Library.md` — `Date`; `WeakMap`/`WeakSet`; `Function.prototype` (`call`/`apply`/`bind`); `String.raw`; updated `Object`, `RegExp`, `String` sections; `Error.stack`
- [ ] `wiki/Engine.md` — update if `ScriptEngine` constructor or public API changed
- [ ] Commit wiki submodule; record updated pointer in main repo
