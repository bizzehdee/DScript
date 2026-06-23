# DScript — ES5 / ES2015 Full Compatibility Plan

Phases 1–14 (ES2020+ standard library) are complete. This plan covers the gaps required to achieve full compatibility with **ES5 (2009)** and **ES2015 (ES6)** as identified in `ES-COMPATIBILITY.md`.

Each phase is independently shippable. The full test suite and 90% coverage gate must pass before committing each phase.

---

## Phase 1 — Property descriptors and the Object API (ES5)

**Gap:** getters/setters, `Object.defineProperty`, `Object.create`, `Object.freeze/seal`, and related methods are all missing because `ScriptVarLink` has no concept of a property descriptor.

**Features:**
- `get prop() {}` / `set prop(v) {}` accessor syntax in object literals and class bodies
- `Object.defineProperty(obj, key, descriptor)` — `value`, `writable`, `enumerable`, `configurable`, `get`, `set`
- `Object.defineProperties(obj, descriptors)`
- `Object.getOwnPropertyDescriptor(obj, key)`
- `Object.getOwnPropertyDescriptors(obj)`
- `Object.getOwnPropertyNames(obj)`
- `Object.getPrototypeOf(obj)` (forward to `Reflect.getPrototypeOf`)
- `Object.setPrototypeOf(obj, proto)` (forward to `Reflect.setPrototypeOf`)
- `Object.create(proto[, descriptors])`
- `Object.freeze(obj)` / `Object.isFrozen(obj)`
- `Object.seal(obj)` / `Object.isSealed(obj)`
- `Object.isExtensible(obj)` / `Object.preventExtensions(obj)`

**Files to change:**
- `DScript/ScriptVar.cs` — add `Getter`/`Setter` `ScriptVar` slots to `ScriptVarLink`; add `IsFrozen`, `IsSealed`, `IsExtensible` flags to `ScriptVar`; enforce flags in `SetVar` / `AddChild`
- `DScript/Compiler/Compiler.*.cs` — parse `get ident() {}` and `set ident(v) {}` in object literals and class bodies; emit new `DefineGetter` / `DefineSetter` opcodes (appended to enum)
- `DScript/Vm/VirtualMachine.cs` — invoke getter/setter in `GetMember` / `SetMember`; enforce frozen/sealed/non-extensible
- `DScript/Extras/ObjectExtras.cs` (or new `ObjectRegistrar.cs`) — add all missing `Object.*` methods listed above
- `DScript.Test/PropertyDescriptorTests.cs` — new test file

**Acceptance criteria:**
```js
var obj = { get x() { return 42; } };
obj.x === 42;

var frozen = Object.freeze({ a: 1 });
frozen.a = 99;   // silently ignored in sloppy mode
frozen.a === 1;

var child = Object.create({ greet() { return 'hi'; } });
child.greet() === 'hi';

var d = Object.getOwnPropertyDescriptor({ n: 5 }, 'n');
d.value === 5 && d.writable === true;
```

---

## Phase 2 — Function.prototype methods (ES5)

**Gap:** `Function.prototype.call`, `.apply`, and `.bind` are absent; `Reflect.apply` exists as a workaround but the spec-standard API is missing.

**Features:**
- `fn.call(thisArg, ...args)`
- `fn.apply(thisArg, argsArray)`
- `fn.bind(thisArg, ...partialArgs)` — returns a bound function with fixed `this` and optional pre-filled arguments

**Files to change:**
- `DScript/Extras/FunctionExtras.cs` (or new `FunctionRegistrar.cs`) — register `call`, `apply`, `bind` on the Function prototype in the engine
- `DScript/Vm/VirtualMachine.cs` — `bind` must return a native callable `ScriptVar` wrapping the original with captured `this` and partial args
- `DScript.Test/FunctionMethodTests.cs` — new test file

**Acceptance criteria:**
```js
function greet(g) { return g + ' ' + this.name; }
greet.call({ name: 'World' }, 'Hello') === 'Hello World';
greet.apply({ name: 'World' }, ['Hi']) === 'Hi World';
var hw = greet.bind({ name: 'World' });
hw('Hey') === 'Hey World';
var hw2 = greet.bind({ name: 'World' }, 'Yo');
hw2() === 'Yo World';
```

---

## Phase 3 — Date object (ES5)

**Gap:** No `Date` object exists at all.

**Features:**
- `Date()` — returns current date string (no `new`)
- `new Date()` — current timestamp
- `new Date(ms)` / `new Date(str)` / `new Date(year, month[, day, h, m, s, ms])`
- `Date.now()` — current Unix milliseconds
- `Date.parse(str)` — parse ISO string to milliseconds
- `Date.UTC(year, month, ...)` — UTC milliseconds
- `Date.prototype` — full `getFullYear`, `getMonth`, `getDate`, `getDay`, `getHours`, `getMinutes`, `getSeconds`, `getMilliseconds`, `getTime`, `getTimezoneOffset`; all UTC variants; all `setFullYear`… setters; `toISOString`, `toUTCString`, `toDateString`, `toTimeString`, `toString`, `valueOf`, `toLocaleDateString`, `toLocaleTimeString`, `toLocaleString`

**Backed by:** `System.DateTimeOffset` / `System.DateTime`.

**Files to change:**
- `DScript/DateRegistrar.cs` — new file; constructor + all prototype and static methods; store timestamp as `double` (Unix ms) in `scriptData`
- `DScript/ScriptEngine.cs` — call `DateRegistrar.Register(this)`
- `DScript.Test/DateTests.cs` — new test file

**Acceptance criteria:**
```js
typeof Date.now() === 'number';
new Date(0).toISOString() === '1970-01-01T00:00:00.000Z';
new Date(2024, 0, 15).getFullYear() === 2024;
Date.parse('2024-01-01T00:00:00.000Z') === 1704067200000;
```

---

## Phase 4 — RegExp enhancements (ES5 / ES2018 / ES2020)

**Gap:** The engine compiles RegExp with `RegexOptions.ECMAScript`, which disables named capture groups, lookbehind, dotAll (`s` flag), and other modern features. `String.prototype.matchAll` is also missing.

**Features:**
- Named capture groups: `(?<name>...)` and `match.groups.name`
- Lookahead: `(?=...)` / `(?!...)`
- Lookbehind: `(?<=...)` / `(?<!...)`
- `s` (dotAll) flag — `.` matches `\n`
- `u` (unicode) flag — enable Unicode category escapes
- `RegExp.prototype.flags` — string of active flags (e.g. `"gi"`)
- `RegExp.prototype.source` — original pattern string
- `String.prototype.matchAll(regexp)` — returns an iterator of all match objects including groups

**Root cause fix:** switch RegExp compilation from `RegexOptions.ECMAScript` to `RegexOptions.None` and apply `IgnoreCase` / `Multiline` / `Singleline` explicitly from the flags string.

**Files to change:**
- `DScript/Extras/RegExpExtras.cs` (wherever RegExp is registered) — change `RegexOptions` strategy; expose `.flags` and `.source`; add `s` and `u` flag handling
- `DScript/Extras/StringExtras.cs` — add `matchAll` as a generator-like iterator over all regex matches
- `DScript.Test/RegExpTests.cs` — new or extended test file

**Acceptance criteria:**
```js
'2024-06'.match(/(?<year>\d{4})-(?<month>\d{2})/).groups.year === '2024';
[...'aababc'.matchAll(/ab/g)].length === 2;
/foo(?=bar)/.test('foobar') === true;
/(?<=foo)bar/.test('foobar') === true;
/a.b/s.test('a\nb') === true;
/ab/gi.flags === 'gi';
```

---

## Phase 5 — WeakMap and WeakSet (ES2015)

**Gap:** Both are absent.

**Features:**
- `new WeakMap()` — `.set(key, val)`, `.get(key)`, `.has(key)`, `.delete(key)`; keys must be objects (throws `TypeError` for primitives)
- `new WeakSet()` — `.add(val)`, `.has(val)`, `.delete(val)`; values must be objects

**Implementation note:** Back `WeakMap` with `System.Runtime.CompilerServices.ConditionalWeakTable<ScriptVar, ScriptVar>` so entries are genuinely released when the key `ScriptVar` is collected. `WeakSet` uses `ConditionalWeakTable<ScriptVar, object>` with a sentinel value.

**Files to change:**
- `DScript/WeakCollectionRegistrar.cs` — new file; both constructors and their prototype methods
- `DScript/ScriptEngine.cs` — call `WeakCollectionRegistrar.Register(this)`
- `DScript.Test/WeakCollectionTests.cs` — new test file

**Acceptance criteria:**
```js
var wm = new WeakMap();
var key = {};
wm.set(key, 42);
wm.has(key) === true;
wm.get(key) === 42;
wm.delete(key);
wm.has(key) === false;

var ws = new WeakSet();
ws.add(key);
ws.has(key) === true;
ws.delete(key);
ws.has(key) === false;

try { wm.set(1, 'x'); } catch(e) { e instanceof TypeError; }  // true
```

---

## Phase 6 — Tagged template literals and String.raw (ES2015)

**Gap:** Tagged templates compile but pass a flat string to the tag function instead of a `strings` array with `.raw`. `String.raw` is absent.

**Features:**
- Tag function receives `(strings, ...values)` where `strings` is a frozen array of cooked segments with a `.raw` property containing the unescaped raw segments
- `String.raw` built-in tag function

**Files to change:**
- `DScript/ScriptLex.cs` — expose raw (escape-preserved) text alongside cooked text during template lexing
- `DScript/Compiler/Compiler.Expressions.cs` (template literal compiler) — when a tag is present, build a frozen cooked-strings array and a parallel `.raw` array; emit a new `TaggedTemplate` opcode (appended to enum) that pops tag + strings-array + N values and invokes the tag
- `DScript/Vm/VirtualMachine.cs` — handle the `TaggedTemplate` opcode
- `DScript/Extras/StringExtras.cs` — add `String.raw` as a native tag function reading `strings.raw` and interleaving subs
- `DScript.Test/TemplateLiteralTests.cs` — new or extended test file

**Acceptance criteria:**
```js
function tag(strings, ...vals) {
    return strings.raw[0] + vals[0] + strings.raw[1];
}
tag`a\n${1}b\n` === 'a\\n1b\\n';

String.raw`Hello\nWorld` === 'Hello\\nWorld';
String.raw`a${1}b${2}c` === 'a1b2c';
```

---

## Phase 7 — arguments object (ES5)

**Gap:** `arguments` exists in basic cases but indexed access and `.length` are unreliable in some call patterns. Arrow functions correctly have no `arguments` (spec-compliant).

**Features:**
- `arguments.length` — actual argument count for every non-arrow function call
- `arguments[i]` — correct value for all positions including extras beyond declared params
- `Array.from(arguments)` and `[...arguments]` work
- `arguments` is `undefined` inside arrow functions (already correct — no change)

**Investigation first:** write the test cases below, run them, identify which fail before touching the VM.

**Files to change:**
- `DScript/Vm/VirtualMachine.cs` — ensure the `arguments` child is materialised on every non-arrow `Call` with correct `.length` and numeric-keyed children for all passed arguments
- `DScript.Test/ArgumentsTests.cs` — new test file

**Acceptance criteria:**
```js
function sum() {
    var t = 0;
    for (var i = 0; i < arguments.length; i++) t += arguments[i];
    return t;
}
sum(1, 2, 3) === 6;
sum() === 0;

function first() { return Array.from(arguments)[0]; }
first(99) === 99;

function extras(a, b) { return arguments[2]; }
extras(1, 2, 3) === 3;

var arrow = () => typeof arguments;
arrow() === 'undefined';
```

---

## Phase 8 — Error improvements (ES5)

**Gap:** Constructable errors exist and `.message` / `.name` are set, but `error.stack` is absent and subclass `instanceof` chains may be incomplete.

**Features:**
- `error.stack` — at minimum `"ErrorType: message\n  at <script>:line"` from the call frame at throw time
- `new TypeError('msg') instanceof Error === true`
- `new TypeError('msg') instanceof TypeError === true`
- `Error.prototype` is the prototype of `TypeError.prototype`, etc. (full chain)

**Files to change:**
- `DScript/Vm/VirtualMachine.cs` — capture the call-frame snapshot when the `Throw` opcode executes; build a simple stack string and attach it to the thrown error object as `.stack`
- `DScript/Extras/ErrorExtras.cs` (or wherever errors are registered) — verify the prototype chain: `TypeError.prototype.__proto__ === Error.prototype`
- `DScript.Test/ErrorTests.cs` — new or extended test file

**Acceptance criteria:**
```js
try { throw new TypeError('bad'); } catch(e) {
    e instanceof TypeError === true;
    e instanceof Error === true;
    typeof e.stack === 'string';
    e.stack.includes('TypeError');
}
```

---

## Phase 9 — globalThis (ES2020, listed under ES5 for completeness)

**Gap:** `globalThis` is not exposed at script level. It was noted as ❌ in the ES2020 section of the matrix.

**Feature:** `globalThis` resolves to the engine's root `ScriptVar`, the same object that is the global scope.

**Files to change:**
- `DScript/ScriptEngine.cs` (or the relevant registrar called at startup) — register `globalThis` at root as a native getter that returns `engine.Root`, guarded against circular child registration per CLAUDE.md safety rules. The canonical implementation: intercept `GetVar "globalThis"` in the VM and push `root` directly — **do not** call `root.AddChild("globalThis", root)`.

**Acceptance criteria:**
```js
typeof globalThis === 'object';
globalThis.Math === Math;
var x = 42;
globalThis.x === 42;
```

---

## Phase 10 — Matrix update and documentation

After all phases pass the full test suite:

1. Update `ES-COMPATIBILITY.md` — flip every addressed ❌ / ⚠️ row to ✅
2. `wiki/Language.md` — getter/setter syntax; `Date`; RegExp named groups and new flags; `arguments` behaviour; tagged template literals
3. `wiki/Standard-Library.md` — `Date`, `WeakMap`, `WeakSet`, `Function.prototype`, updated `String` / `RegExp` / `Object` sections, `globalThis`
4. `wiki/Engine.md` — if any public `ScriptEngine` API changed

---

## Dependency order

```
Phase 1 (property descriptors)  ← must ship first; Phases 2–9 are unblocked after this
  └─ Phase 2 (Function methods)  — benefits from descriptor support for .length/.name
Phase 3 (Date)                   — independent
Phase 4 (RegExp)                 — independent
Phase 5 (WeakMap/WeakSet)        — independent
Phase 6 (tagged templates)
  └─ Phase 7 is already done (String.raw depends on Phase 6)
Phase 7 (arguments)              — independent
Phase 8 (Error stack)            — independent
Phase 9 (globalThis)             — independent (trivial, do any time)
Phase 10 (docs)                  — last
```

Phases 2–9 can proceed in parallel once Phase 1 is merged.

---

## Out of scope for this plan

| Feature | Reason |
|---|---|
| Strict mode enforcement | Silently accepting `"use strict"` is spec-compliant for embedders |
| Indirect `eval` semantics | Low value for an embedded engine |
| `Date` IANA timezone database | .NET `TimeZoneInfo` covers local/UTC; full IANA DB not needed |
| `RegExp` `v` flag | ES2024 feature, not ES5/ES2015 |
| Typed arrays / ArrayBuffer | Separate large feature, not part of ES5/ES2015 core language |
| `WeakRef` / `FinalizationRegistry` | ES2021; out of this plan's scope |

---

## Priority summary

| Phase | Feature | ES | Effort |
|---|---|---|---|
| 1 | Property descriptors + Object API | ES5 | Large (2–3 days) |
| 2 | Function.prototype.call/apply/bind | ES5 | Small (half day) |
| 3 | Date object | ES5 | Large (2–3 days) |
| 4 | RegExp named groups, flags, matchAll | ES5/2018 | Medium (1–2 days) |
| 5 | WeakMap / WeakSet | ES2015 | Small (half day) |
| 6 | Tagged template literals + String.raw | ES2015 | Medium (1–2 days) |
| 7 | arguments object | ES5 | Small (half day) |
| 8 | Error.stack + prototype chain | ES5 | Small (half day) |
| 9 | globalThis | ES2020 | Trivial (< 1 hour) |
| 10 | Documentation | — | Small |
