DScript
=======

Open sourced, object oriented, Javascript based, extendable scripting language implemented in C#.

DScript is distributed as two NuGet packages: **DScript** (the engine) and **DScript.Extras**
(an optional JS-style standard library — `console`, `Math`, `String`, `Array`, `JSON`, etc.).

***Example***

    function Animal(name) {
        this.name = name;
    }

    // methods placed on the constructor are shared by every instance
    Animal.speak = function () {
        return this.name + " makes a sound";
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
- Functions: named, anonymous, and nested; callable with fewer or more arguments than declared
- Arithmetic, comparison, bitwise and boolean operators
- `if` / `else`, `while`, `do` / `while`, `for`, `for...in`, `switch` / `case` / `default`, `return`
- `break` and `continue` within loops
- Ternary (`?:`) expressions
- `typeof`, `instanceof`, `in`, `delete`
- Regular-expression literals (`/pattern/flags`)
- Eval / Exec
- Basic exception handling (`try` / `catch` / `finally` / `throw`)
- Engine state serialization (save / restore)
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

Iteration
---------

C-style and `for...in` loops are both supported; `for...in` walks an object's
member names (and an array's index keys).

    var obj = { a: 1, b: 2, c: 3 };
    for (var key in obj) {
        console.log(key + " = " + obj[key]);
    }

    var nums = [1, 2, 3, 4];
    var doubled = nums.map(function (n) { return n * 2; });   // [2, 4, 6, 8]
    var evens   = nums.filter(function (n) { return n % 2 == 0; });

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
