# DScript.Extras — Native API Expansion Plan (Phase 2)

All items from Phase 1 (String, Array, Object, Math, Number, console, Date, Map, Set, Error types, performance, structuredClone/queueMicrotask, process) are complete.

DScript serves two use cases: **in-app scripting** (embedded in a host application) and **application platform** (standalone scripts, Node.js-style). Items are labelled _(both)_, _(app)_, or _(embed)_ where the distinction matters.

---

## Foundational

### 1. Module system _(both)_

The single most important missing piece. Without it you cannot split code across files, build libraries, or structure any non-trivial application. Everything else is incremental; this is load-bearing.

Needs engine support for loading and caching compiled chunks from disk.

**API**
- `require(path)` — load and cache a module by path
- `module.exports` / `exports` — the value a module exposes
- `__filename` — absolute path of the current script file
- `__dirname` — directory containing the current script file

**Effort:** Large (2–3 days). Core engine change; the native wrappers are trivial once chunk loading works.

---

### 2. EventEmitter (`events` module) _(both)_

The backbone of the Node.js architecture and the right abstraction for in-app scripting callbacks. The host app can emit events into scripts; scripts can wire listeners to host-side events.

**API**
- `new EventEmitter()`
- `.on(event, fn)` / `.once(event, fn)`
- `.off(event, fn)` / `.removeAllListeners(event?)`
- `.emit(event, ...args)` → bool
- `.listeners(event)` → array
- `.listenerCount(event)` → int
- `EventEmitter.defaultMaxListeners`

**Effort:** Medium (1 day). Pure script-side object; no engine changes needed.

---

### 3. Buffer _(both)_

Binary data class. Needed to make `fs`, `net`, `crypto`, and `http` body handling practical. Without it, those modules can only work with text.

**API**
- `Buffer.from(str, enc?)` / `Buffer.from(array)` / `Buffer.from(arrayBuffer)`
- `Buffer.alloc(size, fill?)` / `Buffer.allocUnsafe(size)`
- `Buffer.isBuffer(val)` / `Buffer.concat(list)`
- `.toString(enc?)` / `.length` / `.slice(start, end?)`
- `.readUInt8(offset)` / `.writeUInt8(val, offset)` and common numeric read/write variants
- `.copy(target, targetStart?)` / `.equals(other)` / `.compare(other)`

**Implementation note**  
Backed by `byte[]` stored in ScriptVar's data field. Constructor registration follows the Date/Map/Set pattern.

**Effort:** Medium (1 day).

---

## Core globals

### 4. Timer functions _(both)_

`setTimeout`, `clearTimeout`, `setInterval`, `clearInterval` are entirely absent. Scripts cannot do any async timing without them.

Requires engine integration: a scheduled-callback queue alongside the existing `MicroTaskQueue`. The host drives the timer loop via `engine.DrainTimers()`.

**API**
- `setTimeout(fn, delay?)` → handle id
- `clearTimeout(id)`
- `setInterval(fn, interval?)` → handle id
- `clearInterval(id)`

**Effort:** Medium (1–2 days). The queue is new infrastructure; native registrations are trivial once it exists.

---

### 5. Promise combinators + static constructors _(both)_

Callers expect these alongside the core `Promise`.

| Method | Behaviour |
|---|---|
| `Promise.resolve(val)` | returns a promise already resolved with val |
| `Promise.reject(reason)` | returns a promise already rejected with reason |
| `Promise.all(arr)` | resolves when all resolve; rejects on first rejection |
| `Promise.allSettled(arr)` | always resolves with array of `{status, value/reason}` |
| `Promise.race(arr)` | settles with the first to settle |
| `Promise.any(arr)` | resolves with first fulfillment; rejects with AggregateError if all reject |

**Effort:** Medium (1 day). Depends on how Promise is wired internally.

---

### 6. URI encoding globals _(both)_

One-liner wrappers over `Uri.EscapeDataString` / `Uri.UnescapeDataString`. AppearAtRoot.

| Function | C# backing |
|---|---|
| `encodeURIComponent(str)` | `Uri.EscapeDataString()` |
| `decodeURIComponent(str)` | `Uri.UnescapeDataString()` |
| `encodeURI(str)` | escape everything except `: / ? # [ ] @ ! $ & ' ( ) * + , ; = ~` |
| `decodeURI(str)` | inverse of `encodeURI` |

**Effort:** Trivial (< 1 hour).

---

### 7. Base64 globals _(both)_

`btoa` / `atob` are one-liners on `Convert.ToBase64String` / `Convert.FromBase64String`. AppearAtRoot.

**Effort:** Trivial (< 1 hour).

---

### 8. process lifecycle hooks _(both)_

Scripts need to register cleanup and error handlers.

| Hook | Notes |
|---|---|
| `process.on('exit', fn)` | called just before the process exits |
| `process.on('uncaughtException', fn)` | called when an exception escapes the top level |
| `process.on('unhandledRejection', fn)` | called when a Promise rejection is unhandled |

**Effort:** Small (half a day). Requires a hook dispatch point in the VM's top-level exception handler.

---

## Standard library modules

### 9. `path` module _(both)_

Pure string manipulation; no I/O. Trivial to implement, immediately useful.

| Member | C# backing |
|---|---|
| `path.join(...parts)` | `Path.Combine()` + normalise separators |
| `path.resolve(...parts)` | `Path.GetFullPath(Path.Combine(...))` |
| `path.dirname(p)` | `Path.GetDirectoryName()` |
| `path.basename(p, ext?)` | `Path.GetFileName()` / `Path.GetFileNameWithoutExtension()` |
| `path.extname(p)` | `Path.GetExtension()` |
| `path.isAbsolute(p)` | `Path.IsPathRooted()` |
| `path.normalize(p)` | `Path.GetFullPath()` on a relative path |
| `path.sep` | `Path.DirectorySeparatorChar.ToString()` |

**Effort:** Trivial (< 1 hour).

---

### 10. `os` module _(both)_

All one-liners on `System.Environment` / `RuntimeInformation`.

| Member | C# backing |
|---|---|
| `os.hostname()` | `Dns.GetHostName()` |
| `os.platform()` | same logic as `process.platform` |
| `os.arch()` | `RuntimeInformation.ProcessArchitecture.ToString().ToLower()` |
| `os.homedir()` | `Environment.GetFolderPath(SpecialFolder.UserProfile)` |
| `os.tmpdir()` | `Path.GetTempPath()` |
| `os.totalmem()` | `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` |
| `os.freemem()` | platform-specific |
| `os.cpus()` | `Environment.ProcessorCount` (int, not array) |
| `os.EOL` | `Environment.NewLine` |

**Effort:** Trivial (< 1 hour).

---

### 11. `fs` module _(app)_

Makes DScript a viable shell-script replacement. Synchronous-only to start; async variants can come later.

| Member | Notes |
|---|---|
| `fs.readFileSync(path, enc?)` | `File.ReadAllText()` / `File.ReadAllBytes()` |
| `fs.writeFileSync(path, data, enc?)` | `File.WriteAllText()` / `File.WriteAllBytes()` |
| `fs.appendFileSync(path, data, enc?)` | `File.AppendAllText()` |
| `fs.existsSync(path)` | `File.Exists() \|\| Directory.Exists()` |
| `fs.mkdirSync(path, opts?)` | `Directory.CreateDirectory()` |
| `fs.rmdirSync(path)` | `Directory.Delete()` |
| `fs.unlinkSync(path)` | `File.Delete()` |
| `fs.readdirSync(path)` | `Directory.GetFileSystemEntries()` → array of names |
| `fs.renameSync(old, new)` | `File.Move()` |
| `fs.statSync(path)` | returns `{ size, isFile(), isDirectory(), mtime }` |
| `fs.copyFileSync(src, dest)` | `File.Copy()` |
| `fs.readlinkSync(path)` | `File.ResolveLinkTarget()` |

**Effort:** Small (half a day).

---

### 12. `fetch` _(both)_

HTTP client. Turns DScript from a compute/logic engine into something that can talk to the outside world.

**API**
- `fetch(url, opts?)` → Promise\<Response\>
- `opts`: `{ method, headers, body }`
- Response: `.ok`, `.status`, `.statusText`, `.headers`, `.text()`, `.json()`, `.arrayBuffer()`

**Implementation note**  
`HttpClient` should be a singleton. A synchronous fallback (blocking `HttpClient.Send`) avoids requiring the timer queue before it is built.

**Effort:** Medium (1 day).

---

### 13. `http` module _(app)_

Create HTTP servers, not just make requests. The other half of `fetch`.

**API**
- `http.createServer(fn)` → server object; `fn(req, res)`
- `server.listen(port, host?, cb?)`
- `server.close()`
- Request: `.method`, `.url`, `.headers`, `.on('data', fn)`, `.on('end', fn)`
- Response: `.writeHead(status, headers?)`, `.write(data)`, `.end(data?)`

**Effort:** Large (2+ days). Needs async I/O integration.

---

### 14. `child_process` module _(app)_

Spawn subprocesses. Needed for any script that shells out to system tools.

| Member | Notes |
|---|---|
| `execSync(cmd, opts?)` | `Process.Start()` + capture stdout/stderr; returns stdout string |
| `spawnSync(cmd, args?, opts?)` | same but with argument array; returns `{ stdout, stderr, status }` |
| `exec(cmd, cb)` | async variant; callback is `(err, stdout, stderr)` |
| `spawn(cmd, args?, opts?)` | async; returns object with `.stdout`, `.stderr`, `.on('exit', fn)` |

**Effort:** Medium (1 day).

---

### 15. `readline` module _(app)_

Interactive CLI input. Required for command-line apps.

**API**
- `readline.createInterface({ input, output })` → rl
- `rl.question(prompt, cb)` — print prompt, read one line, call cb(answer)
- `rl.close()`
- `rl.on('line', fn)` / `rl.on('close', fn)`

**Effort:** Small (half a day).

---

### 16. `assert` module _(both)_

Standard Node.js testing/validation utility. Useful for script-side defensive coding and test scripts.

| Member | Notes |
|---|---|
| `assert(val, msg?)` / `assert.ok(val, msg?)` | throws if falsy |
| `assert.equal(a, b, msg?)` | `==` comparison |
| `assert.strictEqual(a, b, msg?)` | `===` comparison |
| `assert.notEqual` / `assert.notStrictEqual` | inverses |
| `assert.deepEqual(a, b, msg?)` | recursive value equality |
| `assert.throws(fn, err?, msg?)` | asserts fn throws |
| `assert.doesNotThrow(fn, msg?)` | asserts fn does not throw |
| `assert.fail(msg?)` | unconditional failure |

**Effort:** Small (half a day).

---

### 17. `util` module _(both)_

`util.format` is used internally by `console.log` in Node.js and widely elsewhere.

| Member | Notes |
|---|---|
| `util.format(fmt, ...args)` | printf-style: `%s`, `%d`, `%i`, `%f`, `%o`, `%j` |
| `util.inspect(val, opts?)` | pretty-print any value (like `JSON.stringify` but handles cycles, functions, etc.) |
| `util.promisify(fn)` | wraps a `(err, result)` callback-style function as a Promise-returning one |
| `util.deprecate(fn, msg)` | wraps a function; prints deprecation warning on first call |
| `util.isArray` / `util.isString` etc. | legacy type checks (low priority) |

**Effort:** Small (half a day). `util.format` and `util.inspect` are the high-value parts.

---

### 18. `crypto` module _(both)_

Extends the planned `crypto.randomUUID` / `randomBytes` with proper hashing and HMAC.

| Member | Notes |
|---|---|
| `crypto.randomUUID()` | `Guid.NewGuid().ToString()` |
| `crypto.randomBytes(n)` | `RandomNumberGenerator.GetBytes(n)` → Buffer |
| `crypto.getRandomValues(arr)` | fills a script array with cryptographically random ints |
| `crypto.createHash(algo)` | returns hash object; `.update(data)`, `.digest(enc)` |
| `crypto.createHmac(algo, key)` | returns hmac object; `.update(data)`, `.digest(enc)` |
| Supported algos | `'sha256'`, `'sha512'`, `'sha1'`, `'md5'` via `System.Security.Cryptography` |

**Effort:** Small (half a day).

---

## Language completeness

### 19. `RegExp` constructor _(both)_

`new RegExp(pattern, flags?)` lets scripts build regexes dynamically rather than only using literals. Backed by a `Regex` object stored in ScriptVar's data field.

**API**
- `new RegExp(pattern, flags?)` — constructor
- `.test(str)` → bool
- `.exec(str)` → match array or null
- `.source`, `.flags`, `.global`, `.ignoreCase`, `.multiline` — properties

**Effort:** Small (half a day). Constructor registration follows the Date/Map/Set pattern.

---

### 20. Array additions _(both)_

| Method | Notes |
|---|---|
| `reduceRight(fn, init?)` | logical companion to existing `reduce` |
| `toSorted(fn?)` | non-mutating `sort` (ES2023) |
| `toReversed()` | non-mutating `reverse` (ES2023) |
| `toSpliced(start, del, ...items)` | non-mutating `splice` (ES2023) |
| `with(index, val)` | returns copy with one element replaced (ES2023) |

**Effort:** Trivial (< 1 hour). Non-mutating variants are copy + delegate to existing methods.

---

### 21. Object additions _(both)_

| Method | Notes |
|---|---|
| `Object.hasOwn(obj, key)` | ES2022; cleaner than `hasOwnProperty` |
| `Object.is(a, b)` | handles `NaN === NaN` → true and `+0 !== -0` |
| `Object.seal(obj)` / `Object.isSealed(obj)` | complement to existing `freeze`/`isFrozen` |
| `Object.groupBy(arr, fn)` | ES2024; groups items into `{ key: [items] }` |
| `Map.groupBy(arr, fn)` | ES2024; same but result is a Map |

**Effort:** Trivial (< 1 hour).

---

### 22. Error improvements _(both)_

| Addition | Notes |
|---|---|
| `AggregateError(errors, msg?)` | wraps multiple errors; required by `Promise.any` |
| `.cause` property | ES2022 error chaining; `new Error('msg', { cause: err })` |

**Effort:** Trivial (< 1 hour).

---

### 23. String additions _(both)_

| Method | Notes |
|---|---|
| `(str).normalize(form?)` | Unicode NFC/NFD/NFKC/NFKD; `str.Normalize(NormalizationForm.*)` |
| `(str).codePointAt(pos)` | full Unicode code point (handles surrogates) |
| `String.fromCodePoint(...codes)` | inverse; `char.ConvertFromUtf32()` |

**Effort:** Trivial (< 1 hour).

---

### 24. Number additions _(both)_

- `(num).toPrecision(digits?)` — significant-figure formatting; `((double)val).ToString("G" + digits)`

**Effort:** Trivial (< 1 hour).

---

### 25. `console.timeLog` _(both)_

Print elapsed time without stopping the timer. Completes the `time` / `timeLog` / `timeEnd` trio.

**Effort:** Trivial (< 1 hour).

---

## In-app scripting concerns

### 26. Sandboxing / permission model _(embed)_

A way for the host to declare what a script is allowed to do. Right now it is all-or-nothing. A capability object at engine creation time lets the host control exposure without patching providers.

**Design**
- `EnginePermissions` flags: `FileSystem`, `Network`, `ProcessSpawn`, `ProcessExit`, `EnvironmentVariables`
- Providers check their assigned permissions before executing; throw a `PermissionError` on violation
- `EngineFunctionLoader.RegisterFunctions(engine, permissions)` — host passes the permission set at startup

**Effort:** Medium (1 day). Mostly mechanical: add permission checks to `fs`, `child_process`, `process.exit`, `fetch`, `process.getenv`.

---

### 27. Script timeout / resource limits _(embed)_

Kill scripts that exceed a CPU time or instruction budget. Critical for untrusted scripts inside a host app.

**Design**
- `engine.SetTimeout(TimeSpan)` — cancels execution after wall-clock time elapses
- `engine.SetInstructionLimit(long)` — cancels after N VM instructions
- Both throw a `ScriptTimeoutException` catchable by the host

**Effort:** Medium (1 day). Needs a counter/timer check in the VM dispatch loop.

---

### 28. Host object injection _(embed)_

A cleaner API for hosts to expose their own C# objects as script-visible values, beyond `AddNative`. Removes the need to hand-write a provider for every host type.

**Design**
- `engine.SetGlobal("name", obj)` — exposes a C# object; public properties and methods become script-accessible
- Attribute-based opt-in: `[ScriptVisible]` on members to control exactly what is exposed
- Read-only by default; `[ScriptWritable]` to allow script-side assignment

**Effort:** Large (2+ days). Requires reflection-based dispatch or source-generated wrappers.

---

### 29. `console` output routing _(embed)_

Let the host redirect `console.log` / `console.error` to its own logger rather than stdout/stderr.

**Design**
- `ConsoleFunctionProvider.SetOutput(Action<string> stdout, Action<string> stderr)` — static delegates the host can set before running scripts
- Defaults to `Console.WriteLine` / `Console.Error.WriteLine` for backward compatibility

**Effort:** Trivial (< 1 hour).

---

## Lower value / niche

### 30. `stream` module _(app)_

Simplified Readable/Writable/Transform. Complex to implement well, but needed if `http` and `net` are to handle large bodies practically.

**Effort:** Large (2+ days). Defer until `http` and `net` are in place and the need is proven.

---

### 31. `net` module _(app)_

Raw TCP client/server. Needed for custom protocols below HTTP.

**API**
- `net.createServer(fn)` → server; `fn(socket)`
- `net.createConnection(port, host?)` → socket
- Socket: `.write(data)`, `.end()`, `.on('data', fn)`, `.on('close', fn)`

**Effort:** Large (2+ days). Needs async I/O integration.

---

### 32. `zlib` module _(app)_

Compress/decompress data. Backed by `System.IO.Compression`.

| Member | Notes |
|---|---|
| `zlib.gzipSync(data)` | `GZipStream` compress → Buffer |
| `zlib.gunzipSync(data)` | `GZipStream` decompress → Buffer |
| `zlib.deflateSync(data)` | `DeflateStream` compress |
| `zlib.inflateSync(data)` | `DeflateStream` decompress |

**Effort:** Small (half a day). Only worth adding once `Buffer` exists.

---

### 33. URL / URLSearchParams _(both)_

Backed by `System.Uri` and a simple query-string parser.

**API (URL)**
- `new URL(href)` — constructor
- `.href`, `.protocol`, `.host`, `.hostname`, `.port`, `.pathname`, `.search`, `.hash`, `.origin`
- `.searchParams` → URLSearchParams instance; `.toString()`

**API (URLSearchParams)**
- `new URLSearchParams(str?)` — constructor
- `.get(key)`, `.set(key, val)`, `.append(key, val)`, `.delete(key)`, `.has(key)`
- `.toString()`, `.entries()`, `.keys()`, `.values()`

**Effort:** Medium (1 day).

---

### 34. TextEncoder / TextDecoder _(both)_

For byte-level UTF-8 work. Limited value without `ArrayBuffer` / `Uint8Array`.

| Class | Notes |
|---|---|
| `new TextEncoder()` | `.encode(str)` → Buffer (or array of byte ints) |
| `new TextDecoder(encoding?)` | `.decode(bytes)` → string |

**Effort:** Small (half a day).

---

## Priority summary

| # | Item | Effort | Use case | Impact |
|---|---|---|---|---|
| 1 | Module system | 2–3 days | both | Critical — nothing scales without it |
| 2 | EventEmitter | 1 day | both | High — core architectural pattern |
| 3 | Buffer | 1 day | both | High — unlocks fs/net/crypto/http |
| 4 | Timer functions | 1–2 days | both | High — fundamental async primitive |
| 5 | Promise combinators + resolve/reject | 1 day | both | High — expected alongside Promise |
| 6 | path module | < 1 hour | both | High — trivial, needed for scripting |
| 7 | os module | < 1 hour | both | High — trivial, useful companion to process |
| 8 | URI encoding globals | < 1 hour | both | High — trivial, widely needed |
| 9 | Base64 btoa/atob | < 1 hour | both | High — trivial, widely needed |
| 10 | fs module | Half day | app | High — makes DScript a scripting engine |
| 11 | fetch | 1 day | both | High — opens up network use cases |
| 12 | child_process | 1 day | app | High — needed for shell scripting |
| 13 | assert module | Half day | both | Medium — testing + defensive coding |
| 14 | util module | Half day | both | Medium — format/inspect/promisify |
| 15 | http server | 2+ days | app | High — other half of fetch |
| 16 | readline | Half day | app | Medium — interactive CLI apps |
| 17 | process lifecycle hooks | Half day | both | Medium — exit/exception handlers |
| 18 | crypto module | Half day | both | Medium — hashing, UUID, random bytes |
| 19 | Sandboxing / permissions | 1 day | embed | High — safety for untrusted scripts |
| 20 | Script timeout / limits | 1 day | embed | High — safety for untrusted scripts |
| 21 | Host object injection | 2+ days | embed | High — ergonomic host integration |
| 22 | console output routing | < 1 hour | embed | High — trivial, needed for host logging |
| 23 | RegExp constructor | Half day | both | Medium — dynamic regex creation |
| 24 | Array additions (ES2023 + reduceRight) | < 1 hour | both | Medium |
| 25 | Object additions (hasOwn/is/seal/groupBy) | < 1 hour | both | Medium |
| 26 | Error improvements (AggregateError, cause) | < 1 hour | both | Medium — needed by Promise.any |
| 27 | String additions (normalize/codePointAt) | < 1 hour | both | Low–medium |
| 28 | Number.toPrecision | < 1 hour | both | Low |
| 29 | console.timeLog | < 1 hour | both | Low |
| 30 | stream module | 2+ days | app | Medium — defer until http/net exist |
| 31 | net module | 2+ days | app | Low (niche) |
| 32 | zlib module | Half day | app | Low (niche) |
| 33 | URL / URLSearchParams | 1 day | both | Low (niche) |
| 34 | TextEncoder / TextDecoder | Half day | both | Low (niche) |
