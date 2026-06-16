// bitwise NOT (~) and unary plus (+)

var n = 5;
var notN = ~n;             // ~5 = -6
var notZero = ~0;          // -1

var coerced = +"42";       // string -> 42
var coercedFloat = +"3.5"; // string -> 3.5
var passthrough = +7;      // 7

// unary plus inside an expression
var sum = 10 + (+"5");     // 15

result = notN == -6 && notZero == -1 &&
         coerced == 42 && coercedFloat == 3.5 &&
         passthrough == 7 && sum == 15;
