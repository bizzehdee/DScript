DScript
=======

Open-source, object-oriented, JavaScript-like scripting language implemented in C#.

Source is compiled to bytecode and executed on a stack-based virtual machine with a peephole optimiser. Lexically scoped closures, generators, async/await, and a CommonJS/ES module system are built in. An optional standard library (`DScript.Extras`) provides a Node.js-style API surface.

Installation
------------

    dotnet add package DScript
    dotnet add package DScript.Extras   # optional standard library

Quick example
-------------

```csharp
using DScript;
using DScript.Extras;

var engine = new ScriptEngine();
new EngineFunctionLoader().RegisterFunctions(engine);

engine.Execute(@"
    function greet(name) {
        return `Hello, ${name}!`;
    }
");

var fn = engine.Root.GetParameter("greet");
var result = engine.CallFunction(fn, null, new ScriptVar("world")).String;
// result == "Hello, world!"
```

```js
// The same engine, from script side
var nums = [1, 2, 3, 4, 5];
var evens = nums.filter(n => n % 2 === 0);   // [2, 4]
var sum   = nums.reduce((a, n) => a + n, 0); // 15

async function load() {
    var data = await Promise.resolve({ ok: true });
    return data.ok;
}
```

Documentation
-------------

Full documentation is on the [DScript Wiki](https://github.com/bizzehdee/DScript/wiki):

| Topic | Page |
|---|---|
| ScriptEngine API, compiling, calling functions, state serialisation | [Engine](https://github.com/bizzehdee/DScript/wiki/Engine) |
| Language reference — syntax, types, operators, classes, generators, async | [Language](https://github.com/bizzehdee/DScript/wiki/Language) |
| Standard library (console, Math, fs, fetch, crypto, …) | [Standard Library](https://github.com/bizzehdee/DScript/wiki/Standard-Library) |
| CommonJS `require` and ES module `import` | [Modules](https://github.com/bizzehdee/DScript/wiki/Modules) |
| Wall-clock and instruction-count limits | [Resource Limits](https://github.com/bizzehdee/DScript/wiki/Resource-Limits) |
| `EnginePermissions` sandbox flags | [Permissions](https://github.com/bizzehdee/DScript/wiki/Permissions) |
| Exposing C# objects to scripts with `[ScriptVisible]` | [Host Objects](https://github.com/bizzehdee/DScript/wiki/Host-Objects) |
| Step debugger and `IDebugger` interface | [Debugger](https://github.com/bizzehdee/DScript/wiki/Debugger) |
| Bytecode serialisation and source maps | [Bytecode](https://github.com/bizzehdee/DScript/wiki/Bytecode) |
| Interactive REPL | [REPL](https://github.com/bizzehdee/DScript/wiki/REPL) |

Building
--------

    dotnet build --configuration Release

Both `net8.0` and `net10.0` targets must build cleanly. Run the test suite with:

    dotnet test DScript.Test --configuration Release

Licence
-------

MIT
