DScript
=======

Open sourced object oriented scripting language implemented in C#.

***Example***

    class MyClass
    {
        function doSomethingComplicated(x, y)
        {
            return x * y / 10;
        }
    }
    
    var inst = new MyClass();
    var x = inst.doSomethingComplicated(1, 2);
    
    Console.WriteLine(x);

***Supports***
- Variables
- Classes
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
- Math library
- Random number library
