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
