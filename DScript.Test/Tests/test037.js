function throwsException() {
    throw "Some Exception Happened";
}

try {
    throwsException();
} catch (e) {

    result = 0;
}
finally {

    result = 1;
}