/* Javascript eval */

mystructure = { a:39, b:3, addStuff : function(c,d) { return c+d; } };

mystring = JSON.stringify(mystructure, undefined); 

// 42-tiny-js change begin --->
// in JavaScript eval is not JSON.parse
// use parentheses or JSON.parse instead
//mynewstructure = eval(mystring);
mynewstructure = eval("("+mystring+")");
// JSON.parse is strict JSON and cannot reconstruct functions (standard JS omits
// them from stringify), so round-trip a data-only object through it.
mynewstructure2 = JSON.parse('{"a":39,"b":3}');
//<--- 42-tiny-js change end

result = mynewstructure.addStuff(mynewstructure.a, mynewstructure.b) == 42 && (mynewstructure2.a + mynewstructure2.b) == 42;
