/* Javascript eval */

// In JavaScript, eval("{ foo: 42 }") parses { } as a block, not an object literal.
// Wrap in parens to force expression context.
myfoo = eval("(" + "{ foo: 42 }" + ")");

result = eval("4*10+2")==42 && myfoo.foo==42;
