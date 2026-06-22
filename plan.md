# DScript.Extras — Native API Expansion Plan

This document describes every gap in the current `DScript.Extras` native API surface,
the rationale for each addition, and effort estimates. See `tasks.md` for the ordered
implementation checklist.

---

## Current surface (summary)

| Namespace | What exists |
|---|---|
| `console` | `log`, `error`, `clear` |
| `Math` | full trig + constants + `random`/`randInt` |
| `JSON` | `parse`, `stringify` |
| `String` | `indexOf`, `lastIndexOf`, `substring`, `substr`, `charAt`, `charCodeAt`, `fromCharCode`, `split`, `match`, `trim`, `toUpperCase`, `toLowerCase`, `concat`, `replace` |
| `Array` | `push`, `pop`, `shift`, `unshift`, `slice`, `sort`, `reverse`, `map`, `filter`, `forEach`, `reduce`, `indexOf`, `join`, `contains`, `remove` |
| `Object` | `keys`, `hasOwnProperty`, `dump`, `clone` |
| `Integer` (globals) | `parseInt`, `parseFloat`, `isNaN`, `isFinite` |
| Root globals | `eval`, `exec`, `trace`, `charToInt` |

---

## 1. String — ES2015–2022 gaps

**Problem**  
Fourteen commonly-used `String.prototype` methods are absent. Code that calls
`str.startsWith(...)`, `str.includes(...)`, `str.padStart(...)`, etc. throws at
runtime. All map trivially to C# `string` methods.

**Additions**

| Method | C# backing |
|---|---|
| `startsWith(prefix, pos?)` | `str.StartsWith()` |
| `endsWith(suffix, len?)` | `str.EndsWith()` |
| `includes(search, pos?)` | `str.Contains()` |
| `repeat(n)` | `string.Concat(Enumerable.Repeat(...))` |
| `padStart(len, fill?)` | `str.PadLeft()` |
| `padEnd(len, fill?)` | `str.PadRight()` |
| `slice(start, end?)` | negative-index substring (semantics differ from `substring`) |
| `trimStart()` / `trimEnd()` | `str.TrimStart()` / `str.TrimEnd()` |
| `replaceAll(what, with)` | `str.Replace()` |
| `at(index)` | negative-index single-character access |
| `search(regex)` | `Regex.Match()` → first match index |
| `matchAll(regex)` | `Regex.Matches()` → array of match arrays |

**Effort:** Small (half a day). Pure C# string wrappers; no VM changes needed.

---

## 2. Array — missing half the spec

**Problem**  
`find`, `findIndex`, `some`, `every`, `includes`, `flat`, `flatMap`, `fill`,
`concat`, `splice`, `at`, and the three static constructors (`Array.isArray`,
`Array.from`, `Array.of`) are all absent. These are among the most-used array
methods in modern JavaScript.

**Additions — instance methods**

| Method | Notes |
|---|---|
| `find(fn)` / `findIndex(fn)` | first element / index where fn returns truthy |
| `findLast(fn)` / `findLastIndex(fn)` | reverse variants (ES2023) |
| `some(fn)` | returns true if any element passes |
| `every(fn)` | returns true if all elements pass |
| `includes(val)` | value-equality test (replaces non-standard `contains`) |
| `flat(depth?)` | flatten nested arrays up to depth (default 1) |
| `flatMap(fn)` | map then flat(1) |
| `fill(val, start?, end?)` | fill a range with a value in place |
| `concat(...arrs)` | shallow-merge arrays into a new array |
| `splice(start, deleteCount, ...items)` | in-place insert/remove |
| `at(index)` | negative-index access |
| `entries()` / `keys()` / `values()` | return index-iterable objects; integrates with `for...of` |

**Additions — static methods**

| Method | Notes |
|---|---|
| `Array.isArray(val)` | type test; very commonly used in guard code |
| `Array.from(iterable, mapFn?)` | construct from any iterable |
| `Array.of(...vals)` | construct from argument list |

**Effort:** Medium (1–2 days). Higher-order callbacks (`find`, `some`, `every`,
`flatMap`) follow the same pattern as `map`/`filter` already in place.

---

## 3. Object — sparse static surface

**Problem**  
`Object.values` and `Object.entries` are symmetric with the existing `Object.keys`
but are absent — a very visible gap. `Object.assign` is needed for the common
shallow-merge pattern. `fromEntries` completes the round-trip with `entries`.

**Additions**

| Method | Notes |
|---|---|
| `Object.values(obj)` | array of own enumerable values |
| `Object.entries(obj)` | array of `[key, value]` pairs |
| `Object.assign(target, ...sources)` | shallow merge; returns target |
| `Object.fromEntries(entries)` | inverse of `entries`; construct object from `[[k,v],…]` |
| `Object.freeze(obj)` / `Object.isFrozen(obj)` | immutability (can no-op if deep freeze is too costly) |
| `Object.create(proto)` | create object with given prototype |
| `Object.getOwnPropertyNames(obj)` | like `keys` but includes non-enumerable |

**Effort:** Small (half a day).

---

## 4. Math — small gaps

**Problem**  
`Math.trunc`, `Math.sign`, `Math.hypot`, `Math.log2`, `Math.log10`, `Math.cbrt`
are all single-line wrappers around `System.Math` methods that are completely absent.

**Additions**

| Method | C# backing |
|---|---|
| `Math.trunc(x)` | `Math.Truncate()` |
| `Math.sign(x)` | `Math.Sign()` |
| `Math.hypot(...vals)` | `Math.Sqrt(sum of squares)` |
| `Math.log2(x)` | `Math.Log2()` |
| `Math.log10(x)` | `Math.Log10()` |
| `Math.cbrt(x)` | `Math.Cbrt()` |
| `Math.clamp(x, lo, hi)` | `Math.Clamp()` (not in JS spec but highly practical) |
| `Math.fround(x)` | `(double)(float)x` |
| `Math.imul(a, b)` | `unchecked((int)a * (int)b)` |

**Effort:** Trivial (< 1 hour).

---

## 5. Number — rename and complete `Integer`

**Problem**  
The current `Integer` class name is non-idiomatic (JS uses `Number`). It is also
missing all numeric constants and the instance methods `toFixed`/`toString(radix)`.

**Additions**

| Addition | Notes |
|---|---|
| `Number` namespace alias (or rename) | expose all existing `Integer` functions under `Number` too |
| `Number.isInteger(x)` | |
| `Number.isFinite(x)` / `Number.isNaN(x)` | non-coercing variants (unlike global `isNaN`) |
| `Number.isSafeInteger(x)` | |
| `Number.MAX_SAFE_INTEGER` / `MIN_SAFE_INTEGER` | `±9007199254740991` |
| `Number.MAX_VALUE` / `MIN_VALUE` / `EPSILON` | `double` constants |
| `Number.POSITIVE_INFINITY` / `NEGATIVE_INFINITY` / `NaN` | |
| `(num).toFixed(digits)` | instance method — round to N decimal places as string |
| `(num).toString(radix?)` | instance method — base conversion |
| `(num).toExponential(digits?)` | instance method — scientific notation string |

**Effort:** Small (half a day).

---

## 6. console — missing the practical half

**Problem**  
Only `log`, `error`, and `clear` exist. Anything that writes to `warn`, `info`,
or does assertions or timing requires the caller to work around the gap.

**Additions**

| Method | Behaviour |
|---|---|
| `console.warn(val)` | stderr, prefix `[WARN] ` |
| `console.info(val)` | stdout, prefix `[INFO] ` |
| `console.debug(val)` | stdout, prefix `[DEBUG] ` |
| `console.assert(cond, msg?)` | writes `[ASSERT] msg` to stderr if cond is falsy |
| `console.time(label)` | starts a named `Stopwatch` |
| `console.timeEnd(label)` | stops the stopwatch, prints elapsed ms |
| `console.count(label?)` | increments and prints a named counter |
| `console.countReset(label?)` | resets named counter |
| `console.group(label?)` / `console.groupEnd()` | increase/decrease indent level for subsequent log output |
| `console.dir(obj)` | structured object dump (calls `.Trace`) |
| `console.table(arr)` | pretty-print an array of objects as a text table |

**Effort:** Small (half a day). `time`/`timeEnd` need a static dictionary of
`Stopwatch` instances; the rest are trivial string formatting.

---

## 7. Date — completely absent

**Problem**  
There is no `Date` class at all. Any script that needs the current time, date
arithmetic, or formatted timestamps has no built-in support. `System.DateTimeOffset`
covers all required functionality.

**Constructor forms**
- `new Date()` — current UTC time
- `new Date(milliseconds)` — from Unix epoch ms
- `new Date(isoString)` — from ISO 8601 string
- `new Date(year, month, day, hour?, min?, sec?, ms?)` — component form

**Static methods**
- `Date.now()` — current epoch milliseconds

**Instance methods (get)**
- `getFullYear()`, `getMonth()` (0-based), `getDate()`, `getDay()` (0=Sun)
- `getHours()`, `getMinutes()`, `getSeconds()`, `getMilliseconds()`
- `getTime()` — epoch milliseconds
- UTC variants: `getUTCFullYear()`, `getUTCMonth()`, etc.

**Instance methods (set)**
- `setFullYear(y)`, `setMonth(m)`, `setDate(d)`
- `setHours(h)`, `setMinutes(m)`, `setSeconds(s)`, `setMilliseconds(ms)`
- `setTime(ms)`

**Formatting**
- `toISOString()` — `"2024-06-22T15:30:00.000Z"`
- `toLocaleDateString()`, `toLocaleTimeString()`, `toLocaleString()`
- `toString()`, `toDateString()`, `toTimeString()`
- `toUTCString()`

**Implementation note**  
`Date` objects need to be backed by a `DateTimeOffset` stored in `ScriptVar`'s
data field (same pattern as `VmFunction`). The constructor needs to be registered
with the engine as a special `new Date(...)` form.

**Effort:** Medium (1–2 days).

---

## 8. Map

**Problem**  
ES6 `Map` is one of the most-used collection types in modern JavaScript. The
current alternative is plain objects, which coerce all keys to strings.

**API**
- `new Map()` / `new Map([[k,v],…])` — constructors
- `.get(key)` / `.set(key, val)` / `.has(key)` / `.delete(key)` / `.clear()`
- `.size` — property
- `.keys()` / `.values()` / `.entries()` — return array-based iterators
- `.forEach(fn)` — `fn(value, key, map)`

**Implementation note**  
Backed by `Dictionary<ScriptVar, ScriptVar>` with reference-equality semantics for
object keys (matching JS). Store the dictionary in `ScriptVar`'s data field.

**Effort:** Medium (1 day).

---

## 9. Set

**Problem**  
`Set` provides O(1) membership testing and uniqueness guarantees that arrays cannot
match. Commonly used as a deduplification tool and for fast `has()` checks.

**API**
- `new Set()` / `new Set(iterable)` — constructors
- `.add(val)` / `.has(val)` / `.delete(val)` / `.clear()`
- `.size` — property
- `.keys()` / `.values()` / `.entries()` / `.forEach(fn)`
- Set algebra: `.union(other)`, `.intersection(other)`, `.difference(other)`,
  `.isSubsetOf(other)` (ES2024)

**Implementation note**  
Backed by `HashSet<ScriptVar>` with the same equality semantics as `Map`.

**Effort:** Small–Medium (1 day). Simpler than `Map` since keys = values.

---

## 10. Error types

**Problem**  
`throw` and `catch` work, but there are no standard error constructor functions.
Catching specific error types (`catch(e) { if (e instanceof TypeError) ... }`)
is impossible.

**Additions**  
Constructor functions that create an object with `.name`, `.message`, `.stack`:
- `Error(msg?)` — base
- `TypeError(msg?)` — wrong type
- `RangeError(msg?)` — out-of-range value
- `ReferenceError(msg?)` — undefined variable (thrown by VM)
- `SyntaxError(msg?)` — compilation error (thrown by compiler)
- `URIError(msg?)`, `EvalError(msg?)` — completeness

**Effort:** Small (half a day). Each is a function that returns a plain ScriptVar
object with `.name` and `.message` set.

---

## 11. performance

**Problem**  
Scripts have no way to measure elapsed time at sub-millisecond resolution for
profiling or benchmarking their own code.

**Additions**

| Method | C# backing |
|---|---|
| `performance.now()` | `Stopwatch.GetTimestamp()` scaled to milliseconds; monotonic, high resolution |

**Effort:** Trivial (< 1 hour).

---

## 12. structuredClone and queueMicrotask

**Problem**  
`structuredClone(val)` (deep clone) has a native backing in `ScriptVar.CopyValue()`
but is not exposed as a global. `queueMicrotask(fn)` is needed when script code
wants to schedule microtasks without creating a full Promise.

**Additions**

| Function | Notes |
|---|---|
| `structuredClone(val)` | calls `.CopyValue()`; exposes existing functionality |
| `queueMicrotask(fn)` | enqueues `fn` on `MicroTaskQueue`; flushes with `engine.DrainMicroTasks()` |

**Effort:** Trivial (< 1 hour each).

---

## 13. process (host-context utilities)

**Problem**  
Embedded scripts often need to read environment variables, inspect the platform, or
exit the host process. None of this is exposed.

**Additions**

| Member | Notes |
|---|---|
| `process.platform` | `"win32"` / `"linux"` / `"darwin"` etc. |
| `process.env` | object whose properties map to `Environment.GetEnvironmentVariable()` |
| `process.argv` | array of strings (injected by host at engine setup) |
| `process.exit(code?)` | calls `Environment.Exit(code)` |
| `process.version` | DScript runtime version string |

**Effort:** Small (half a day). `process.env` is best implemented as a lazy proxy
object whose `GetProp` handler calls `Environment.GetEnvironmentVariable`.

---

## Priority summary

| # | Item | Effort | Impact |
|---|---|---|---|
| 1 | String gaps | Half day | High — used constantly |
| 2 | Array gaps | 1–2 days | High — used constantly |
| 3 | Object values/entries/assign | Half day | High — symmetric with keys |
| 4 | Math gaps | < 1 hour | Low effort, fills spec |
| 5 | Number class | Half day | Medium |
| 6 | console gaps | Half day | Medium |
| 7 | Date | 1–2 days | High — no date support at all |
| 8 | Map | 1 day | High — fundamental collection |
| 9 | Set | 1 day | Medium |
| 10 | Error types | Half day | Medium |
| 11 | performance.now | < 1 hour | Low effort |
| 12 | structuredClone / queueMicrotask | < 1 hour | Low effort |
| 13 | process | Half day | Medium (host-context scripts) |
