// a constructor that returns an object replaces the new instance

function Factory() {
    this.fromInstance = 1;
    var made = { fromReturn: 2 };
    return made;
}

var f = new Factory();

// f is the returned object, not the implicit instance
result = f.fromReturn == 2 && f.fromInstance == undefined;
