// methods defined on the constructor are shared by every instance

function Person(name) {
    this.name = name;
}

Person.greet = function() {
    return "hi " + this.name;
};

var a = new Person("Kenny");
var b = new Person("Stan");

result = a.greet() == "hi Kenny" && b.greet() == "hi Stan";
