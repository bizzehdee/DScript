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
- `if` / `else`, `while`, `for`, `switch` / `case` / `default`, `return`
- Ternary (`?:`) expressions
- `typeof`
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

***Arithmetic operators***
+, -, *, /, %, ++, --

***Assignment operators***
=, +=, -=, /=, %=

***Comparison operators***
<, >, <=, >=, ==, !=, ===, !==

***Boolean / bitwise operators***
!, &, |, ^, &&, ||, <<, >>, >>>

***Other operators***
?: (ternary), typeof, instanceof, new

Standard library (DScript.Extras)
---------------------------------

***Global functions***
- eval
- exec
- trace
- parseInt
- parseFloat
- charToInt

***Console***
- log
- error
- clear

***Math***
- abs, acos, asin, atan, atan2
- ceil, cos, cosh, exp, floor
- log, min, max, pow, random
- round, sin, sinh, sqrt, tan, tanh

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
- contains, remove, join

***Object***
- dump, clone

***Integer***
- parseInt, parseFloat, valueOf

***JSON***
- parse
- stringify
