var MyClass = {
    doSomethingComplicated: function (x, y) {
        return Math.pow(--x, ++y) / 10.0;
    }
};

var inst = new MyClass();
var x = inst.doSomethingComplicated(10.0, 2.0);

console.log(x);

var to = typeof x;

console.log(to == "number");