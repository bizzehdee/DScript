// in / delete operators, Object.keys and hasOwnProperty

var obj = { a: 1, b: 2 };

// `in` operator
var hasA = "a" in obj;          // true
var hasZ = "z" in obj;          // false

// Object.keys (captured before any deletion)
var keys = Object.keys(obj);    // ["a", "b"]

// hasOwnProperty
var ownA = obj.hasOwnProperty("a");   // true
var ownZ = obj.hasOwnProperty("z");   // false

// delete
delete obj.a;
var afterDelete = "a" in obj;   // false

// `in` with array indices
var arr = [10, 20];
var has0 = 0 in arr;            // true
var has5 = 5 in arr;            // false

result = hasA && !hasZ &&
         keys.length == 2 && keys[0] == "a" && keys[1] == "b" &&
         ownA && !ownZ &&
         !afterDelete &&
         has0 && !has5;
