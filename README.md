DScript
=======

Open sourced, object oriented, Javascript based, extendable scripting language implemented in C#.

***Example***

    var MyClass = {
        doSomethingComplicated: function (x, y) {
            return x * y / 10.0;
        }
    };

    var inst = new MyClass();
    var x = inst.doSomethingComplicated(1.0, 2.0);
    
    console.log(x);

***Supports***
- Variables
- Classes/Objects
- Class methods
- Global methods
- Class scope
- Global scope
- Method scope
- Arithmetic
- Eval/Exec
- Basic exception handling (try/catch/finally/throw)
- ...more

***Arithmetic operators***
++, --, +, -, *, /, %

***Comparison operators***
<, >, <=, >=, ==, !=

***Boolean operators***
!, &, |, ^, &&, ||

***Provides***
- Console I/O
  - log
  - error
  - clear
- Math library
  - abs
  - acos
  - asin
  - atan
  - atan2
  - ceil
  - cos
  - cosh
  - exp
  - floor
  - log
  - min
  - max
  - pow
  - random
  - round
  - sin
  - sinh
  - sqrt
  - tan
  - tanh
- Random number library
- JSON
  - parse
  - stringify
