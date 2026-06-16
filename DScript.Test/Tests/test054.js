// Higher-order array methods backed by the function-call API

var nums = [1, 2, 3, 4];

var doubled = nums.map(function(x) { return x * 2; });        // [2, 4, 6, 8]

var evens = nums.filter(function(x) { return x % 2 == 0; });  // [2, 4]

var sum = 0;
nums.forEach(function(x) { sum = sum + x; });                 // 10

var total = nums.reduce(function(acc, x) { return acc + x; }, 0); // 10

var desc = [1, 3, 2];
desc.sort(function(a, b) { return b - a; });                  // [3, 2, 1]

result = doubled.length == 4 && doubled[0] == 2 && doubled[3] == 8 &&
         evens.length == 2 && evens[0] == 2 && evens[1] == 4 &&
         sum == 10 && total == 10 &&
         desc[0] == 3 && desc[1] == 2 && desc[2] == 1;
