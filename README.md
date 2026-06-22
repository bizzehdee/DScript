DScript
=======

Open sourced, object oriented, Javascript based, extendable scripting language implemented in C#.

DScript is distributed as two NuGet packages: **DScript** (the engine) and **DScript.Extras**
(an optional JS-style standard library — `console`, `Math`, `String`, `Array`, `JSON`, etc.).

Source is **compiled to bytecode once** and executed on a stack-based virtual machine, so
loops and function calls don't re-parse on every iteration. Compiled bytecode can also be
saved and re-run later. Functions are **lexically scoped closures**.

***Example***

    function Animal(name) {
        this.name = name;
    }

    // methods placed on the constructor are shared by every instance
    Animal.speak = function () {
        return `${this.name} makes a sound`;
    };

    var dog = new Animal("dog");

    console.log(dog.speak());           // dog makes a sound
    console.log(dog instanceof Animal); // 1

    var MyClass = {
        doSomethingComplicated: function (x, y) {
            return x * y / 10.0;
        }
    };

    var inst = new MyClass();
    console.log(inst.doSomethingComplicated(1.0, 2.0));

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

    // Read a value back out of the script's root scope
    var result = engine.Root.GetParameter("result").Int; // 42

***Exposing your own native functions***

    engine.AddNative("function add(a, b)", (var, userData) =>
    {
        var.ReturnVar.Int = var.GetParameter("a").Int + var.GetParameter("b").Int;
    }, null);

    engine.Execute("console.log(add(2, 3));"); // 5

***Calling script functions from C#***

    engine.Execute("function square(n) { return n * n; }");

    var square = engine.Root.GetParameter("square");
    var result = engine.CallFunction(square, null, new ScriptVar(9)).Int; // 81

`CallFunction(function, thisArg, args...)` invokes any script (or native) function
programmatically. It is also what powers the higher-order array methods
(`map` / `filter` / `forEach` / `reduce` and `sort` comparators).

***Compiling to bytecode and re-running it***

    // compile once... (compilation is engine-independent)
    var program = ScriptEngine.Compile("var answer = 6 * 7;");

    // ...run it (repeatedly, cheaply)
    engine.Run(program);

    // ...or persist the bytecode and run it later on another engine
    byte[] bytes = DScript.Vm.BytecodeSerializer.Save(program);
    // (save bytes to disk, send over the wire, etc.)

    var other = new ScriptEngine();
    var loader2 = new EngineFunctionLoader();
    loader2.RegisterFunctions(other);                 // register the same natives
    other.Run(DScript.Vm.BytecodeSerializer.Load(bytes));

Native functions are resolved by name at run time, so compiled bytecode does not embed
them — a loaded program just needs the host to register the same natives before running.

***Saving and restoring engine state***

    var state = engine.SerializeState();   // capture all variables/values
    // ... later, on a fresh engine with the same native functions registered ...
    engine.DeserializeState(state);         // restore them

(Native functions cannot be serialized; re-register them before restoring.)

***Supports***
- Variables (`var`) and constants (`const`)
- Classes / objects and object literals
- Constructor functions (`new`), prototype chains and `instanceof`
- Class methods and instance methods
- Global, class, and method scope
- Functions: named, anonymous, nested, and arrow functions (`=>`)
- Arithmetic, comparison, bitwise and boolean operators
- `if` / `else`, `while`, `do` / `while`, `for`, `for...in`, `switch` / `case` / `default`, `return`
- `break` and `continue` within loops
- Ternary (`?:`) expressions
- `typeof`, `instanceof`, `in`, `delete`
- Regular-expression literals (`/pattern/flags`)
- Template literals (`` `Hello, ${name}!` ``)
- Eval / Exec
- Exception handling (`try` / `catch` / `finally` / `throw`) with rich script stack traces
- Engine state serialization (save / restore)
- Step debugger with breakpoints (`IDebugger`)
- ...more

Classes and objects
--------------------

Objects can be created with an object literal, with a constructor function via `new`,
or by explicitly linking a `prototype`.

    // constructor function
    function Dog(name) {
        this.name = name;
    }

    // shared method defined on the constructor
    Dog.bark = function () {
        return this.name + " says woof";
    };

    var d1 = new Dog("rex");
    var d2 = new Dog("fido");

    d1.bark();   // "rex says woof"
    d2.bark();   // "fido says woof"  (each instance keeps its own fields)

***Inheritance***

    function Animal() { this.alive = 1; }
    function Dog()    { this.barks = 1; }

    Dog.prototype = new Animal();   // link the prototype chain

    var d = new Dog();
    d instanceof Dog;     // 1
    d instanceof Animal;  // 1  (the chain is walked)

Property reads fall back to the prototype chain, while assignments always create or
update an *own* property on the target object, so instances never share each other's
state. `new Ctor` may be written with or without parentheses, and a constructor that
returns an object uses that object as the result of the `new` expression.

Arrow functions
---------------

Arrow functions provide a concise syntax for function expressions. All three forms
are supported — no-parameter, single-parameter (no parentheses needed), and
multi-parameter — for both expression bodies (implicit return) and block bodies.

    // expression body — value is returned implicitly
    var double  = x => x * 2;
    var add     = (a, b) => a + b;
    var getZero = () => 0;

    double(5);    // 10
    add(3, 4);    // 7

    // block body — requires an explicit return
    var clamp = (val, lo, hi) => {
        if (val < lo) return lo;
        if (val > hi) return hi;
        return val;
    };

Arrow functions are closures and capture variables from the enclosing scope:

    var factor = 3;
    var triple = x => x * factor;
    triple(7);    // 21

They are particularly useful as callbacks to higher-order array methods:

    var nums    = [1, 2, 3, 4, 5];
    var doubled = nums.map(n => n * 2);         // [2, 4, 6, 8, 10]
    var evens   = nums.filter(n => n % 2 == 0); // [2, 4]
    var sum     = nums.reduce((acc, n) => acc + n, 0); // 15

Template literals
-----------------

Template literals are backtick-delimited strings that support multi-character escape
sequences and `${expression}` interpolation. Any expression — variable read, arithmetic,
function call — may appear inside `${}`.

    var name = "Alice";
    var age  = 30;

    console.log(`Hello, ${name}!`);             // Hello, Alice!
    console.log(`In 10 years you'll be ${age + 10}.`);

    function greet(n) { return `Hi, ${n}!`; }
    greet("Bob");   // "Hi, Bob!"

Escape sequences follow the same rules as regular strings (`\n`, `\t`, `\\`, etc.).
Use `\$` to include a literal `$` without triggering interpolation.

Iteration
---------

C-style and `for...in` loops are both supported; `for...in` walks an object's
member names (and an array's index keys).

    var obj = { a: 1, b: 2, c: 3 };
    for (var key in obj) {
        console.log(`${key} = ${obj[key]}`);
    }

    var nums = [1, 2, 3, 4];
    var doubled = nums.map(n => n * 2);
    var evens   = nums.filter(n => n % 2 == 0);

Switch statements
-----------------

`switch` matches a discriminant against `case` values with strict equality. `default`
may appear anywhere in the block. Unlike C, there is no fallthrough — each `case` body
is independent. `break` is accepted (and does nothing extra) for compatibility. `continue`
inside a `switch` that is itself inside a loop targets the enclosing loop, not the switch.

    switch (status) {
        case "ok":    return "all good";
        case "warn":  return "check logs";
        default:      return "unknown";
    }

Exception handling
------------------

`try` / `catch` / `finally` / `throw` work as in standard JavaScript. When an exception
propagates out of a script call, the thrown `JITException` or `ScriptException` carries
a `ScriptStackTrace` property — a list of `(Source, Line)` pairs — and its `ToString()`
includes those frames for easy diagnostics.

    try {
        throw "something went wrong";
    } catch (e) {
        console.log(e);
    } finally {
        // always runs
    }

From C#:

    try
    {
        engine.Run(program);
    }
    catch (JITException ex)
    {
        // ex.ScriptStackTrace — IReadOnlyList<(string Source, int Line)>
        // ex.ToString()       — formatted with "at <fn> (line N)" frames
        Console.Error.WriteLine(ex.ToString());
    }

Step debugger
-------------

Attach an `IDebugger` to pause execution at each source line, step over/into/out of
calls, and inspect locals. The debugger fires before each new source line is executed
and receives the current call stack with local variable snapshots.

    using DScript.Debugger;

    class MyDebugger : IDebugger
    {
        public DebugAction OnPause(DebugEvent ev)
        {
            var loc = ev.Location;
            Console.WriteLine($"Paused at {loc.Source}:{loc.Line}");

            foreach (var frame in ev.CallStack)
            {
                Console.WriteLine($"  in {frame.FunctionName}");
                foreach (var (name, val) in frame.Locals)
                    Console.WriteLine($"    {name} = {val}");
            }

            return DebugAction.StepIn;   // Continue / StepIn / StepOver / StepOut
        }
    }

    engine.AttachDebugger(new MyDebugger(), initialAction: DebugAction.StepIn);
    engine.AddBreakpoint("<main>", 5);   // break at line 5 of the main chunk
    engine.Run(program);
    engine.DetachDebugger();

***Arithmetic operators***
+, -, *, /, %, ++, --  (and unary +, -)

***Assignment operators***
=, +=, -=, *=, /=, %=, &=, |=, ^=, <<=, >>=, >>>=

***Comparison operators***
<, >, <=, >=, ==, !=, ===, !==

***Boolean / bitwise operators***
!, ~, &, |, ^, &&, ||, <<, >>, >>>

***Other operators***
?: (ternary), typeof, instanceof, in, delete, new

Standard library (DScript.Extras)
---------------------------------

***Global functions***
- eval
- exec
- trace
- parseInt (supports an optional radix)
- parseFloat
- isNaN, isFinite
- charToInt

***Console***
- log
- error
- clear

***Math***
- abs, acos, asin, atan, atan2
- ceil, cos, cosh, exp, floor
- log, min, max, pow
- random, randomInt (alias randInt)
- round, sin, sinh, sqrt, tan, tanh
- constants: PI, E, LOG2E, LOG10E

***String***
- length (property)
- indexOf, lastIndexOf
- substring, substr
- charAt, charCodeAt, fromCharCode
- split, match (regular expressions)
- trim, concat, replace
- toUpperCase, toLowerCase

***Array***
- length (property)
- push, pop, shift, unshift
- slice, indexOf, reverse, sort
- contains, remove, join
- map, filter, forEach, reduce

***Object***
- keys, hasOwnProperty
- dump, clone

***Integer***
- parseInt (supports an optional radix), parseFloat, valueOf

***JSON***
- parse
- stringify
