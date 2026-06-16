// for...in iterates an object's member names (and an array's indices)

var obj = { a: 1, b: 2, c: 3 };
var keys = "";
var total = 0;
for (var k in obj) {
    keys = keys + k;
    total = total + obj[k];
}

// for...in over an array yields its index keys
var arr = [10, 20, 30];
var idxSum = 0;
for (var i in arr) {
    idxSum = idxSum + arr[i];
}

// break works inside for...in
var cnt = 0;
for (var k2 in obj) {
    cnt = cnt + 1;
    if (cnt == 2) break;
}

result = keys == "abc" && total == 6 && idxSum == 60 && cnt == 2;
