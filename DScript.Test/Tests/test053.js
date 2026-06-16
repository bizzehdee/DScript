// Array methods: push, pop, shift, unshift, indexOf, slice, reverse, sort

var a = [1, 2, 3];
a.push(4);                 // [1, 2, 3, 4]
var popped = a.pop();      // 4 -> [1, 2, 3]
a.unshift(0);              // [0, 1, 2, 3]
var shifted = a.shift();   // 0 -> [1, 2, 3]
var idx = a.indexOf(2);    // 1

var sl = a.slice(1, 3);    // [2, 3] (a is unchanged)

a.reverse();               // [3, 2, 1]

var b = [3, 1, 2];
b.sort();                  // [1, 2, 3]

result = a.length == 3 && a[0] == 3 && a[2] == 1 &&
         popped == 4 && shifted == 0 && idx == 1 &&
         sl.length == 2 && sl[0] == 2 && sl[1] == 3 &&
         b[0] == 1 && b[1] == 2 && b[2] == 3;
