// `new Ctor` without parentheses still runs the constructor

function Thing() {
    this.x = 42;
}

var t = new Thing;

result = t.x == 42;
