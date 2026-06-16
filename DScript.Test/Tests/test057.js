// parseInt with radix, plus isNaN and isFinite

var dec = parseInt("42");          // 42
var hex = parseInt("ff", 16);      // 255
var bin = parseInt("1010", 2);     // 10
var prefixed = parseInt("0x1F");   // 31 (auto-detected hex)
var trailing = parseInt("123abc"); // 123 (stops at first non-digit)
var neg = parseInt("-10");         // -10

var nanFromString = isNaN("abc");  // true
var notNan = isNaN("42");          // false
var nanValue = isNaN(0 / 0);       // true

var finite = isFinite(100);        // true
var infinite = isFinite(1 / 0);    // false (Infinity)
var nanFinite = isFinite("xyz");   // false

result = dec == 42 && hex == 255 && bin == 10 && prefixed == 31 &&
         trailing == 123 && neg == -10 &&
         nanFromString && !notNan && nanValue &&
         finite && !infinite && !nanFinite;
