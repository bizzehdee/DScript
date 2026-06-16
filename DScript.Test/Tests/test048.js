// string substring/substr/lastIndexOf clamp arguments instead of throwing

var s = "hello";

var swapped = s.substring(3, 1);   // indices swapped -> "el"
var openEnd = s.substring(2);      // end omitted -> "llo"
var fromEnd = s.substr(-2);        // negative start counts from end -> "lo"
var overLen = s.substr(1, 100);    // length clamped -> "ello"
var lastL = s.lastIndexOf("l");    // position omitted -> searches from end -> 3

result = swapped == "el" &&
         openEnd == "llo" &&
         fromEnd == "lo" &&
         overLen == "ello" &&
         lastL == 3;
