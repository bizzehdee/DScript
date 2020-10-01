const testStr = "For more information, see Chapter 3.4.5.1";

function MyClass() {
    this.doSomethingComplicated = function (x, y) {
        return this.doSomethingElse(x, y);
    };
    this.doSomethingElse = function (a, b) {
        return Math.pow(a, b) / 10.0;
    };
};

var inst = new MyClass();
var x = inst.doSomethingComplicated(10.0, 2.0);

console.log(x);

var to = typeof x;

var p = to == "number" ? "yes" : "no";
var testString = testStr;
var myRegex = /see (chapter \d+(\.\d)*)/i;

var matches = testString.match(myRegex);
console.log(matches[1]);

console.log(p);

try {

    var x = 1;
    var obj = { message: "turd", doStuff: function () { } };

    throw obj;
}
catch (ex) {
    var pns = "Exception message: " + ex.message;
    console.log(pns);
}

try {
    testStr = "123";
}
catch (ex) {
    console.log(ex);
}
finally {

}