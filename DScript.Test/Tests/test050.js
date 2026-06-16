// break and continue in for, while and do/while loops (including nesting)

// break in a for loop
var s1 = 0;
for (var i = 0; i < 10; i = i + 1) {
    if (i == 5) break;
    s1 = s1 + i;            // 0+1+2+3+4
}

// continue in a for loop (skip odd numbers)
var s2 = 0;
for (var j = 0; j < 10; j = j + 1) {
    if (j % 2 == 1) continue;
    s2 = s2 + j;            // 0+2+4+6+8
}

// break in a while loop
var s3 = 0;
var k = 0;
while (k < 100) {
    s3 = s3 + 1;
    if (s3 == 3) break;
    k = k + 1;
}

// continue in a while loop (skip m == 3)
var s4 = 0;
var m = 0;
while (m < 5) {
    m = m + 1;
    if (m == 3) continue;
    s4 = s4 + m;            // 1+2+4+5
}

// break and continue in a do/while loop
var s5 = 0;
var n = 0;
do {
    n = n + 1;
    if (n == 2) continue;  // skip 2
    if (n == 4) break;     // stop before 4
    s5 = s5 + n;           // 1+3
} while (n < 100);

// nested loops: break only affects the innermost loop
var count = 0;
for (var a = 0; a < 3; a = a + 1) {
    for (var b = 0; b < 3; b = b + 1) {
        if (b == 1) break;
        count = count + 1; // inner contributes once per outer iteration
    }
}

result = s1 == 10 && s2 == 20 && s3 == 3 && s4 == 12 && s5 == 4 && count == 3;
