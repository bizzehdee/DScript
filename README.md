DScript
=======

Open-source, object-oriented, JavaScript-like scripting language implemented in C#.

DScript is distributed as two NuGet packages: **DScript** (the engine) and **DScript.Extras**
(an optional JS-style standard library — `console`, `Math`, `String`, `Array`, `JSON`, etc.).

Source is **compiled to bytecode once** and executed on a stack-based virtual machine, so
loops and function calls don't re-parse on every iteration. A peephole optimiser folds
constants, fuses binary operations with inline integers, collapses jump chains, and upgrades
eligible tail calls automatically. Compiled bytecode can be saved to disk with an embedded
source map and reloaded later. Functions are **lexically scoped closures**.

---

Quick example
-------------

    function Animal(name) {
        this.name = name;
    }

    Animal.speak = function () {
        return `${this.name} makes a sound`;
    };

    var dog = new Animal("dog");

    console.log(dog.speak());           // dog makes a sound
    console.log(dog instanceof Animal); // 1

Installation
------------

    dotnet add package DScript
    dotnet add package DScript.Extras   # optional standard library

Using DScript from C#
---------------------

    using DScript;
    using DScript.Extras;

    var engine = new ScriptEngine();

    // Register the built-in JS-style library (console, Math, String, Array, ...)
    var loader = new EngineFunctionLoader();
    loader.RegisterFunctions(engine);

    engine.Execute("var result = 6 * 7;");

    var result = engine.Root.GetParameter("result").Int; // 42

**Exposing your own native functions**

    engine.AddNative("function add(a, b)", (scope, userData) =>
    {
        scope.ReturnVar.Int = scope.GetParameter("a").Int + scope.GetParameter("b").Int;
    }, null);

    engine.Execute("console.log(add(2, 3));"); // 5

**Calling script functions from C#**

    engine.Execute("function square(n) { return n * n; }");

    var square = engine.Root.GetParameter("square");
    var result = engine.CallFunction(square, null, new ScriptVar(9)).Int; // 81

`CallFunction(function, thisArg, args...)` invokes any script or native function
programmatically. It is also what powers the higher-order array methods
(`map`, `filter`, `forEach`, `reduce`, and `sort` comparators).

**Compiling once and running many times**

    // Compile is engine-independent — share chunks across engine instances.
    var program = ScriptEngine.Compile("var answer = 6 * 7;");

    engine.Run(program); // cheap

    // Persist bytecode to disk (includes source map).
    DScript.Vm.BytecodeSerializer.SaveWithSourceMap(program, "program.dsc");

    // Reload and run on a fresh engine.
    var other = new ScriptEngine();
    loader.RegisterFunctions(other);
    other.Run(DScript.Vm.BytecodeSerializer.LoadWithSourceMap("program.dsc"));

Native functions are resolved by name at run time, so serialised bytecode does not embed
them — a loaded program just needs the host to register the same natives before running.

**Saving and restoring engine state**

    var state = engine.SerializeState();
    // ... later, on a fresh engine with the same natives registered ...
    engine.DeserializeState(state);

---

Language features
-----------------

**Variables and scoping**

    var x = 10;       // function-scoped
    let y = 20;       // block-scoped
    const PI = 3.14;  // block-scoped, immutable binding

`let` and `const` are block-scoped and do not hoist.

**Classes and objects**

Objects can be created with an object literal, with a constructor function via `new`,
or by linking a `prototype` explicitly.

    function Dog(name) {
        this.name = name;
    }

    Dog.bark = function () {
        return `${this.name} says woof`;
    };

    var d1 = new Dog("rex");
    d1.bark();   // "rex says woof"

*Shorthand and computed property names:*

    var key = "score";
    var name = "alice";

    var obj = {
        name,           // shorthand: { name: name }
        [key]: 100,     // computed:  { score: 100 }
    };

*Inheritance via prototype chain:*

    function Animal() { this.alive = 1; }
    function Dog()    { this.barks = 1; }

    Dog.prototype = new Animal();

    var d = new Dog();
    d instanceof Dog;     // 1
    d instanceof Animal;  // 1

**Arrow functions**

All three forms are supported for both expression bodies (implicit return) and
block bodies.

    var double  = x => x * 2;
    var add     = (a, b) => a + b;
    var getZero = () => 0;

    var clamp = (val, lo, hi) => {
        if (val < lo) return lo;
        if (val > hi) return hi;
        return val;
    };

Arrow functions close over the enclosing scope and are particularly useful as
callbacks:

    var nums    = [1, 2, 3, 4, 5];
    var doubled = nums.map(n => n * 2);          // [2, 4, 6, 8, 10]
    var evens   = nums.filter(n => n % 2 == 0);  // [2, 4]
    var sum     = nums.reduce((acc, n) => acc + n, 0); // 15

**Default parameters**

    function greet(name, greeting = "Hello") {
        return `${greeting}, ${name}!`;
    }

    greet("Alice");          // "Hello, Alice!"
    greet("Bob", "Hi");      // "Hi, Bob!"

**Destructuring**

*Array destructuring:*

    var [a, b, c] = [1, 2, 3];
    var [first, ...rest] = [10, 20, 30];  // rest = [20, 30]

*Object destructuring:*

    var { x, y } = { x: 1, y: 2 };
    var { name: alias, score = 0 } = { name: "alice" };  // alias="alice", score=0

**Spread and rest**

    // spread in function calls
    function sum(a, b, c) { return a + b + c; }
    var args = [1, 2, 3];
    sum(...args);   // 6

    // spread in array literals
    var a = [1, 2];
    var b = [3, 4];
    var c = [...a, ...b, 5];  // [1, 2, 3, 4, 5]

    // rest parameters
    function first(head, ...tail) { return head; }
    first(1, 2, 3);   // 1, tail = [2, 3]

**Template literals**

    var name = "Alice";
    var age  = 30;

    console.log(`Hello, ${name}!`);
    console.log(`In 10 years you'll be ${age + 10}.`);

Any expression may appear inside `${}`. Use `\$` for a literal dollar sign.

**Nullish coalescing and optional chaining**

    var x = null;
    var y = x ?? "default";       // "default"

    var obj = { a: { b: 42 } };
    var val = obj?.a?.b;           // 42
    var missing = obj?.z?.w;       // undefined (no throw)
    var result = obj?.fn?.(1, 2);  // undefined if fn is absent

**Iteration**

    // for...of works with arrays, generators, and any object with a .next() method
    for (var x of [1, 2, 3]) {
        console.log(x);
    }

    // for...in walks enumerable property names
    var obj = { a: 1, b: 2 };
    for (var key in obj) {
        console.log(`${key} = ${obj[key]}`);
    }

**Switch**

`default` may appear anywhere in the block. There is no fallthrough — each `case`
body is independent. `continue` inside a `switch` within a loop targets the
enclosing loop, not the switch.

    switch (status) {
        case "ok":   return "all good";
        case "warn": return "check logs";
        default:     return "unknown";
    }

**Exception handling**

    try {
        throw { code: 404, message: "not found" };
    } catch (e) {
        console.log(e.message);
    } finally {
        // always runs
    }

From C#, caught exceptions carry a script stack trace:

    try
    {
        engine.Run(program);
    }
    catch (JITException ex)
    {
        // ex.ScriptStackTrace — IReadOnlyList<(string Source, int Line)>
        Console.Error.WriteLine(ex.ToString());
    }

---

Generators
----------

`function*` declarations and `yield` expressions are fully supported.
Generators that contain no `try`/`catch` blocks use a stackless state-machine
execution path — no OS thread is created per invocation.

    function* range(start, end) {
        var i = start;
        while (i < end) {
            yield i;
            i++;
        }
    }

    for (var n of range(0, 5)) {
        console.log(n);   // 0 1 2 3 4
    }

    // .next() protocol
    var gen = range(0, 3);
    gen.next();  // { value: 0, done: false }
    gen.next();  // { value: 1, done: false }
    gen.next();  // { value: 2, done: false }
    gen.next();  // { value: undefined, done: true }

---

Async / await
-------------

`async function` declarations return a `Promise` that resolves with the function's
return value. `await` suspends the function until the awaited `Promise` settles.
Call `engine.DrainMicroTasks()` after `Run()` to flush the microtask queue.

    async function fetchData() {
        var raw = await Promise.resolve(42);
        return raw * 2;
    }

    var r = 0;
    fetchData().then(function(v) { r = v; });
    // engine.DrainMicroTasks() from C# to settle the chain

All six Promise combinators are available:

| Method | Behaviour |
|---|---|
| `Promise.resolve(val)` | Already-resolved promise |
| `Promise.reject(reason)` | Already-rejected promise |
| `Promise.all(arr)` | Resolves when all resolve; rejects on first rejection |
| `Promise.allSettled(arr)` | Always resolves with `[{status, value/reason}]` |
| `Promise.race(arr)` | Settles with the first to settle |
| `Promise.any(arr)` | Resolves with first fulfillment; rejects with `AggregateError` if all reject |

---

Modules
-------

DScript supports CommonJS-style `require` / `export` and ES-module `import` syntax.
Supply a module loader callback to resolve module paths:

    engine.ModuleLoader = (path, fromPath) =>
        File.ReadAllText(Path.Combine(baseDir, path + ".ds"));

**CommonJS (require / export)**

    // math.ds
    export function add(a, b) { return a + b; }
    export const PI = 3.14159;

    // main.ds
    var math = require("math");
    console.log(math.add(2, 3));  // 5
    console.log(math.PI);         // 3.14159

**ES module import**

    import { add, PI } from "math";
    import * as math from "math";
    import defaultExport from "utils";

Module exports are cached — re-requiring the same path returns the cached
object without re-executing the module body. Circular `require()` is handled
gracefully via pre-seeding the cache before execution.

Each module environment exposes `module`, `exports`, `__filename`, and `__dirname`:

    // Inside any required module:
    console.log(__filename);      // absolute path of this file
    console.log(__dirname);       // directory of this file
    module.exports = { greet };   // replace the exports object

---

Resource limits
---------------

Prevent runaway scripts from hanging the host process:

    // Cancel after 500 ms of wall-clock time
    engine.SetTimeout(TimeSpan.FromMilliseconds(500));

    // Cancel after 10 million VM instructions
    engine.SetInstructionLimit(10_000_000);

Both limits throw `ScriptTimeoutException` (a plain .NET exception, not derived
from `JITException`) so script-level `try/catch` blocks cannot intercept it:

    try
    {
        engine.Run(program);
    }
    catch (ScriptTimeoutException ex)
    {
        Console.Error.WriteLine($"Script killed: {ex.Message}");
    }

---

Sandboxing / permissions
------------------------

Restrict which system resources a script can access by passing `EnginePermissions`
flags when registering the standard library:

    // Allow everything (default)
    loader.RegisterFunctions(engine);

    // Deny all system access
    loader.RegisterFunctions(engine, EnginePermissions.None);

    // Allow only file and network access
    loader.RegisterFunctions(engine, EnginePermissions.FileSystem | EnginePermissions.Network);

Available flags:

| Flag | Guards |
|---|---|
| `FileSystem` | All `fs.*` operations |
| `Network` | `fetch()`, `http.*`, `net.*` |
| `ProcessSpawn` | `child_process.*` |
| `ProcessExit` | `process.exit()` |
| `EnvironmentVariables` | `process.env`, `process.getenv` |

Violations throw `PermissionException`, which scripts can catch.

---

Host object injection
---------------------

Expose a C# object to scripts without writing a custom provider. Mark members
with `[ScriptVisible]` (read access) and optionally `[ScriptWritable]` (write access),
then call `engine.SetGlobal`:

    public class Config
    {
        [ScriptVisible] public string Env { get; set; } = "prod";
        [ScriptVisible][ScriptWritable] public int MaxRetries { get; set; } = 3;

        [ScriptVisible]
        public string GetVersion() => "1.0.0";
    }

    engine.SetGlobal("config", new Config());

    // In script:
    console.log(config.Env);         // "prod"
    console.log(config.GetVersion()); // "1.0.0"
    config.MaxRetries = 5;           // writable

---

Step debugger
-------------

Attach an `IDebugger` to pause execution at each new source line, step over or
into calls, inspect locals, and set breakpoints.

    using DScript.Debugger;

    class MyDebugger : IDebugger
    {
        public DebugAction OnPause(DebugEvent ev)
        {
            var loc = ev.Location;
            Console.WriteLine($"Paused at {loc.Source}:{loc.Line}:{loc.Col}");

            foreach (var frame in ev.CallStack)
            {
                Console.WriteLine($"  in {frame.FunctionName}");
                foreach (var (name, val) in frame.Locals)
                    Console.WriteLine($"    {name} = {val}");
            }

            return DebugAction.StepIn;
        }
    }

    engine.AttachDebugger(new MyDebugger(), initialAction: DebugAction.StepIn);
    engine.AddBreakpoint("<main>", 5);
    engine.Run(program);
    engine.DetachDebugger();

---

Language Server (DScript.LanguageServer)
----------------------------------------

A standalone LSP server ships in the `DScript.LanguageServer` project. It speaks
JSON-RPC over stdio and integrates with any LSP-capable editor. The companion
VS Code extension lives in `vscode-dscript/`.

Supported LSP capabilities:
- **Diagnostics** — compile errors reported on `textDocument/didOpen` and `didChange`
- **Hover** — variable type and value at the cursor position
- **Go to definition** — jump to a variable or function declaration
- **Completion** — identifier suggestions from the current scope
- **Signature help** — parameter hints while typing a function call

Run the server directly:

    dotnet run --project DScript.LanguageServer

---

Operator reference
------------------

| Category | Operators |
|---|---|
| Arithmetic | `+` `-` `*` `/` `%` `++` `--` (unary `+` `-`) |
| Assignment | `=` `+=` `-=` `*=` `/=` `%=` `&=` `\|=` `^=` `<<=` `>>=` `>>>=` |
| Comparison | `<` `>` `<=` `>=` `==` `!=` `===` `!==` |
| Boolean / bitwise | `!` `~` `&` `\|` `^` `&&` `\|\|` `<<` `>>` `>>>` |
| Nullish / optional | `??` `?.` |
| Other | `?:` `typeof` `instanceof` `in` `delete` `new` `...` |

---

Standard library (DScript.Extras)
----------------------------------

Register with `new EngineFunctionLoader().RegisterFunctions(engine)`.

**Global functions**

`eval`, `exec`, `trace`, `parseInt(str, radix?)`, `parseFloat`, `isNaN`, `isFinite`,
`encodeURIComponent`, `decodeURIComponent`, `encodeURI`, `decodeURI`,
`btoa`, `atob`, `charToInt`, `structuredClone`, `queueMicrotask`

**console**

`log`, `error`, `warn`, `info`, `clear`, `time(label?)`, `timeEnd(label?)`, `timeLog(label?)`

Output routing: `ConsoleFunctionProvider.SetOutput(Action<string> stdout, Action<string> stderr)`

**Math**

`abs`, `acos`, `asin`, `atan`, `atan2`, `ceil`, `cos`, `cosh`, `exp`, `floor`,
`log`, `log2`, `log10`, `min`, `max`, `pow`, `random`, `round`, `sign`, `sin`, `sinh`,
`sqrt`, `tan`, `tanh`, `trunc`, `cbrt`, `hypot`, `clz32`, `fround`, `imul`

Constants: `PI`, `E`, `SQRT2`, `SQRT1_2`, `LN2`, `LN10`, `LOG2E`, `LOG10E`

**String** (instance methods on string values)

`charAt`, `charCodeAt`, `codePointAt`, `fromCharCode`, `fromCodePoint`,
`indexOf`, `lastIndexOf`, `includes`, `startsWith`, `endsWith`,
`substring`, `slice`, `split`, `match`, `matchAll`, `trim`, `trimStart`, `trimEnd`,
`padStart`, `padEnd`, `repeat`, `concat`, `replace`, `replaceAll`,
`toUpperCase`, `toLowerCase`, `normalize`, `at`

**Array** (instance methods on array values)

`push`, `pop`, `shift`, `unshift`, `splice`, `slice`, `indexOf`, `lastIndexOf`,
`includes`, `find`, `findIndex`, `reverse`, `sort`, `flat`, `flatMap`,
`join`, `map`, `filter`, `forEach`, `reduce`, `reduceRight`,
`every`, `some`, `fill`, `copyWithin`, `keys`, `values`, `entries`, `at`,
`toSorted`, `toReversed`, `toSpliced`, `with`

`Array.from`, `Array.isArray`, `Array.of`

**Object**

`keys`, `values`, `entries`, `assign`, `create`, `freeze`, `isFrozen`,
`seal`, `isSealed`, `hasOwn`, `is`, `groupBy`, `fromEntries`,
`hasOwnProperty`, `dump`, `clone`

**Number**

`isInteger`, `isFinite`, `isNaN`, `isSafeInteger`, `parseInt`, `parseFloat`,
`toFixed(digits?)`, `toPrecision(digits?)`, `toExponential(digits?)`

Constants: `MAX_VALUE`, `MIN_VALUE`, `MAX_SAFE_INTEGER`, `MIN_SAFE_INTEGER`,
`POSITIVE_INFINITY`, `NEGATIVE_INFINITY`, `NaN`, `EPSILON`

**JSON**

`parse`, `stringify`

**Map**

`new Map()`, `.set`, `.get`, `.has`, `.delete`, `.clear`, `.forEach`, `.keys`, `.values`, `.entries`, `.size`

`Map.groupBy(arr, fn)`

**Set**

`new Set()`, `.add`, `.has`, `.delete`, `.clear`, `.forEach`, `.keys`, `.values`, `.entries`, `.size`

**Date**

`new Date()`, `.getFullYear`, `.getMonth`, `.getDate`, `.getDay`,
`.getHours`, `.getMinutes`, `.getSeconds`, `.getMilliseconds`, `.getTime`,
`.toISOString`, `.toLocaleDateString`, `.toString`

`Date.now()`

**RegExp**

`new RegExp(pattern, flags?)`, `.test(str)`, `.exec(str)`

Properties: `.source`, `.flags`, `.global`, `.ignoreCase`, `.multiline`

**Buffer**

`Buffer.from(str, enc?)`, `Buffer.from(array)`, `Buffer.alloc(size, fill?)`,
`Buffer.allocUnsafe(size)`, `Buffer.isBuffer(val)`, `Buffer.concat(list)`

Instance: `.toString(enc?)`, `.length`, `.slice(start, end?)`, `.copy(target, targetStart?)`,
`.equals(other)`, `.readUInt8(offset)`, `.writeUInt8(val, offset)`, and common numeric variants

**EventEmitter**

`new EventEmitter()`, `.on(event, fn)`, `.once(event, fn)`, `.off(event, fn)`,
`.removeAllListeners(event?)`, `.emit(event, ...args)`, `.listeners(event)`, `.listenerCount(event)`

**Timers**

`setTimeout(fn, delay?)`, `clearTimeout(id)`, `setInterval(fn, interval?)`, `clearInterval(id)`

Flush pending timers from C# with `engine.DrainTimers()`.

**process**

`argv`, `env`, `getenv(name)`, `exit(code?)`, `cwd()`, `platform`, `version`

Lifecycle hooks: `process.on('exit', fn)`, `process.on('uncaughtException', fn)`, `process.on('unhandledRejection', fn)`

**assert**

`assert(val, msg?)` / `assert.ok`, `assert.equal`, `assert.strictEqual`,
`assert.notEqual`, `assert.notStrictEqual`, `assert.deepEqual`,
`assert.throws`, `assert.doesNotThrow`, `assert.fail`

**util**

`util.format(fmt, ...args)`, `util.inspect(val, opts?)`,
`util.promisify(fn)`, `util.deprecate(fn, msg)`

**path**

`join(...parts)`, `resolve(...parts)`, `dirname(p)`, `basename(p, ext?)`,
`extname(p)`, `isAbsolute(p)`, `normalize(p)`, `sep`

**os**

`hostname()`, `platform()`, `arch()`, `homedir()`, `tmpdir()`,
`totalmem()`, `freemem()`, `cpus()`, `EOL`

**fs** (synchronous)

`readFileSync(path, enc?)`, `writeFileSync(path, data, enc?)`, `appendFileSync(path, data, enc?)`,
`existsSync(path)`, `mkdirSync(path, opts?)`, `rmdirSync(path)`, `unlinkSync(path)`,
`readdirSync(path)`, `renameSync(old, new)`, `statSync(path)`, `copyFileSync(src, dest)`

Requires `EnginePermissions.FileSystem`.

**crypto**

`randomUUID()`, `randomBytes(n)`, `getRandomValues(arr)`,
`createHash(algo)` → `{ update(data), digest(enc?) }`,
`createHmac(algo, key)` → `{ update(data), digest(enc?) }`

Supported algorithms: `sha256`, `sha512`, `sha1`, `md5`

**fetch**

`fetch(url, opts?)` — synchronous HTTP client. `opts`: `{ method, headers, body }`.

Response: `.ok`, `.status`, `.statusText`, `.headers`, `.text()`, `.json()`, `.arrayBuffer()`

Requires `EnginePermissions.Network`.

**http**

`http.createServer(fn)` — `fn(req, res)` is called per request.

`server.listen(port, host?, cb?)`, `server.close(cb?)`

Request: `.method`, `.url`, `.headers`, `.on('data', fn)`, `.on('end', fn)`

Response: `.writeHead(status, headers?)`, `.write(data)`, `.end(data?)`

Requires `EnginePermissions.Network`.

**child_process**

`execSync(cmd, opts?)` — returns stdout; throws on non-zero exit.

`spawnSync(cmd, args?, opts?)` → `{ stdout, stderr, status, signal }`

`exec(cmd, cb)` — runs and calls `cb(err, stdout, stderr)`.

`spawn(cmd, args?, opts?)` — returns process object; `.on('exit', fn)`, `.on('data', fn)`

Requires `EnginePermissions.ProcessSpawn`.

**readline**

`readline.createInterface({ input, output })` → rl object

`rl.question(prompt, cb)`, `rl.close()`, `rl.on('line', fn)`, `rl.on('close', fn)`

**net**

`net.createServer(connectionListener?)` — TCP server; handler receives a socket object.

`net.createConnection(port, host?, connectListener?)` — TCP client.

Socket: `.write(data)`, `.end(data?)`, `.read()`, `.destroy()`, `.on(event, fn)`,
`.remoteAddress`, `.remotePort`

Requires `EnginePermissions.Network`.

**stream**

`new stream.Readable()` — `.push(chunk)`, `.read()`, `.pipe(dest)`, `.on('data', fn)`, `.on('end', fn)`

`new stream.Writable()` — `.write(chunk)`, `.end(chunk?)`, `.on('finish', fn)`, `.getBuffer()`

`new stream.Transform()` — combined readable + writable; write feeds the readable side.

**zlib**

`zlib.gzipSync(input)`, `zlib.gunzipSync(input)`,
`zlib.deflateSync(input)`, `zlib.inflateSync(input)`

Input may be a Buffer or a string (UTF-8 encoded). Returns a Buffer.

**URL / URLSearchParams**

    var u = new URL('https://example.com/path?a=1#frag');
    u.hostname;           // "example.com"
    u.pathname;           // "/path"
    u.searchParams.get('a');  // "1"

    var sp = new URLSearchParams('x=1&y=2');
    sp.set('x', '99');
    sp.toString();        // "x=99&y=2"

**TextEncoder / TextDecoder**

    var enc = new TextEncoder();          // encoding: 'utf-8'
    var buf = enc.encode('hello');        // Buffer

    var dec = new TextDecoder('utf-8');
    dec.decode(buf);                      // 'hello'

---

Feature summary
---------------

- Variables: `var` (function-scoped), `let` and `const` (block-scoped)
- Classes, object literals, constructor functions (`new`), prototype chains, `instanceof`
- Shorthand property names and computed property keys (`[expr]: value`)
- Named, anonymous, nested, and arrow functions
- Default parameter values
- Array and object destructuring with defaults and aliases
- Spread (`...arr`) in calls and array literals; rest parameters (`...rest`)
- Template literals with `${expression}` interpolation
- Nullish coalescing (`??`) and optional chaining (`?.`)
- `if`/`else`, `while`, `do`/`while`, `for`, `for...in`, `for...of`, `switch`/`case`/`default`
- `break` and `continue` (with `continue` crossing `switch` to target the enclosing loop)
- Ternary (`?:`) expressions
- `typeof`, `instanceof`, `in`, `delete`
- Regular-expression literals (`/pattern/flags`) and `new RegExp(pattern, flags?)`
- Exception handling (`try`/`catch`/`finally`/`throw`) with script stack traces
- Generators (`function*`, `yield`) — stackless for simple bodies, thread-based fallback
- `async`/`await` with `Promise`, `.then()`, `.catch()`, microtask queue, all six combinators
- Modules: `require()`, `export var/const/function/default`, `import { } from`, `import * as`, `module`/`exports`/`__filename`/`__dirname`
- Tail call optimisation (eligible `return f(...)` calls avoid growing the C# call stack)
- Bytecode compiler with peephole optimiser (constant folding, BinaryIntConst fusion, jump-chain collapse, dead-code elimination)
- Bytecode serialisation with source maps (`BytecodeSerializer.SaveWithSourceMap`)
- Engine state serialisation (save / restore all script variables)
- Resource limits: wall-clock timeout and instruction count limit (`ScriptTimeoutException`)
- Permission sandbox: `EnginePermissions` flags gate filesystem, network, process, and environment access
- Host object injection: `engine.SetGlobal(name, obj)` with `[ScriptVisible]` / `[ScriptWritable]`
- Step debugger with breakpoints (`IDebugger`)
- Language Server Protocol server with diagnostics, hover, completion, go-to-definition, signature help
