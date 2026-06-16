// constructors reached through member access (new ns.Widget())

var ns = {};
ns.Widget = function(w) {
    this.w = w;
};

var viaDot = new ns.Widget(5);
var viaIndex = new ns["Widget"](6);

result = viaDot.w == 5 && viaIndex.w == 6;
