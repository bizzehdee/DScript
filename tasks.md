# DScript.Extras — API Expansion Tasks (Phase 2)

Phase 1 (Math, String, console, Object, Number, Array, Date, Map, Set, Error types, performance, structuredClone/queueMicrotask, process) is complete.

Tasks are ordered by effort-to-impact ratio. Each phase is independent and can be committed separately. See `plan.md` for full design notes.

Status: `[ ]` todo · `[~]` in progress · `[x]` done

---

## Phase 1 — Trivial fills (< 2 hours total)

### 1a — Core globals
- [ ] `encodeURIComponent(str)` → `Uri.EscapeDataString()` (root global)
- [ ] `decodeURIComponent(str)` → `Uri.UnescapeDataString()` (root global)
- [ ] `encodeURI(str)` — escape all except `: / ? # [ ] @ ! $ & ' ( ) * + , ; = ~` (root global)
- [ ] `decodeURI(str)` — inverse of `encodeURI` (root global)
- [ ] `btoa(str)` → `Convert.ToBase64String()` (root global)
- [ ] `atob(str)` → `Convert.FromBase64String()` (root global)

### 1b — path module
- [ ] `path.join(...parts)` → `Path.Combine()` + normalise separators
- [ ] `path.resolve(...parts)` → `Path.GetFullPath(Path.Combine(...))`
- [ ] `path.dirname(p)` → `Path.GetDirectoryName()`
- [ ] `path.basename(p, ext?)` → `Path.GetFileName()` / `Path.GetFileNameWithoutExtension()`
- [ ] `path.extname(p)` → `Path.GetExtension()`
- [ ] `path.isAbsolute(p)` → `Path.IsPathRooted()`
- [ ] `path.normalize(p)` → `Path.GetFullPath()`
- [ ] `path.sep` property → `Path.DirectorySeparatorChar.ToString()`

### 1c — os module
- [ ] `os.hostname()` → `Dns.GetHostName()`
- [ ] `os.platform()` — same mapping as `process.platform`
- [ ] `os.arch()` → `RuntimeInformation.ProcessArchitecture.ToString().ToLower()`
- [ ] `os.homedir()` → `Environment.GetFolderPath(SpecialFolder.UserProfile)`
- [ ] `os.tmpdir()` → `Path.GetTempPath()`
- [ ] `os.totalmem()` → `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`
- [ ] `os.freemem()` — platform-specific
- [ ] `os.cpus()` → `Environment.ProcessorCount` (int)
- [ ] `os.EOL` property → `Environment.NewLine`

### 1d — console.timeLog
- [ ] `console.timeLog(label?)` — print elapsed ms without stopping the timer

### 1e — console output routing
- [ ] `ConsoleFunctionProvider.SetOutput(Action<string> stdout, Action<string> stderr)` — static delegates the host can set; defaults to `Console.WriteLine` / `Console.Error.WriteLine`

### 1f — Language completeness (trivial)
- [ ] `Array.prototype.reduceRight(fn, init?)` — same as `reduce` but right-to-left
- [ ] `Array.prototype.toSorted(fn?)` — non-mutating `sort`; returns a copy
- [ ] `Array.prototype.toReversed()` — non-mutating `reverse`; returns a copy
- [ ] `Array.prototype.toSpliced(start, del, ...items)` — non-mutating `splice`; returns a copy
- [ ] `Array.prototype.with(index, val)` — returns copy with one element replaced
- [ ] `Object.hasOwn(obj, key)` — ES2022; cleaner than `hasOwnProperty`
- [ ] `Object.is(a, b)` — strict equality handling `NaN` and `-0`
- [ ] `Object.seal(obj)` / `Object.isSealed(obj)` — complement to existing `freeze`/`isFrozen`
- [ ] `Object.groupBy(arr, fn)` — ES2024; groups into `{ key: [items] }`
- [ ] `Map.groupBy(arr, fn)` — ES2024; groups into a Map
- [ ] `AggregateError(errors, msg?)` — wraps multiple errors; needed by `Promise.any`
- [ ] `Error` `.cause` property — `new Error('msg', { cause: err })` (ES2022)
- [ ] `Number.prototype.toPrecision(digits?)` — significant-figure string
- [ ] `String.prototype.normalize(form?)` → `str.Normalize(NormalizationForm.*)`
- [ ] `String.prototype.codePointAt(pos)` — full Unicode code point
- [ ] `String.fromCodePoint(...codes)` → `char.ConvertFromUtf32()`

---

## Phase 2 — crypto module (half a day)

Create `CryptoFunctionProvider` with `[ScriptClass("crypto")]`.

- [ ] `crypto.randomUUID()` → `Guid.NewGuid().ToString()`
- [ ] `crypto.randomBytes(n)` → `RandomNumberGenerator.GetBytes(n)` returned as array of ints
- [ ] `crypto.getRandomValues(arr)` — fills a script array with cryptographically random ints
- [ ] `crypto.createHash(algo)` — returns hash object; `.update(data)`, `.digest(enc?)`
- [ ] `crypto.createHmac(algo, key)` — returns hmac object; `.update(data)`, `.digest(enc?)`
- [ ] Supported algorithms: `'sha256'`, `'sha512'`, `'sha1'`, `'md5'`

---

## Phase 3 — process lifecycle hooks (half a day)

Extend existing `ProcessFunctionProvider`.

- [ ] `process.on('exit', fn)` — register handler called just before exit
- [ ] `process.on('uncaughtException', fn)` — register handler for top-level exceptions
- [ ] `process.on('unhandledRejection', fn)` — register handler for unhandled Promise rejections
- [ ] Dispatch hooks from the VM's top-level exception handler and `process.exit`

---

## Phase 4 — assert module (half a day)

Create `AssertFunctionProvider` registered under `assert.*`.

- [ ] `assert(val, msg?)` / `assert.ok(val, msg?)` — throw if falsy
- [ ] `assert.equal(a, b, msg?)` — `==` comparison
- [ ] `assert.strictEqual(a, b, msg?)` — `===` comparison
- [ ] `assert.notEqual(a, b, msg?)` / `assert.notStrictEqual(a, b, msg?)`
- [ ] `assert.deepEqual(a, b, msg?)` — recursive value equality
- [ ] `assert.throws(fn, err?, msg?)` — assert fn throws
- [ ] `assert.doesNotThrow(fn, msg?)` — assert fn does not throw
- [ ] `assert.fail(msg?)` — unconditional failure

---

## Phase 5 — util module (half a day)

Create `UtilFunctionProvider` registered under `util.*`.

- [ ] `util.format(fmt, ...args)` — printf-style: `%s`, `%d`, `%i`, `%f`, `%o`, `%j`
- [ ] `util.inspect(val, opts?)` — pretty-print any value; handles cycles and functions
- [ ] `util.promisify(fn)` — wraps a `(err, result)` callback-style function as Promise-returning
- [ ] `util.deprecate(fn, msg)` — wraps a function; prints deprecation warning on first call

---

## Phase 6 — fs module (half a day)

Create `FsFunctionProvider` registered under `fs.*`. Synchronous-only to start.

- [ ] `fs.readFileSync(path, enc?)` → `File.ReadAllText()` / `File.ReadAllBytes()`
- [ ] `fs.writeFileSync(path, data, enc?)` → `File.WriteAllText()` / `File.WriteAllBytes()`
- [ ] `fs.appendFileSync(path, data, enc?)` → `File.AppendAllText()`
- [ ] `fs.existsSync(path)` → `File.Exists() || Directory.Exists()`
- [ ] `fs.mkdirSync(path, opts?)` → `Directory.CreateDirectory()`
- [ ] `fs.rmdirSync(path)` → `Directory.Delete()`
- [ ] `fs.unlinkSync(path)` → `File.Delete()`
- [ ] `fs.readdirSync(path)` → `Directory.GetFileSystemEntries()` → array of names
- [ ] `fs.renameSync(old, new)` → `File.Move()`
- [ ] `fs.statSync(path)` → object `{ size, isFile(), isDirectory(), mtime }`
- [ ] `fs.copyFileSync(src, dest)` → `File.Copy()`

---

## Phase 7 — RegExp constructor (half a day)

- [ ] Create `RegExpObject` wrapper backed by `System.Text.RegularExpressions.Regex`
- [ ] Register `new RegExp(pattern, flags?)` constructor (follows Date/Map/Set pattern)
- [ ] `.test(str)` → bool
- [ ] `.exec(str)` → match array or null
- [ ] `.source`, `.flags`, `.global`, `.ignoreCase`, `.multiline` properties

---

## Phase 8 — Buffer (1 day)

Create `BufferObject` backed by `byte[]`. Constructor registration follows Date/Map/Set pattern.

- [ ] `Buffer.from(str, enc?)` — encode string to bytes
- [ ] `Buffer.from(array)` — from array of byte ints
- [ ] `Buffer.alloc(size, fill?)` — zeroed (or filled) byte array
- [ ] `Buffer.allocUnsafe(size)` — uninitialized byte array
- [ ] `Buffer.isBuffer(val)` → bool
- [ ] `Buffer.concat(list)` — concatenate multiple Buffers
- [ ] `.toString(enc?)` — decode to string; default UTF-8
- [ ] `.length` property
- [ ] `.slice(start, end?)` → new Buffer (shared view)
- [ ] `.copy(target, targetStart?)` — copy bytes into another Buffer
- [ ] `.equals(other)` → bool
- [ ] `.readUInt8(offset)` / `.writeUInt8(val, offset)` and common numeric variants

---

## Phase 9 — EventEmitter (1 day)

Create `EventEmitterFunctionProvider`. Instances are script-side objects; no native object backing needed.

- [ ] Register `new EventEmitter()` constructor
- [ ] `.on(event, fn)` / `.once(event, fn)` — register listener
- [ ] `.off(event, fn)` / `.removeAllListeners(event?)` — unregister
- [ ] `.emit(event, ...args)` → bool (true if any listeners called)
- [ ] `.listeners(event)` → array of listener functions
- [ ] `.listenerCount(event)` → int
- [ ] `EventEmitter.defaultMaxListeners` property (default 10; warn if exceeded)

---

## Phase 10 — Timer functions (1–2 days)

Requires new engine infrastructure: a scheduled-callback queue alongside `MicroTaskQueue`.

- [ ] Add `TimerQueue` to VM — stores `(id, dueTime, interval, fn)` entries; driven by `engine.DrainTimers()`
- [ ] `setTimeout(fn, delay?)` → int handle id
- [ ] `clearTimeout(id)` — cancel pending callback
- [ ] `setInterval(fn, interval?)` → int handle id
- [ ] `clearInterval(id)` — cancel repeating callback
- [ ] `engine.DrainTimers()` public API — host calls this on each tick to fire due callbacks

---

## Phase 11 — Promise combinators + static constructors (1 day)

Extend the existing Promise implementation.

- [ ] `Promise.resolve(val)` — return an already-resolved Promise
- [ ] `Promise.reject(reason)` — return an already-rejected Promise
- [ ] `Promise.all(arr)` — resolve when all resolve; reject on first rejection
- [ ] `Promise.allSettled(arr)` — always resolve with `[{status, value/reason}]`
- [ ] `Promise.race(arr)` — settle with the first to settle
- [ ] `Promise.any(arr)` — resolve with first fulfillment; reject with `AggregateError` if all reject

---

## Phase 12 — fetch (1 day)

Create `FetchFunctionProvider`. `HttpClient` should be a singleton.

- [ ] `fetch(url, opts?)` — synchronous blocking implementation first; returns result object
- [ ] `opts`: `{ method, headers, body }`
- [ ] Response object: `.ok`, `.status`, `.statusText`, `.headers`
- [ ] `.text()` → string
- [ ] `.json()` → parsed object
- [ ] `.arrayBuffer()` → Buffer (requires Phase 8)
- [ ] Upgrade to async/Promise-returning once timer queue (Phase 10) exists

---

## Phase 13 — Sandboxing / permission model (1 day)

- [ ] Define `EnginePermissions` flags enum: `FileSystem`, `Network`, `ProcessSpawn`, `ProcessExit`, `EnvironmentVariables`
- [ ] Update `EngineFunctionLoader.RegisterFunctions(engine, permissions)` to accept permissions
- [ ] `fs` provider checks `FileSystem` permission before each operation
- [ ] `fetch` provider checks `Network` permission
- [ ] `child_process` provider checks `ProcessSpawn` permission
- [ ] `process.exit` checks `ProcessExit` permission
- [ ] `process.getenv` / `process.env` checks `EnvironmentVariables` permission
- [ ] All violations throw a `PermissionError` catchable by the script

---

## Phase 14 — Script timeout / resource limits (1 day)

- [ ] `engine.SetTimeout(TimeSpan)` — cancels execution after wall-clock time elapses
- [ ] `engine.SetInstructionLimit(long)` — cancels after N VM instructions
- [ ] Add instruction counter to VM dispatch loop; check against limit each instruction
- [ ] Both cancellation paths throw `ScriptTimeoutException` (catchable by host, not script)

---

## Phase 15 — child_process module (1 day)

Create `ChildProcessFunctionProvider` registered under `child_process.*`.

- [ ] `child_process.execSync(cmd, opts?)` → stdout string; throws on non-zero exit
- [ ] `child_process.spawnSync(cmd, args?, opts?)` → `{ stdout, stderr, status, signal }`
- [ ] `child_process.exec(cmd, cb)` — async; `cb(err, stdout, stderr)` (requires timer queue)
- [ ] `child_process.spawn(cmd, args?, opts?)` — async; returns process object with `.on('exit', fn)`

---

## Phase 16 — readline module (half a day)

Create `ReadlineFunctionProvider` registered under `readline.*`.

- [ ] `readline.createInterface({ input, output })` → rl object
- [ ] `rl.question(prompt, cb)` — print prompt, read one line, call `cb(answer)`
- [ ] `rl.close()`
- [ ] `rl.on('line', fn)` / `rl.on('close', fn)`

---

## Phase 17 — Module system (2–3 days)

Core engine change. Requires VM support for loading and caching compiled chunks from disk.

- [ ] Add chunk cache to engine keyed by resolved absolute path
- [ ] `require(path)` global — resolve path relative to `__dirname`, load if not cached, return `module.exports`
- [ ] `module` object per script — `module.exports`, `module.filename`, `module.loaded`
- [ ] `exports` alias → `module.exports`
- [ ] `__filename` global — absolute path of the current script
- [ ] `__dirname` global — directory of the current script
- [ ] Circular dependency handling — return partial exports if re-entered
- [ ] Support `require('./relative')`, `require('/absolute')`, `require('name')` (search path configurable)

---

## Phase 18 — Host object injection (2+ days)

Clean API for hosts to expose C# objects to scripts without hand-writing a provider.

- [ ] `engine.SetGlobal(name, obj)` — exposes a C# object; public members become script-accessible
- [ ] `[ScriptVisible]` attribute — opt-in for specific properties and methods
- [ ] `[ScriptWritable]` attribute — allow script-side assignment (read-only by default)
- [ ] Reflection-based dispatch or source-generated wrappers for method calls
- [ ] Support primitive return types, string, and `ScriptVar` directly

---

## Phase 19 — http server (2+ days)

Requires async I/O integration. Defer until timer queue (Phase 10) and Buffer (Phase 8) exist.

- [ ] `http.createServer(fn)` → server object; `fn(req, res)` called per request
- [ ] `server.listen(port, host?, cb?)`
- [ ] `server.close()`
- [ ] Request: `.method`, `.url`, `.headers`, `.on('data', fn)`, `.on('end', fn)`
- [ ] Response: `.writeHead(status, headers?)`, `.write(data)`, `.end(data?)`

---

## Phase 20 — stream, net, zlib, URL, TextEncoder (deferred / niche)

Defer until the foundational pieces (Buffer, EventEmitter, async I/O) are in place.

- [ ] `stream` — simplified Readable/Writable/Transform
- [ ] `net` — TCP client/server (`net.createServer`, `net.createConnection`)
- [ ] `zlib` — `gzipSync`, `gunzipSync`, `deflateSync`, `inflateSync` (needs Buffer)
- [ ] `URL` / `URLSearchParams` — constructor + property accessors backed by `System.Uri`
- [ ] `TextEncoder` / `TextDecoder` — UTF-8 encode/decode to/from Buffer
