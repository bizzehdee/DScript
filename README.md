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

`Promise.resolve(value)` and `Promise.reject(reason)` are available as static
constructors. `.then(fn)` and `.catch(fn)` are chainable.

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

`eval`, `exec`, `trace`, `parseInt(str, radix?)`, `parseFloat`, `isNaN`,
`isFinite`, `charToInt`

**console**

`log`, `error`, `clear`

**Math**

`abs`, `acos`, `asin`, `atan`, `atan2`, `ceil`, `cos`, `cosh`, `exp`, `floor`,
`log`, `min`, `max`, `pow`, `random`, `randomInt`, `round`, `sin`, `sinh`,
`sqrt`, `tan`, `tanh`

Constants: `PI`, `E`, `SQRT2`, `SQRT1_2`, `LN2`, `LN10`, `LOG2E`, `LOG10E`

**String** (instance methods on string values)

`charAt`, `charCodeAt`, `fromCharCode`, `indexOf`, `lastIndexOf`,
`substring`, `substr`, `split`, `match`, `trim`, `concat`, `replace`,
`toUpperCase`, `toLowerCase`

**Array** (instance methods on array values)

`push`, `pop`, `shift`, `unshift`, `slice`, `indexOf`, `reverse`, `sort`,
`contains`, `remove`, `join`, `map`, `filter`, `forEach`, `reduce`

**Object**

`keys`, `hasOwnProperty`, `dump`, `clone`

**JSON**

`parse`, `stringify`

**Integer** (also available as globals)

`parseInt(str, radix?)`, `parseFloat`, `isNaN`, `isFinite`, `valueOf`

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
- Regular-expression literals (`/pattern/flags`)
- Exception handling (`try`/`catch`/`finally`/`throw`) with script stack traces
- Generators (`function*`, `yield`) — stackless for simple bodies, thread-based fallback
- `async`/`await` with `Promise`, `.then()`, `.catch()`, microtask queue
- Modules: `require()`, `export var/const/function/default`, `import { } from`, `import * as`
- Tail call optimisation (eligible `return f(...)` calls avoid growing the C# call stack)
- Bytecode compiler with peephole optimiser (constant folding, BinaryIntConst fusion, jump-chain collapse, dead-code elimination)
- Bytecode serialisation with source maps (`BytecodeSerializer.SaveWithSourceMap`)
- Engine state serialisation (save / restore all script variables)
- Step debugger with breakpoints (`IDebugger`)
- Language Server Protocol server with diagnostics, hover, completion, go-to-definition, signature help
