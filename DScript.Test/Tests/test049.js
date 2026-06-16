// do/while loops: the body runs at least once, then repeats while the condition holds

// normal iteration
var sum = 0;
var i = 1;
do {
    sum = sum + i;
    i = i + 1;
} while (i <= 5);

// body executes once even when the condition is false from the start
var ran = 0;
do {
    ran = ran + 1;
} while (ran > 10);

result = sum == 15 && ran == 1;
