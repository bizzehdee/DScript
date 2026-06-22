# DScript — Feature & Performance Plan

This document describes every planned feature and optimisation, with scope, design
notes, and effort estimates. See `tasks.md` for the ordered implementation checklist.

---

## Language Features

---

### 1. Short-hand and computed object properties

**What**
Two missing object-literal forms from modern JS:

```js
// short-hand: { x, y }  is sugar for  { x: x, y: y }
var point = { x, y };

// computed key: { [expr]: value }
var key = "name";
var obj = { [key]: "Alice" };   // { name: "Alice" }
```

**Scope**
Pure compiler change in `CompileObjectLiteral` (`Compiler.Factor.cs`).

- Short-hand: when a key token is followed by `,` or `}` instead of `:`, emit
  `GetVar key` rather than requiring an explicit value.
- Computed key: when the key is `[`, compile the expression, then emit a new
  `SetPropDynamic` opcode that pops both key and value off the stack.

New opcode needed: `SetPropDynamic`.

**Effort:** Small (1–2 days).

---

### 2. Default parameter values

**What**

```js
function greet(name, greeting = "Hello") {
    return `${greeting}, ${name}!`;
}
greet("Alice");          // "Hello, Alice!"
greet("Bob", "Hi");      // "Hi, Bob!"
```

**Scope**
Pure compiler change in `CompileFunctionRest` / `CompileArrowFunction`.

For each parameter that carries a `= expr` default, emit a guard at function
entry:

```
GetVar  paramName
JumpIfDefined  →skip
  <compile default expr>
  SetVar  paramName
skip:
```

No new opcodes needed; `JumpIfDefined` (jump if the variable is not `undefined`)
can be added as a single-instruction variant of `JumpIfFalse`.

**Effort:** Small (1–2 days).

---

### 3. `let` / `const` block scoping

**What**
`var` is function-scoped; `let` and `const` are block-scoped:

```js
for (let i = 0; i < 3; i++) { /* i only exists here */ }
// i is not visible here

{
    const PI = 3.14159;
}
// PI is not visible here
```

**Scope**
Currently `const` is parsed as a keyword but treated identically to `var`.
True block scoping requires:

- A new `BlockEnv` opcode pair (`EnterBlock` / `LeaveBlock`) that pushes/pops a
  fresh scope frame on the environment chain at each `{`.
- The compiler emits `EnterBlock` at the start of any block that contains a
  `let` or `const` declaration, and `LeaveBlock` at the end.
- At declaration sites, `let`/`const` are compiled to `DefineLocal` rather than
  `SetVar` so the VM creates the binding in the innermost frame.
- `const` additionally sets an immutable flag on the binding; assignment emits a
  runtime error.

The existing `Environment` linked-list already supports the lookup semantics;
this is mainly about making sure the right frame is created and destroyed at the
right time.

**Effort:** Medium (3–5 days). High correctness value.

---

### 4. Optional chaining and nullish coalescing

**What**

```js
var city = user?.address?.city;      // undefined if any link is null/undefined
var name = config?.name ?? "default"; // "default" if null or undefined
fn?.();                              // call only if fn is not null/undefined
arr?.[0]                             // index only if arr is not null/undefined
```

**Scope**
Two new tokens (`?.` and `??`) and short-circuit compile paths; no new opcodes
beyond reusing existing jumps.

- `?.` on a member access emits: `Dup`, `JumpIfNullOrUndefined →end`, `GetProp`.
- `?.` on a call emits: `Dup`, `JumpIfNullOrUndefined →end`, `Call`.
- `??` emits: `Dup`, `JumpIfNotNullOrUndefined →end`, `Pop`, `<rhs>`.
- A new helper opcode `JumpIfNullOrUndefined` (and its inverse) keeps bytecode
  compact.

**Effort:** Small–Medium (2–3 days).

---

### 5. Destructuring assignment

**What**

```js
// array destructuring
const [a, b, c] = [1, 2, 3];
const [head, ...tail] = arr;

// object destructuring
const { x, y } = point;
const { name: alias, age = 0 } = person;

// in function parameters
function draw({ x, y, color = "black" }) { ... }
function first([head]) { return head; }
```

**Scope**
Compiler-only change, but touches several sites:

- `var` / `let` / `const` declarations: detect `[` or `{` after the keyword and
  dispatch to `CompileArrayDestructure` / `CompileObjectDestructure`.
- Assignment statements: same detection on the left-hand side of `=`.
- Function parameter lists: each parameter may be a pattern, not just an
  identifier.

Each destructuring pattern compiles to a sequence of index/property reads and
`SetVar` / `DefineLocal` calls. Rest elements (`...tail`) emit a `Slice` call
against the source array.

**Effort:** Medium (4–6 days). High user-facing value.

---

### 6. Rest and spread operators

**What**

```js
// rest: collect remaining arguments into an array
function sum(...nums) {
    return nums.reduce((a, b) => a + b, 0);
}

// spread at call site: expand an array into arguments
Math.max(...values);

// spread in array/object literals
var merged = [...arr1, ...arr2];
var copy   = { ...defaults, ...overrides };
```

**Scope**

- **Rest parameters**: compiler change in `CompileFunctionRest`. When the last
  parameter is `...name`, emit a `MakeRestArray` opcode at function entry that
  collects all arguments from index N onwards into a new array and binds it.
- **Spread at call sites**: a new `SpreadCall` opcode (or a pre-call `Unpack`
  step) that flattens a spread argument into the call's argument list.
- **Spread in array literals**: `CompileArrayLiteral` already iterates elements;
  a `...expr` element emits the array and a `PushSpread` opcode to copy its
  elements onto the constructed array.
- **Spread in object literals**: similar to computed properties; emit a
  `MergeObject` opcode.

**Effort:** Medium (3–5 days). Depends on destructuring infrastructure for rest
parameters.

---

### 7. `class` syntax

**What**

```js
class Animal {
    constructor(name) {
        this.name = name;
    }
    speak() {
        return `${this.name} makes a sound`;
    }
    static create(name) { return new Animal(name); }
}

class Dog extends Animal {
    speak() { return `${this.name} barks`; }
}
```

**Scope**
Pure compiler desugaring — the VM already supports prototype chains and `new`.
The `class` keyword compiles into the equivalent constructor-function and
prototype-assignment code that DScript already runs.

Key points:
- `constructor` method → the named function that becomes the class variable.
- Instance methods → `ClassName.methodName = function() { ... }` on the
  prototype object.
- `static` methods → assigned directly to the constructor variable.
- `extends` → `SubClass.prototype = new SuperClass()` linkage.
- `super(...)` call in the constructor → call the parent constructor with
  `this` bound.

The compiler needs a new `CompileClass` path but emits only existing opcodes.

**Effort:** Medium (4–6 days).

---

### 8. REPL

**What**
An interactive read-eval-print loop for exploring DScript at the command line.

```
$ dscript
> var x = 6 * 7;
> console.log(x);
42
> function fib(n) { return n < 2 ? n : fib(n-1)+fib(n-2); }
> fib(10)
55
```

**Scope**
A thin console application (`DScript.Repl` project) wrapping a single persistent
`ScriptEngine`. Each input line is compiled with `DScriptCompiler.CompileProgram`
and run on the engine. Errors are caught and printed; the engine state is kept
between inputs. Multi-line input can be detected by watching for unmatched `{`.

**Effort:** Small (1 day).

---

### 9. Source maps

**What**
Emit a `.dsmap` sidecar file alongside serialized bytecode, mapping each bytecode
offset back to `(source file, line, column)`. Lets external tools (debuggers,
error reporters) point to the original source rather than bytecode offsets.

**Scope**
The VM already records `(chunk.Name, line)` per instruction for the step debugger.
Source maps extend this to include the original file path and column. The
`BytecodeSerializer` gains optional `SaveWithSourceMap` / `LoadWithSourceMap`
methods; the format can follow the compact VLQ scheme used by the JS ecosystem
so existing tools can consume it.

**Effort:** Medium (3–4 days).

---

### 10. Generators and iterators

**What**

```js
function* range(start, end) {
    for (var i = start; i < end; i++) yield i;
}

for (var n of range(1, 5)) {
    console.log(n);   // 1, 2, 3, 4
}
```

**Scope**
Suspendable execution frames. Each generator call creates a `GeneratorObject`
that holds a saved `(ip, stack snapshot, environment)`. `yield expr` saves this
state and returns the value; the next `.next()` call restores it.

Also enables the iterator protocol: any object with a `next()` method works
with `for...of`.

This is the most significant VM change in this plan. The operand stack is
currently a flat array in `VirtualMachine`; per-generator snapshots require
either copying the relevant slice or restructuring execution into coroutine-style
continuations.

**Effort:** Large (1–2 weeks).

---

### 11. `async` / `await`

**What**

```js
async function fetchUser(id) {
    var data = await httpGet(`/users/${id}`);
    return JSON.parse(data);
}
```

**Scope**
Depends on generators (the desugaring of `async`/`await` is essentially a
generator-based state machine driven by promise resolution). A minimal
implementation can be cooperative and host-driven: `await` suspends the current
function and hands a continuation back to the host, which resumes it when the
awaited value is available.

Requires a lightweight `Promise`-equivalent type and a micro-task queue either
inside the VM or supplied by the host.

**Effort:** Large (2–3 weeks). Depends on generators.

---

### 12. Module system

**What**

```js
// math.ds
export function add(a, b) { return a + b; }
export const PI = 3.14159;

// main.ds
import { add, PI } from "./math.ds";
console.log(add(1, PI));
```

**Scope**
Each module is an independent `Chunk`. The host supplies a resolver callback
`(importPath, currentModule) → Chunk`. The VM tracks already-executed modules
(keyed by resolved path) and returns their export namespace without re-running.

`export` compiles to writing named values into a special `__exports__` scope
object. `import { x } from "..."` compiles to calling the resolver and reading
the exported names.

CommonJS-style `require()` can be added as a simpler stepping stone without new
syntax.

**Effort:** Large (1–2 weeks). Bytecode serialization already in place.

---

### 13. Language Server Protocol (LSP) support

**What**
A `DScript.LanguageServer` process that speaks LSP over stdio, powering VS Code
(and any other LSP-capable editor) with:

- Syntax error diagnostics as-you-type
- Hover type/value information
- Go-to-definition
- Completion (variables, properties, function names in scope)
- Signature help on function calls

**Scope**
Large standalone project. The compiler's partial-parse capability and the lexer's
`CloneToEnd` lookahead provide the foundation. Completions and hover require a
lightweight type-inference or value-tracking pass that doesn't currently exist.

**Effort:** Very large (ongoing). High tooling value.

---

## Performance Optimisations

---

### P1. Tiered constant folding

**What**
The optimizer already folds a `Binary` whose right operand is a single
`Constant` into a `BinaryConst`. Extend it to fold chains of constants:

```
1 + 2 + 3   →   Constant(6)
"Hello" + ", " + name   →   Constant("Hello, ") + name
```

**Scope**
Compiler-side only (`Chunk.cs` optimizer passes). After the existing
`TryFoldBinaryConst` pass, add a second pass that looks for adjacent
`Constant` + `BinaryConst` pairs that can collapse further.

**Effort:** Small (1 day).

---

### P2. String interning for name indices

**What**
`Chunk.AddName` deduplicates name strings within a single chunk. When multiple
chunks (functions, modules) reference the same property names (e.g. `"length"`,
`"push"`, `"toString"`), they each hold separate string instances. A VM-level
interning table would make these a single allocation and allow pointer comparison
instead of string comparison during property lookup.

**Scope**
Add a `NameTable` singleton (or per-engine table) that canonicalises property
name strings. `AddName` consults this table; `GetProp` / `SetProp` compare
canonical references rather than string content.

**Effort:** Small–Medium (2 days).

---

### P3. Call-frame allocation pool

**What**
The remaining GC bottleneck is call-frame `ScriptVar` allocation for functions
whose frames can't be recycled (`RecyclableFrame = false` — functions that close
over their frame or are called recursively). A per-engine pool of pre-allocated
`ScriptVar` objects, returned on frame exit, would reduce allocation pressure on
the hot call path.

**Scope**
`VirtualMachine` gains a `Stack<ScriptVar>` pool. `InvokeVmFunctionFromStack`
borrows from the pool (or allocates if empty); the `finally` block in `Execute`
returns the frame vars to the pool. Pool entries must have their children cleared
before reuse.

**Effort:** Small (1–2 days). Extends the existing `BorrowFrameVars` pattern.

---

### P4. Inline property cache

**What**
`GetProp` / `SetProp` currently walk a linked list of `ScriptVarLink` nodes on
every access. An inline cache stores `(expectedShape, slotOffset)` at each
`GetProp` call site; on a hit the slot is read directly, skipping the walk.

This is the highest-impact runtime optimisation for object-heavy scripts.

**Scope**
Requires a notion of object "shape" (property layout version number, incremented
when properties are added or removed). The cache is stored in a side-table keyed
by `(chunk, opcodeOffset)`. On a miss the full walk runs and the cache is updated.

**Effort:** Medium–Large (1 week). No language changes; pure VM internals.

---

### P5. Tail-call elimination (TCE)

**What**
A self-recursive call in tail position reuses the current call frame instead of
allocating a new one, turning O(n) stack growth into O(1):

```js
function sum(n, acc = 0) {
    if (n == 0) return acc;
    return sum(n - 1, acc + n);   // tail call — no new frame needed
}
sum(100000);   // would stack-overflow without TCE
```

**Scope**
The compiler detects tail calls (a `Call` immediately followed by `Return`,
with no active `try` blocks) and emits a `TailCall` opcode. The VM handles
`TailCall` by rebinding the current frame's parameters in-place and jumping back
to the start of the function body.

**Effort:** Medium (3–4 days).
