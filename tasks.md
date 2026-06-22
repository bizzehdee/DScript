# DScript.Extras — API Expansion Task List

Tasks are ordered by effort-to-impact ratio: quick wins first, then medium
changes, then larger new types. Each phase is independent and can be committed
separately. See `plan.md` for full design notes on each item.

Status: `[ ]` todo · `[~]` in progress · `[x]` done

---

## Phase 1 — Trivial fills (< 1 hour total)

### 1a — Math gaps
- [ ] Add `Math.trunc(x)` → `Math.Truncate()`
- [ ] Add `Math.sign(x)` → `Math.Sign()`
- [ ] Add `Math.hypot(...vals)` → `Math.Sqrt(sum of squares)` (variadic via array arg)
- [ ] Add `Math.log2(x)` → `Math.Log2()`
- [ ] Add `Math.log10(x)` → `Math.Log10()`
- [ ] Add `Math.cbrt(x)` → `Math.Cbrt()`
- [ ] Add `Math.clamp(x, lo, hi)` → `Math.Clamp()`
- [ ] Add `Math.fround(x)` → `(double)(float)x`
- [ ] Add `Math.imul(a, b)` → `unchecked((int)a * (int)b)`

### 1b — Trivial globals
- [ ] Add `performance.now()` → `Stopwatch.GetTimestamp()` scaled to ms (new `PerformanceFunctionProvider` class, `AppearAtRoot = false`)
- [ ] Add `structuredClone(val)` → `val.CopyValue()` (root global)
- [ ] Add `queueMicrotask(fn)` → `MicroTaskQueue.Enqueue(fn)` (root global)

---

## Phase 2 — String gaps (half a day)

All additions go in `StringFunctionProvider`. Each follows the existing parameter
pattern: `var.GetParameter("this")` for the receiver.

- [ ] `startsWith(prefix, pos?)` — `str.StartsWith(prefix)` with optional start offset
- [ ] `endsWith(suffix, len?)` — `str.EndsWith(suffix)` with optional length clamp
- [ ] `includes(search, pos?)` — `str.Contains(search)` with optional start offset
- [ ] `repeat(n)` — `string.Concat(Enumerable.Repeat(str, n))`
- [ ] `padStart(len, fill?)` — `str.PadLeft(len, fillChar)`, fill defaults to space
- [ ] `padEnd(len, fill?)` — `str.PadRight(len, fillChar)`, fill defaults to space
- [ ] `slice(start, end?)` — negative-index substring; semantics differ from `substring` (clamps, no swap)
- [ ] `trimStart()` — `str.TrimStart()`
- [ ] `trimEnd()` — `str.TrimEnd()`
- [ ] `replaceAll(what, with)` — `str.Replace(what, with)`
- [ ] `at(index)` — negative-index single-char access; returns `""` for out-of-range
- [ ] `search(regex)` — `Regex.Match(str, pattern).Index`; returns -1 if no match
- [ ] `matchAll(regex)` — `Regex.Matches()`; returns array of match-group arrays

---

## Phase 3 — console gaps (half a day)

All additions go in `ConsoleFunctionProvider`.

- [ ] `console.warn(val)` — `Console.Error.WriteLine("[WARN] " + val)`
- [ ] `console.info(val)` — `Console.WriteLine("[INFO] " + val)`
- [ ] `console.debug(val)` — `Console.WriteLine("[DEBUG] " + val)`
- [ ] `console.assert(cond, msg?)` — writes `[ASSERT] msg` to stderr if `cond` is falsy
- [ ] `console.time(label?)` — stores `Stopwatch.StartNew()` in a static `Dictionary<string, Stopwatch>`
- [ ] `console.timeEnd(label?)` — stops stopwatch, prints `"label: Xms"`
- [ ] `console.count(label?)` — increments and prints `"label: N"` using a static counter dict
- [ ] `console.countReset(label?)` — resets named counter to zero
- [ ] `console.group(label?)` — increments indent level, optionally prints label
- [ ] `console.groupEnd()` — decrements indent level
- [ ] `console.dir(obj)` — calls `obj.Trace(0, null)` to dump structure
- [ ] `console.table(arr)` — prints a column-aligned text table from an array of objects

---

## Phase 4 — Object gaps (half a day)

All additions go in `ObjectFunctionProvider`.

- [ ] `Object.values(obj)` — array of own enumerable values (symmetric with existing `keys`)
- [ ] `Object.entries(obj)` — array of `[key, value]` pairs; excludes `__proto__`
- [ ] `Object.assign(target, ...sources)` — shallow merge sources into target; returns target
  - Variadic: collect extra args via an array parameter or repeated `GetParameter` with index
- [ ] `Object.fromEntries(entries)` — iterate `entries` array, set each `[k, v]` pair on a new object
- [ ] `Object.freeze(obj)` — mark object as read-only (can track via a `__frozen__` flag child for now)
- [ ] `Object.isFrozen(obj)` — check the `__frozen__` flag
- [ ] `Object.create(proto)` — create new object and set `__proto__` to proto
- [ ] `Object.getOwnPropertyNames(obj)` — like `keys` but includes all children

---

## Phase 5 — Number class (half a day)

Create `NumberFunctionProvider` in `DScript.Extras`. Register under `Number.*` and
also expose the root-level globals (`parseInt`, `parseFloat`, `isNaN`, `isFinite`)
as `Number.*` variants to match the JS spec.

- [ ] Add `Number` class scaffold with `[ScriptClass("Number")]`
- [ ] `Number.isInteger(x)` — check `x == Math.Floor(x)` and not NaN/Infinity
- [ ] `Number.isFinite(x)` — non-coercing: returns false for non-numbers
- [ ] `Number.isNaN(x)` — non-coercing: returns false for non-numbers
- [ ] `Number.isSafeInteger(x)` — `isInteger(x) && Math.Abs(x) <= MAX_SAFE_INTEGER`
- [ ] `Number.MAX_SAFE_INTEGER` property → `9007199254740991`
- [ ] `Number.MIN_SAFE_INTEGER` property → `-9007199254740991`
- [ ] `Number.MAX_VALUE` property → `double.MaxValue`
- [ ] `Number.MIN_VALUE` property → `double.Epsilon`
- [ ] `Number.EPSILON` property → `2.220446049250313e-16`
- [ ] `Number.POSITIVE_INFINITY` property → `double.PositiveInfinity`
- [ ] `Number.NEGATIVE_INFINITY` property → `double.NegativeInfinity`
- [ ] `Number.NaN` property → `double.NaN`
- [ ] `(num).toFixed(digits)` — instance method; format to N decimal places as string
- [ ] `(num).toString(radix?)` — instance method; base conversion (2–36)
- [ ] `(num).toExponential(digits?)` — instance method; scientific notation string

---

## Phase 6 — Array gaps (1–2 days)

All instance-method additions go in `ArrayFunctionProvider`. Static methods are
new entries with the receiver as `"this"` pointing to the `Array` class object
(or registered under `Array.*` directly).

### Instance methods
- [ ] `find(fn)` — return first element where `fn(elem, idx, arr)` is truthy, or `undefined`
- [ ] `findIndex(fn)` — return index of first match, or -1
- [ ] `findLast(fn)` — like `find` but iterates in reverse
- [ ] `findLastIndex(fn)` — like `findIndex` but iterates in reverse
- [ ] `some(fn)` — return `true` if any element passes `fn`
- [ ] `every(fn)` — return `true` if all elements pass `fn`
- [ ] `includes(val)` — value-equality membership test (deprecates non-standard `contains`)
- [ ] `flat(depth?)` — flatten nested arrays; default depth 1
- [ ] `flatMap(fn)` — `map(fn).flat(1)` in one pass
- [ ] `fill(val, start?, end?)` — set a range of elements to val in place
- [ ] `concat(...arrs)` — shallow-merge arrays into a new array (does not mutate)
- [ ] `splice(start, deleteCount?, ...items)` — in-place remove/insert; returns removed elements
- [ ] `at(index)` — negative-index element access
- [ ] `entries()` — returns array of `[index, value]` pairs (usable with `for...of`)
- [ ] `keys()` — returns array of indices (usable with `for...of`)
- [ ] `values()` — returns array of values (usable with `for...of`)

### Static methods
- [ ] `Array.isArray(val)` — return `true` if val is an array
- [ ] `Array.from(iterable, mapFn?)` — construct array from any iterable or array-like
- [ ] `Array.of(...vals)` — construct array from argument list

---

## Phase 7 — Date class (1–2 days)

Create `DateFunctionProvider` in `DScript.Extras`. `Date` instances must store a
`DateTimeOffset` in `ScriptVar`'s data field (same pattern as `VmFunction`).
The engine needs a `new Date(...)` constructor registered similarly to how
`Promise` is registered in `ScriptEngine.cs`.

### Scaffolding
- [ ] Create `DateObject` wrapper class holding a `DateTimeOffset`
- [ ] Register `Date` constructor in `ScriptEngine` (or via `EngineFunctionLoader`)
  - `new Date()` → current UTC
  - `new Date(ms)` → from epoch milliseconds
  - `new Date(str)` → `DateTimeOffset.Parse(str)`
  - `new Date(y, m, d, h?, min?, s?, ms?)` → component form
- [ ] `Date.now()` static → `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`

### Instance get methods
- [ ] `getTime()` — epoch ms
- [ ] `getFullYear()`, `getMonth()` (0-based), `getDate()`, `getDay()` (0=Sun)
- [ ] `getHours()`, `getMinutes()`, `getSeconds()`, `getMilliseconds()`
- [ ] UTC variants: `getUTCFullYear()`, `getUTCMonth()`, `getUTCDate()`,
  `getUTCDay()`, `getUTCHours()`, `getUTCMinutes()`, `getUTCSeconds()`,
  `getUTCMilliseconds()`
- [ ] `getTimezoneOffset()` — local offset in minutes

### Instance set methods
- [ ] `setTime(ms)`, `setFullYear(y)`, `setMonth(m)`, `setDate(d)`
- [ ] `setHours(h)`, `setMinutes(m)`, `setSeconds(s)`, `setMilliseconds(ms)`

### Formatting
- [ ] `toISOString()` — `"2024-06-22T15:30:00.000Z"`
- [ ] `toString()`, `toDateString()`, `toTimeString()`
- [ ] `toLocaleDateString()`, `toLocaleTimeString()`, `toLocaleString()`
- [ ] `toUTCString()`
- [ ] `valueOf()` — same as `getTime()`

---

## Phase 8 — Map (1 day)

Create `MapFunctionProvider`. `Map` instances store a `Dictionary<ScriptVar, ScriptVar>`
(reference equality for object keys) in `ScriptVar`'s data field. Register a `Map`
constructor similarly to `Date`.

- [ ] Create `MapObject` wrapper class holding `Dictionary<ScriptVar, ScriptVar>`
- [ ] Register `Map` constructor: `new Map()` and `new Map([[k,v],…])`
- [ ] `.get(key)` — return value or `undefined`
- [ ] `.set(key, val)` — store and return the Map (chainable)
- [ ] `.has(key)` — membership test
- [ ] `.delete(key)` — remove entry; return true if it existed
- [ ] `.clear()` — remove all entries
- [ ] `.size` property — entry count
- [ ] `.keys()` — array of keys
- [ ] `.values()` — array of values
- [ ] `.entries()` — array of `[key, value]` pairs
- [ ] `.forEach(fn)` — `fn(value, key, map)` for each entry

---

## Phase 9 — Set (1 day)

Create `SetFunctionProvider`. Backed by `HashSet<ScriptVar>` (reference equality
for objects, value equality for primitives). Register a `Set` constructor.

- [ ] Create `SetObject` wrapper class holding `HashSet<ScriptVar>`
- [ ] Register `Set` constructor: `new Set()` and `new Set(iterable)`
- [ ] `.add(val)` — add and return the Set (chainable)
- [ ] `.has(val)` — membership test
- [ ] `.delete(val)` — remove; return true if present
- [ ] `.clear()` — remove all
- [ ] `.size` property — element count
- [ ] `.keys()` / `.values()` — array of elements (identical for Set)
- [ ] `.entries()` — array of `[val, val]` pairs (matching JS spec)
- [ ] `.forEach(fn)` — `fn(value, value, set)` for each element
- [ ] `.union(other)` — new Set with all elements from both
- [ ] `.intersection(other)` — new Set with elements in both
- [ ] `.difference(other)` — new Set with elements not in other
- [ ] `.isSubsetOf(other)` — true if all elements are in other

---

## Phase 10 — Error types (half a day)

Create `ErrorFunctionProvider`. Each error type is a constructor function that
returns a plain object with `.name`, `.message`, and `.stack` properties.
Register each under the global scope (`AppearAtRoot = true`).

- [ ] `Error(msg?)` — `{ name: "Error", message: msg, stack: "" }`
- [ ] `TypeError(msg?)` — `{ name: "TypeError", message: msg, stack: "" }`
- [ ] `RangeError(msg?)` — `{ name: "RangeError", message: msg, stack: "" }`
- [ ] `ReferenceError(msg?)` — `{ name: "ReferenceError", message: msg, stack: "" }`
- [ ] `SyntaxError(msg?)` — `{ name: "SyntaxError", message: msg, stack: "" }`
- [ ] `URIError(msg?)` — `{ name: "URIError", message: msg, stack: "" }`
- [ ] `EvalError(msg?)` — `{ name: "EvalError", message: msg, stack: "" }`
- [ ] Make VM throw `ReferenceError`-shaped objects for undefined variable access
  (currently throws a plain `ScriptException`)

---

## Phase 11 — process (half a day)

Create `ProcessFunctionProvider` with `[ScriptClass("process")]`.

- [ ] `process.platform` property — `RuntimeInformation.OSDescription` mapped to
  `"win32"` / `"linux"` / `"darwin"` / `"freebsd"`
- [ ] `process.version` property — DScript version string constant
- [ ] `process.argv` property — array set by host via `engine.SetArgv(string[])`;
  defaults to empty array
- [ ] `process.exit(code?)` — `Environment.Exit(code ?? 0)`
- [ ] `process.env` — lazy object: `GetProp` handler reads `Environment.GetEnvironmentVariable(name)`
  (requires a special `ScriptVar` backed by a C# dictionary getter, not a static
  property; may need a small VM extension point or a pre-populated snapshot object)
