// calling a function with fewer/more arguments than declared

function pick(a, b) {
    if (b == undefined) {
        return a;
    }
    return b;
}

function first(a) {
    return a;
}

var fewer = pick(10);          // b is missing -> undefined -> returns a
var exact = pick(10, 20);      // returns b
var more = first(1, 2, 3);     // extra arguments are evaluated then ignored

result = fewer == 10 && exact == 20 && more == 1;
