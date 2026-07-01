/* Javascript eval */

mystructure = { a:39, b:3, addStuff : function(c,d) { return c+d; } };

mystring = JSON.stringify(mystructure, undefined);

// In JavaScript, eval(jsonString) parses { } as a block, not an object literal.
// Wrap in parens to force expression context.
mynewstructure = eval("(" + mystring + ")");

result = mynewstructure.addStuff(mynewstructure.a, mynewstructure.b);
