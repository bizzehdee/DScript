var MyClass = {
    doSomethingComplicated: function (x, y) {
        return Math.pow(x, y) / 10.0;
    }
};

var inst = new MyClass();
var x = inst.doSomethingComplicated(1.0, 2.0);

++x;

console.log(x);