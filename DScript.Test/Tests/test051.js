// compound assignment operators

var a = 10;
a += 5;     // 15
a -= 3;     // 12
a *= 2;     // 24
a /= 4;     // 6
a %= 4;     // 2

var b = 12;     // 1100
b &= 10;        // 1100 & 1010 = 1000 = 8
b |= 1;         // 1000 | 0001 = 1001 = 9
b ^= 3;         // 1001 ^ 0011 = 1010 = 10

var c = 1;
c <<= 4;        // 16
c >>= 1;        // 8

var d = -8;
d >>>= 28;      // unsigned shift of -8 by 28 -> 15

result = a == 2 && b == 10 && c == 8 && d == 15;
