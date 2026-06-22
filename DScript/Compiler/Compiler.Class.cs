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
            var methods = new List<(string Name, bool IsStatic, string Source)>();
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

                var methodName = lexer.TokenString;
                lexer.Match(ScriptLex.LexTypes.Id);

                // Capture the full method source starting from '('
                var methodSrc = CaptureMethodSource();

                if (!isStatic && methodName == "constructor")
                    constructorSrc = methodSrc;
                else
                    methods.Add((methodName, isStatic, methodSrc));
            }
            lexer.Match((ScriptLex.LexTypes)'}');

            // --- Emit constructor ---
            _superClassName = parentName;
            var ctorSrc = constructorSrc ?? "(){}";
            var ctorIdx = CompileMethodSource(ctorSrc, className);
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
            foreach (var (methodName, isStatic, src) in methods)
            {
                var methodIdx = CompileMethodSource(src, methodName);
                var methodNameIdx = chunk.AddName(methodName);

                // Both instance methods and statics go directly on className.
                // Static methods land on the constructor; instance methods also land on
                // the constructor so the prototype chain walker finds them.
                chunk.Emit(OpCode.GetVar, classNameIdx);
                chunk.Emit(OpCode.MakeClosure, methodIdx);
                chunk.MakesClosure = true;
                chunk.Emit(OpCode.SetProp, methodNameIdx);
                chunk.Emit(OpCode.Pop);
            }
        }

        // Compile a method source string "(params) { body }" into a nested chunk.
        // Returns the index in the current chunk's function table.
        private int CompileMethodSource(string src, string name)
        {
            var fnChunk = new Chunk { Name = name };
            var paramDefaults = new List<(string ParamName, string DefaultSrc)>();

            var savedLexer = lexer;
            using var methodLexer = new ScriptLex(src);
            lexer = methodLexer;

            lexer.Match((ScriptLex.LexTypes)'(');
            while (lexer.TokenType != (ScriptLex.LexTypes)')')
            {
                var paramName = lexer.TokenString;
                lexer.Match(ScriptLex.LexTypes.Id);
                fnChunk.Parameters.Add(paramName);

                string defaultSrc = null;
                if (lexer.TokenType == (ScriptLex.LexTypes)'=')
                {
                    lexer.Match((ScriptLex.LexTypes)'=');
                    defaultSrc = ReadDefaultExpression();
                }
                paramDefaults.Add((paramName, defaultSrc));

                if (lexer.TokenType != (ScriptLex.LexTypes)')')
                    lexer.Match((ScriptLex.LexTypes)',');
            }
            lexer.Match((ScriptLex.LexTypes)')');

            var saved = chunk;
            chunk = fnChunk;
            EnterFunctionBody(out var savedLoops, out var savedFinally);

            EmitDefaultParamGuards(paramDefaults);
            CompileBlock();
            chunk.Emit(OpCode.PushUndefined);
            chunk.Emit(OpCode.Return);

            ExitFunctionBody(savedLoops, savedFinally);
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

        // Compile `super(args)` — calls the parent constructor with `this` as receiver.
        private void CompileSuper()
        {
            lexer.Match(ScriptLex.LexTypes.RSuper);

            if (_superClassName == null)
            {
                // super outside a class constructor: compile as identifier read (no-op graceful)
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
    }
}
