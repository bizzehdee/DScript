// instanceof, including walking an inheritance chain

function Animal() {
    this.alive = 1;
}

function Dog() {
    this.barks = 1;
}

Dog.prototype = new Animal();

var d = new Dog();
var lone = new Animal();

result = (d instanceof Dog) &&
         (d instanceof Animal) &&
         (lone instanceof Animal) &&
         !(lone instanceof Dog);
