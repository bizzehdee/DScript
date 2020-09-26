var MyClass = {
    doSomethingComplicated: function (x, y) {
        return Math.pow(--x, ++y) / 10.0;
    }
};

var inst = new MyClass();
var x = inst.doSomethingComplicated(10.0, 2.0);

console.log(x);

var to = typeof x;

var p = to == "number" ? "yes" : "no";
var testString = "For more information, see Chapter 3.4.5.1";
var myRegex = /see (chapter \d+(\.\d)*)/i;

var matches = testString.match(myRegex);
console.log(matches[1]);

console.log(p);

try {

    var x = 1;
    throw "this broke";
}
catch (ex) {
    var pns = "Exception message: " + ex;
    console.log(pns);
}

try {

    var x = 1;
}
catch (ex) {

}
finally {

}