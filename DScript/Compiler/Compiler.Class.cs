/*
Copyright (c) 2014 - 2020 Darren Horrocks

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System.Collections.Generic;
using DScript.Vm;

namespace DScript.Compiler
{
    public sealed partial class DScriptCompiler
    {
        // Name of the parent class currently being compiled, for `super()` support.
        private string _superClassName;

        /// <summary>
        /// Compile a class declaration:
        ///   class Name [extends Parent] { [static] method(...) { ... } ... }
        ///
        /// Desugars to:
        ///   DeclareVar Name
        ///   MakeClosure &lt;constructor&gt; (or an empty one if none is declared)
        ///   SetVar Name
        ///   Pop
        ///   [SetupPrototypeChain]
        ///   For each method:   Name.prototype.method = function(...) { ... }
        ///   For each static:   Name.static = function(...) { ... }
        ///   For each static initialisation block: (function(){...}).call(Name)
        /// </summary>
        private void CompileClass()
        {
            lexer.Match(ScriptLex.LexTypes.RClass);

            var className = lexer.TokenString;
            lexer.Match(ScriptLex.LexTypes.Id);

            string parentName = null;
            if (lexer.TokenType == ScriptLex.LexTypes.RExtends)
            {
                lexer.Match(ScriptLex.LexTypes.RExtends);
                parentName = lexer.TokenString;
                lexer.Match(ScriptLex.LexTypes.Id);
            }

            // Declare the class variable upfront so recursive references work.
            var classNameIdx = chunk.AddName(className);
            chunk.Emit(OpCode.DeclareVar, classNameIdx);

            // Collect all members before emitting anything for the constructor.
            // AccessorOp: SetProp for regular methods, DefineGetter/DefineSetter for accessors.
            var methods = new List<(string Name, bool IsStatic, string Source, OpCode AccessorOp)>();
            var staticBlocks = new List<string>();  // static { ... } body sources
            // Private members: (internalName, isStatic, methodSourceOrNull, fieldInitExprOrNull)
            var privateMembers = new List<(string Name, bool IsStatic, string MethodSrc, string FieldInit)>();
            var privateNames = new System.Collections.Generic.HashSet<string>();
            string constructorSrc = null;

            lexer.Match((ScriptLex.LexTypes)'{');
            while (lexer.TokenType != (ScriptLex.LexTypes)'}' &&
                   lexer.TokenType != ScriptLex.LexTypes.Eof)
            {
                var isStatic = false;
                if (lexer.TokenType == ScriptLex.LexTypes.RStatic)
                {
                    lexer.Match(ScriptLex.LexTypes.RStatic);
                    isStatic = true;
                }

                // static initialisation block: static { ... }
                if (isStatic && lexer.TokenType == (ScriptLex.LexTypes)'{')
                {
                    staticBlocks.Add(CaptureBlockSource());
                    continue;
                }

                // Private field or method: #name
                if (lexer.TokenType == ScriptLex.LexTypes.PrivateName)
                {
                    var privateName = lexer.TokenString; // includes '#' prefix
                    lexer.Match(ScriptLex.LexTypes.PrivateName);
                    privateNames.Add(privateName);
                    if (lexer.TokenType == (ScriptLex.LexTypes)'(')
                    {
                        // Private method
                        var methodSrc = CaptureMethodSource();
                        privateMembers.Add((privateName, isStatic, methodSrc, null));
                    }
                    else
                    {
                        // Private field (with optional initializer)
                        string initExpr = null;
                        if (lexer.TokenType == (ScriptLex.LexTypes)'=')
                        {
                            lexer.Match((ScriptLex.LexTypes)'=');
                            initExpr = CaptureFieldInitializer();
                        }
                        if (lexer.TokenType == (ScriptLex.LexTypes)';')
                            lexer.Match((ScriptLex.LexTypes)';');
                        privateMembers.Add((privateName, isStatic, null, initExpr));
                    }
                    continue;
                }

                var methodName = lexer.TokenString;
                lexer.Match(ScriptLex.LexTypes.Id);

                // get/set accessor — contextual: treat as accessor keyword only when the
                // next token is another identifier (the property name) not '('.
                OpCode accessorOp = OpCode.SetProp;
                if ((methodName == "get" || methodName == "set") && lexer.TokenType == ScriptLex.LexTypes.Id)
                {
                    accessorOp = methodName == "get" ? OpCode.DefineGetter : OpCode.DefineSetter;
                    methodName = lexer.TokenString;
                    lexer.Match(ScriptLex.LexTypes.Id);
                }

                // Capture the full method source starting from '('
                var methodSrc2 = CaptureMethodSource();

                if (!isStatic && methodName == "constructor" && accessorOp == OpCode.SetProp)
                    constructorSrc = methodSrc2;
                else
                    methods.Add((methodName, isStatic, methodSrc2, accessorOp));
            }
            lexer.Match((ScriptLex.LexTypes)'}');

            // Build preamble for instance private field initializers
            var instanceFieldInits = new System.Text.StringBuilder();
            foreach (var (name, isStatic, _, init) in privateMembers)
            {
                if (!isStatic && init != null)
                    instanceFieldInits.Append($"this[\"{name}\"] = {init};");
            }

            // --- Emit constructor ---
            _superClassName = parentName;
            var savedPrivateNames = _currentClassPrivateNames;
            _currentClassPrivateNames = privateNames;
            var ctorSrc = constructorSrc ?? "(){}";
            var ctorPreamble = instanceFieldInits.Length > 0 ? instanceFieldInits.ToString() : null;
            var ctorIdx = CompileMethodSource(ctorSrc, className, ctorPreamble);
            _superClassName = null;

            chunk.Emit(OpCode.MakeClosure, ctorIdx);
            chunk.MakesClosure = true;
            chunk.Emit(OpCode.SetVar, classNameIdx);
            chunk.Emit(OpCode.Pop);

            // --- Set up prototype chain for extends ---
            // DScript's VM uses `instance.prototype = ctor` for the prototype chain
            // (see FindInParentClasses). So for inheritance we set Dog.prototype = Animal
            // (the constructor, not an instance) so the chain becomes:
            //   DogInstance.prototype → Dog_ctor → Dog_ctor.prototype → Animal_ctor → …
            if (parentName != null)
            {
                var parentNameIdx = chunk.AddName(parentName);
                // className.prototype = parentClassName  (link ctor chain)
                chunk.Emit(OpCode.GetVar, classNameIdx);
                chunk.Emit(OpCode.GetVar, parentNameIdx);
                chunk.Emit(OpCode.SetProp, chunk.AddName("prototype"));
                chunk.Emit(OpCode.Pop);
            }

            // --- Emit methods and statics ---
            // Instance methods are placed directly on the constructor function so that
            // FindInParentClasses(instance, name) finds them via instance.prototype = ctor.
            // Track the parent name across method bodies so `super.m()` resolves.
            _superClassName = parentName;
            foreach (var (methodName, isStatic, src, accessorOp) in methods)
            {
                var methodIdx = CompileMethodSource(src, methodName);
                var methodNameIdx = chunk.AddName(methodName);

                // Both instance methods and statics go directly on className.
                // Static methods land on the constructor; instance methods also land on
                // the constructor so the prototype chain walker finds them.
                chunk.Emit(OpCode.GetVar, classNameIdx);
                chunk.Emit(OpCode.MakeClosure, methodIdx);
                chunk.MakesClosure = true;
                chunk.Emit(accessorOp, methodNameIdx);
                chunk.Emit(OpCode.Pop);
            }

            // --- Emit private methods and static private fields ---
            foreach (var (name, isStatic, methodSrc, fieldInit) in privateMembers)
            {
                if (methodSrc != null)
                {
                    // Private method — stored on the constructor with the #name key
                    var methodIdx = CompileMethodSource(methodSrc, name);
                    var nameIdx = chunk.AddName(name);
                    chunk.Emit(OpCode.GetVar, classNameIdx);
                    chunk.Emit(OpCode.MakeClosure, methodIdx);
                    chunk.MakesClosure = true;
                    chunk.Emit(OpCode.SetProp, nameIdx);
                    chunk.Emit(OpCode.Pop);
                }
                else if (isStatic && fieldInit != null)
                {
                    // Static private field with initializer — set on class constructor
                    var nameIdx = chunk.AddName(name);
                    chunk.Emit(OpCode.GetVar, classNameIdx);
                    CompileInSubLexer(fieldInit, CompileBase);
                    chunk.Emit(OpCode.SetProp, nameIdx);
                    chunk.Emit(OpCode.Pop);
                }
            }

            _superClassName = null;
            _currentClassPrivateNames = savedPrivateNames;

            // --- Emit static initialisation blocks ---
            // Each `static { ... }` is compiled as an anonymous function called with
            // the class constructor as `this`, so `this.x = 1` sets a static property.
            foreach (var blockSrc in staticBlocks)
            {
                var blockIdx = CompileMethodSource("()" + blockSrc, "<static_init>");
                chunk.Emit(OpCode.GetVar, classNameIdx);   // receiver (this)
                chunk.Emit(OpCode.MakeClosure, blockIdx);
                chunk.MakesClosure = true;
                chunk.Emit(OpCode.CallMethod, 0);           // call with 0 args
                chunk.Emit(OpCode.Pop);
            }
        }

        // Compile a method source string "(params) { body }" into a nested chunk.
        // An optional preamble (plain statements) is compiled into the function body
        // before the user-written body — used for instance private field initializers.
        // Returns the index in the current chunk's function table.
        private int CompileMethodSource(string src, string name, string preamble = null)
        {
            var fnChunk = new Chunk { Name = name };

            var savedLexer = lexer;
            using var methodLexer = new ScriptLex(src);
            lexer = methodLexer;

            var paramDefaults = ParseParameterList(fnChunk);

            var saved = chunk;
            chunk = fnChunk;
            EnterFunctionBody(out var savedLoops, out var savedFinally, out var savedBlockDepth);

            EmitDefaultParamGuards(paramDefaults);
            // Optional preamble (e.g. instance private field initializers)
            if (preamble != null)
            {
                var bodyLex = lexer;  // save the method-body lexer
                using var preambleLex = new ScriptLex(preamble);
                lexer = preambleLex;
                while (lexer.TokenType != ScriptLex.LexTypes.Eof)
                    CompileStatement();
                lexer = bodyLex;
            }
            CompileBlock();
            chunk.Emit(OpCode.PushUndefined);
            chunk.Emit(OpCode.Return);

            ExitFunctionBody(savedLoops, savedFinally, savedBlockDepth);
            FinalizeArgumentsUsage(fnChunk, saved);
            chunk = saved;
            lexer = savedLexer;

            fnChunk.Source = "function " + name + src;
            return saved.AddFunction(fnChunk);
        }

        // Consume from '(' through the matching '}' that closes the method body.
        // Returns the captured source string starting with '('.
        private string CaptureMethodSource()
        {
            var start = lexer.TokenStart;
            // Skip param list
            lexer.Match((ScriptLex.LexTypes)'(');
            var depth = 1;
            while (lexer.TokenType != ScriptLex.LexTypes.Eof && depth > 0)
            {
                if (lexer.TokenType == (ScriptLex.LexTypes)'(') depth++;
                else if (lexer.TokenType == (ScriptLex.LexTypes)')') { depth--; if (depth == 0) break; }
                lexer.GetNextToken();
            }
            lexer.Match((ScriptLex.LexTypes)')');

            // Skip method body block
            lexer.Match((ScriptLex.LexTypes)'{');
            depth = 1;
            while (lexer.TokenType != ScriptLex.LexTypes.Eof && depth > 0)
            {
                if (lexer.TokenType == (ScriptLex.LexTypes)'{') depth++;
                else if (lexer.TokenType == (ScriptLex.LexTypes)'}') { depth--; if (depth == 0) break; }
                lexer.GetNextToken();
            }
            lexer.Match((ScriptLex.LexTypes)'}');

            return lexer.GetSubString(start);
        }

        // Capture a field initializer expression up to the next `;` or `}` at depth 0.
        // Returns the expression source text (without the `;`).
        private string CaptureFieldInitializer()
        {
            var start = lexer.TokenStart;
            var depth = 0;
            while (lexer.TokenType != ScriptLex.LexTypes.Eof)
            {
                var t = lexer.TokenType;
                if (t is (ScriptLex.LexTypes)'(' or (ScriptLex.LexTypes)'[' or (ScriptLex.LexTypes)'{') depth++;
                else if (t is (ScriptLex.LexTypes)')' or (ScriptLex.LexTypes)']' or (ScriptLex.LexTypes)'}')
                {
                    if (depth == 0) break;
                    depth--;
                }
                else if (depth == 0 && t == (ScriptLex.LexTypes)';') break;
                lexer.GetNextToken();
            }
            return lexer.GetSubString(start);
        }

        // Capture a bare block `{ ... }` (no parameter list) and return its source
        // including the braces, so it can be wrapped as a method body.
        private string CaptureBlockSource()
        {
            var start = lexer.TokenStart;
            lexer.Match((ScriptLex.LexTypes)'{');
            var depth = 1;
            while (lexer.TokenType != ScriptLex.LexTypes.Eof && depth > 0)
            {
                if (lexer.TokenType == (ScriptLex.LexTypes)'{') depth++;
                else if (lexer.TokenType == (ScriptLex.LexTypes)'}') { depth--; if (depth == 0) break; }
                lexer.GetNextToken();
            }
            lexer.Match((ScriptLex.LexTypes)'}');
            return lexer.GetSubString(start);
        }

        // Compile a `super` expression:
        //   super(args)         → calls the parent constructor with `this` as receiver
        //   super.m(args)        → calls the parent's method `m` with `this` as receiver
        //   super.m              → reads the parent's property `m`
        // Methods/properties live directly on the parent constructor, and its own
        // prototype chain is walked by GetProp, so resolving against the parent var works.
        private void CompileSuper()
        {
            lexer.Match(ScriptLex.LexTypes.RSuper);

            // super.member [ (...) ]
            if (lexer.TokenType == (ScriptLex.LexTypes)'.')
            {
                lexer.Match((ScriptLex.LexTypes)'.');
                var member = lexer.TokenString;
                lexer.Match(ScriptLex.LexTypes.Id);
                var memberIdx = chunk.AddName(member);

                if (lexer.TokenType == (ScriptLex.LexTypes)'(')
                {
                    // Method call: receiver is `this`, callee is Parent.member.
                    // Stack: [this, Parent.member, args...] → CallMethod argc
                    chunk.Emit(OpCode.GetVar, chunk.AddName("this"));
                    EmitSuperMember(memberIdx);
                    var margc = CompileArguments();
                    chunk.Emit(OpCode.CallMethod, margc);
                }
                else
                {
                    // Property read: super.x
                    EmitSuperMember(memberIdx);
                }
                return;
            }

            if (_superClassName == null)
            {
                // super(...) outside a class constructor: graceful no-op identifier read.
                chunk.Emit(OpCode.PushUndefined);
                if (lexer.TokenType == (ScriptLex.LexTypes)'(')
                    CompileArguments();
                return;
            }

            // super(args) → Parent.call(this, args) via CallMethod
            // Stack: [this, Parent, args...] → CallMethod argc
            chunk.Emit(OpCode.GetVar, chunk.AddName("this"));
            chunk.Emit(OpCode.GetVar, chunk.AddName(_superClassName));
            var argc = CompileArguments();
            // argc already on stack; call Parent with this as receiver
            chunk.Emit(OpCode.CallMethod, argc);
        }

        // Push Parent.member onto the stack (undefined if super has no parent in scope).
        private void EmitSuperMember(int memberIdx)
        {
            if (_superClassName == null)
            {
                chunk.Emit(OpCode.PushUndefined);
                return;
            }
            chunk.Emit(OpCode.GetVar, chunk.AddName(_superClassName));
            chunk.Emit(OpCode.GetProp, memberIdx);
        }
    }
}
