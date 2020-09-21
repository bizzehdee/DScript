DScript
=======

Open sourced object oriented Javascript based scripting language implemented in C#.

***Example***

    var MyClass = {
        doSomethingComplicated: function (x, y) {
            return x * y / 10.0;
        }
    };

    var inst = new MyClass();
    var x = inst.doSomethingComplicated(1.0, 2.0);
    
    Console.WriteLine(x);

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
- ...more

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
