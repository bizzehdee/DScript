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
        private void CompileFactor(bool canAssign)
        {
            // yield expression — must be checked before arrow-function and identifier cases
            if (lexer.TokenType == ScriptLex.LexTypes.RYield)
            {
                lexer.Match(ScriptLex.LexTypes.RYield);
                // yield with a value: yield <expr>; yield with no value: yield;
                if (lexer.TokenType != (ScriptLex.LexTypes)';' &&
                    lexer.TokenType != (ScriptLex.LexTypes)'}' &&
                    lexer.TokenType != ScriptLex.LexTypes.Eof)
                    CompileBase();
                else
                    chunk.Emit(OpCode.PushUndefined);
                chunk.Emit(OpCode.Yield);
                return;
            }

            // await expression — compiles identically to yield; the async driver
            // intercepts the yielded Promise and resumes with the resolved value.
            if (lexer.TokenType == ScriptLex.LexTypes.RAwait)
            {
                lexer.Match(ScriptLex.LexTypes.RAwait);
                // await expr — evaluate the expression (should be a Promise)
                if (lexer.TokenType != (ScriptLex.LexTypes)';' &&
                    lexer.TokenType != (ScriptLex.LexTypes)'}' &&
                    lexer.TokenType != ScriptLex.LexTypes.Eof)
                    CompileBase();
                else
                    chunk.Emit(OpCode.PushUndefined);
                chunk.Emit(OpCode.Yield); // reuse Yield — the async driver handles resumption
                return;
            }

            // Arrow function: `x => body` or `(params) => body` — must be
            // checked before the '(' and Id cases so the disambiguation runs
            // before any tokens are consumed.
            if ((lexer.TokenType == (ScriptLex.LexTypes)'(' ||
                 lexer.TokenType == ScriptLex.LexTypes.Id) && IsArrowFunction())
            {
                CompileArrowFunction();
                return;
            }

            switch (lexer.TokenType)
            {
                case (ScriptLex.LexTypes)'(':
                    lexer.Match((ScriptLex.LexTypes)'(');
                    CompileBase();
                    lexer.Match((ScriptLex.LexTypes)')');
                    break;

                case ScriptLex.LexTypes.RTrue:
                    lexer.Match(ScriptLex.LexTypes.RTrue);
                    chunk.Emit(OpCode.PushTrue);
                    break;

                case ScriptLex.LexTypes.RFalse:
                    lexer.Match(ScriptLex.LexTypes.RFalse);
                    chunk.Emit(OpCode.PushFalse);
                    break;

                case ScriptLex.LexTypes.RNull:
                    lexer.Match(ScriptLex.LexTypes.RNull);
                    chunk.Emit(OpCode.PushNull);
                    break;

                case ScriptLex.LexTypes.RUndefined:
                    lexer.Match(ScriptLex.LexTypes.RUndefined);
                    chunk.Emit(OpCode.PushUndefined);
                    break;

                case ScriptLex.LexTypes.Int:
                {
                    var value = new ScriptVar(lexer.TokenString, ScriptVar.Flags.Integer).Int;
                    lexer.Match(ScriptLex.LexTypes.Int);
                    EmitConstantInt(value);
                    break;
                }

                case ScriptLex.LexTypes.Float:
                {
                    var value = new ScriptVar(lexer.TokenString, ScriptVar.Flags.Double).Float;
                    lexer.Match(ScriptLex.LexTypes.Float);
                    chunk.Emit(OpCode.Constant, chunk.AddConstant(ConstantValue.Double(value)));
                    break;
                }

                case ScriptLex.LexTypes.Str:
                {
                    var value = lexer.TokenString;
                    lexer.Match(ScriptLex.LexTypes.Str);
                    chunk.Emit(OpCode.Constant, chunk.AddConstant(ConstantValue.String(value)));
                    break;
                }

                case ScriptLex.LexTypes.RegExp:
                {
                    var value = lexer.TokenString;
                    lexer.Match(ScriptLex.LexTypes.RegExp);
                    chunk.Emit(OpCode.Constant, chunk.AddConstant(ConstantValue.Regex(value)));
                    break;
                }

                case ScriptLex.LexTypes.TemplateLiteral:
                    CompileTemplateLiteral();
                    break;

                case (ScriptLex.LexTypes)'{':
                    CompileObjectLiteral();
                    break;

                case (ScriptLex.LexTypes)'[':
                    CompileArrayLiteral();
                    break;

                case ScriptLex.LexTypes.RAsync:
                {
                    // async function expression: async function [name](params) { body }
                    lexer.Match(ScriptLex.LexTypes.RAsync);
                    lexer.Match(ScriptLex.LexTypes.RFunction);
                    var fnName = string.Empty;
                    if (lexer.TokenType == ScriptLex.LexTypes.Id)
                    {
                        fnName = lexer.TokenString;
                        lexer.Match(ScriptLex.LexTypes.Id);
                    }
                    var idx = CompileFunctionRest(fnName, isGenerator: false, isAsync: true);
                    chunk.Emit(OpCode.MakeClosure, idx);
                    chunk.MakesClosure = true;
                    break;
                }

                case ScriptLex.LexTypes.RFunction:
                {
                    lexer.Match(ScriptLex.LexTypes.RFunction);
                    bool isGenerator = lexer.TokenType == (ScriptLex.LexTypes)'*';
                    if (isGenerator) lexer.Match((ScriptLex.LexTypes)'*');
                    var fnName = string.Empty;
                    if (lexer.TokenType == ScriptLex.LexTypes.Id)
                    {
                        fnName = lexer.TokenString;
                        lexer.Match(ScriptLex.LexTypes.Id);
                    }
                    var idx = CompileFunctionRest(fnName, isGenerator);
                    chunk.Emit(OpCode.MakeClosure, idx);
                    chunk.MakesClosure = true;
                    break;
                }

                case ScriptLex.LexTypes.RNew:
                    CompileNew();
                    break;

                case ScriptLex.LexTypes.RSuper:
                    CompileSuper();
                    return;

                case ScriptLex.LexTypes.Id:
                    CompileIdentifierChain(canAssign);
                    return;

                default:
                    // unexpected token for the current (phase-limited) grammar
                    lexer.Match(ScriptLex.LexTypes.Eof);
                    return;
            }

            // Allow member access / calls on any primary expression:
            // 'str'.method(), [1,2].indexOf(x), (expr).prop, new Foo().bar, etc.
            CompileMemberChain(false);
        }

        // identifier, optionally followed by an assignment, increment, or a
        // chain of .member / [index] accesses (also assignable).
        private void CompileIdentifierChain(bool canAssign)
        {
            var name = lexer.TokenString;
            lexer.Match(ScriptLex.LexTypes.Id);

            // a = expr
            if (canAssign && lexer.TokenType == (ScriptLex.LexTypes)'=')
            {
                lexer.Match((ScriptLex.LexTypes)'=');
                CompileBase();
                chunk.Emit(OpCode.SetVar, chunk.AddName(name));
                return;
            }

            // a op= expr
            if (canAssign && TryGetCompoundOp(lexer.TokenType, out var baseOp, out var isShift))
            {
                lexer.Match(lexer.TokenType);
                chunk.Emit(OpCode.GetVar, chunk.AddName(name));
                var operandStart1 = chunk.Code.Count;
                CompileBase();
                EmitBinaryOrShift(baseOp, isShift, operandStart1);
                chunk.Emit(OpCode.SetVar, chunk.AddName(name));
                return;
            }

            // a &&= b / a ||= b / a ??= b
            if (canAssign && lexer.TokenType is ScriptLex.LexTypes.AndAndEqual
                                              or ScriptLex.LexTypes.OrOrEqual
                                              or ScriptLex.LexTypes.NullCoalesceEqual)
            {
                var logOp = lexer.TokenType;
                lexer.Match(logOp);
                chunk.Emit(OpCode.GetVar, chunk.AddName(name));
                if (logOp == ScriptLex.LexTypes.NullCoalesceEqual)
                {
                    var isNull = chunk.EmitJump(OpCode.JumpIfNullOrUndefined); // null/undef → jump
                    var skipAssign = chunk.EmitJump(OpCode.Jump);              // not null/undef → skip
                    chunk.PatchJump(isNull);
                    chunk.Emit(OpCode.Pop);
                    CompileBase();
                    chunk.Emit(OpCode.SetVar, chunk.AddName(name));
                    chunk.PatchJump(skipAssign);
                }
                else
                {
                    var jumpOp = logOp == ScriptLex.LexTypes.AndAndEqual
                        ? OpCode.JumpIfFalseOrPop
                        : OpCode.JumpIfTrueOrPop;
                    var skip = chunk.EmitJump(jumpOp);
                    CompileBase();
                    chunk.Emit(OpCode.SetVar, chunk.AddName(name));
                    chunk.PatchJump(skip);
                }
                return;
            }

            // a++ / a-- (postfix): result is the old value
            if (lexer.TokenType is ScriptLex.LexTypes.PlusPlus or ScriptLex.LexTypes.MinusMinus)
            {
                var op = lexer.TokenType == ScriptLex.LexTypes.PlusPlus ? (ScriptLex.LexTypes)'+' : (ScriptLex.LexTypes)'-';
                lexer.Match(lexer.TokenType);
                chunk.Emit(OpCode.GetVar, chunk.AddName(name)); // old value (kept as result)
                chunk.Emit(OpCode.GetVar, chunk.AddName(name)); // value to increment
                var operandStart2 = chunk.Code.Count;
                EmitConstantInt(1);
                EmitBinary((int)op, operandStart2);
                chunk.Emit(OpCode.SetVar, chunk.AddName(name));
                chunk.Emit(OpCode.Pop);                         // discard new, leave old
                return;
            }

            // plain variable read, possibly followed by member access
            chunk.Emit(OpCode.GetVar, chunk.AddName(name));
            CompileMemberChain(canAssign);
        }

        private void CompileMemberChain(bool canAssign)
        {
            while (lexer.TokenType is (ScriptLex.LexTypes)'.' or (ScriptLex.LexTypes)'[' or (ScriptLex.LexTypes)'('
                                   or ScriptLex.LexTypes.QuestionDot)
            {
                // Optional chaining: `?.` — skip the access if the base is null/undefined.
                // JumpIfNullOrUndefined: pop; if null/undef push undefined + jump to end; else push back.
                // We collect all the optional-chain jumps so they share a single end label.
                if (lexer.TokenType == ScriptLex.LexTypes.QuestionDot)
                {
                    lexer.Match(ScriptLex.LexTypes.QuestionDot);
                    var optEnd = chunk.EmitJump(OpCode.JumpIfNullOrUndefined);

                    if (lexer.TokenType == (ScriptLex.LexTypes)'(')
                    {
                        // a?.() — call only if a is not null/undefined
                        var argc = CompileArguments();
                        if (argc < 0) chunk.Emit(OpCode.CallSpread);
                        else chunk.Emit(OpCode.Call, argc);
                    }
                    else if (lexer.TokenType == (ScriptLex.LexTypes)'[')
                    {
                        // a?.[expr]
                        lexer.Match((ScriptLex.LexTypes)'[');
                        CompileBase();
                        lexer.Match((ScriptLex.LexTypes)']');
                        chunk.Emit(OpCode.GetIndex);
                    }
                    else
                    {
                        // a?.b  or  a?.b(args) — property name may be a keyword (e.g. catch)
                        var prop = lexer.TokenString;
                        lexer.MatchPropertyName();
                        var nameIndex = chunk.AddName(prop);

                        if (lexer.TokenType == (ScriptLex.LexTypes)'(')
                        {
                            // a?.b(args) — method call with null-guard on a
                            chunk.Emit(OpCode.Dup);
                            chunk.Emit(OpCode.GetProp, nameIndex);
                            var argc = CompileArguments();
                            if (argc < 0) chunk.Emit(OpCode.CallMethodSpread);
                            else chunk.Emit(OpCode.CallMethod, argc);
                        }
                        else
                        {
                            chunk.Emit(OpCode.GetProp, nameIndex);
                        }
                    }

                    chunk.PatchJump(optEnd);
                    continue;
                }

                if (lexer.TokenType == (ScriptLex.LexTypes)'.')
                {
                    lexer.Match((ScriptLex.LexTypes)'.');
                    // Property name may be a keyword (e.g. obj.catch, obj.then, obj.delete)
                    var prop = lexer.TokenString;
                    lexer.MatchPropertyName();
                    var nameIndex = chunk.AddName(prop);

                    if (lexer.TokenType == (ScriptLex.LexTypes)'(')
                    {
                        // method call: receiver becomes `this`
                        chunk.Emit(OpCode.Dup);
                        chunk.Emit(OpCode.GetProp, nameIndex);
                        var argc = CompileArguments();
                        if (argc < 0) chunk.Emit(OpCode.CallMethodSpread);
                        else chunk.Emit(OpCode.CallMethod, argc);
                        continue;
                    }

                    if (lexer.TokenType is ScriptLex.LexTypes.PlusPlus or ScriptLex.LexTypes.MinusMinus)
                    {
                        var op = lexer.TokenType == ScriptLex.LexTypes.PlusPlus ? (ScriptLex.LexTypes)'+' : (ScriptLex.LexTypes)'-';
                        lexer.Match(lexer.TokenType);
                        chunk.Emit(OpCode.Dup);                 // keep obj
                        chunk.Emit(OpCode.GetProp, nameIndex);  // current value
                        var operandStart3 = chunk.Code.Count;
                        EmitConstantInt(1);
                        EmitBinary((int)op, operandStart3);
                        chunk.Emit(OpCode.SetProp, nameIndex);  // leaves new value
                        return;
                    }

                    if (canAssign && lexer.TokenType == (ScriptLex.LexTypes)'=')
                    {
                        lexer.Match((ScriptLex.LexTypes)'=');
                        CompileBase();
                        chunk.Emit(OpCode.SetProp, nameIndex);
                        return;
                    }

                    if (canAssign && TryGetCompoundOp(lexer.TokenType, out var baseOp, out var isShift))
                    {
                        lexer.Match(lexer.TokenType);
                        chunk.Emit(OpCode.Dup);                 // keep obj for the set
                        chunk.Emit(OpCode.GetProp, nameIndex);  // current value
                        var operandStart4 = chunk.Code.Count;
                        CompileBase();
                        EmitBinaryOrShift(baseOp, isShift, operandStart4);
                        chunk.Emit(OpCode.SetProp, nameIndex);
                        return;
                    }

                    // obj.prop &&= b / obj.prop ||= b / obj.prop ??= b
                    if (canAssign && lexer.TokenType is ScriptLex.LexTypes.AndAndEqual
                                                      or ScriptLex.LexTypes.OrOrEqual
                                                      or ScriptLex.LexTypes.NullCoalesceEqual)
                    {
                        var logOp = lexer.TokenType;
                        lexer.Match(logOp);
                        if (logOp == ScriptLex.LexTypes.NullCoalesceEqual)
                        {
                            chunk.Emit(OpCode.Dup);
                            chunk.Emit(OpCode.GetProp, nameIndex);         // [obj, old]
                            var isNull = chunk.EmitJump(OpCode.JumpIfNullOrUndefined);
                            // Not null/undef: [obj, old] — pop old, re-fetch via GetProp
                            chunk.Emit(OpCode.Pop);
                            chunk.Emit(OpCode.GetProp, nameIndex);         // [old]
                            var skipAssign = chunk.EmitJump(OpCode.Jump);
                            // Null/undef: [obj, undef] — pop undef, compile b, set
                            chunk.PatchJump(isNull);
                            chunk.Emit(OpCode.Pop);
                            CompileBase();
                            chunk.Emit(OpCode.SetProp, nameIndex);
                            chunk.PatchJump(skipAssign);
                        }
                        else
                        {
                            var jumpOp = logOp == ScriptLex.LexTypes.AndAndEqual
                                ? OpCode.JumpIfFalse
                                : OpCode.JumpIfTrue;
                            chunk.Emit(OpCode.Dup);
                            chunk.Emit(OpCode.GetProp, nameIndex);         // [obj, old]
                            var skip = chunk.EmitJump(jumpOp);             // pop old; check → [obj] or skip
                            CompileBase();                                  // [obj, b]
                            chunk.Emit(OpCode.SetProp, nameIndex);         // [b]
                            var end = chunk.EmitJump(OpCode.Jump);
                            chunk.PatchJump(skip);
                            chunk.Emit(OpCode.GetProp, nameIndex);         // [old] (consumes obj)
                            chunk.PatchJump(end);
                        }
                        return;
                    }

                    chunk.Emit(OpCode.GetProp, nameIndex);
                }
                else if (lexer.TokenType == (ScriptLex.LexTypes)'[')
                {
                    lexer.Match((ScriptLex.LexTypes)'[');
                    CompileBase();                              // key
                    lexer.Match((ScriptLex.LexTypes)']');

                    if (canAssign && lexer.TokenType == (ScriptLex.LexTypes)'=')
                    {
                        lexer.Match((ScriptLex.LexTypes)'=');
                        CompileBase();
                        chunk.Emit(OpCode.SetIndex);
                        return;
                    }

                    if (canAssign && TryGetCompoundOp(lexer.TokenType, out var baseOp, out var isShift))
                    {
                        lexer.Match(lexer.TokenType);
                        chunk.Emit(OpCode.Dup2);                // keep obj,key for the set
                        chunk.Emit(OpCode.GetIndex);            // current value
                        var operandStart5 = chunk.Code.Count;
                        CompileBase();
                        EmitBinaryOrShift(baseOp, isShift, operandStart5);
                        chunk.Emit(OpCode.SetIndex);
                        return;
                    }

                    // obj[key] &&= b / obj[key] ||= b / obj[key] ??= b
                    if (canAssign && lexer.TokenType is ScriptLex.LexTypes.AndAndEqual
                                                      or ScriptLex.LexTypes.OrOrEqual
                                                      or ScriptLex.LexTypes.NullCoalesceEqual)
                    {
                        var logOp = lexer.TokenType;
                        lexer.Match(logOp);
                        if (logOp == ScriptLex.LexTypes.NullCoalesceEqual)
                        {
                            chunk.Emit(OpCode.Dup2);
                            chunk.Emit(OpCode.GetIndex);               // [obj, key, old]
                            var isNull = chunk.EmitJump(OpCode.JumpIfNullOrUndefined);
                            // Not null/undef: [obj, key, old] — pop old, re-fetch
                            chunk.Emit(OpCode.Pop);
                            chunk.Emit(OpCode.GetIndex);               // [old] (consumes obj,key)
                            var skipAssign = chunk.EmitJump(OpCode.Jump);
                            // Null/undef: [obj, key, undef] — pop undef, compile b, set
                            chunk.PatchJump(isNull);
                            chunk.Emit(OpCode.Pop);
                            CompileBase();
                            chunk.Emit(OpCode.SetIndex);               // [b]
                            chunk.PatchJump(skipAssign);
                        }
                        else
                        {
                            var jumpOp = logOp == ScriptLex.LexTypes.AndAndEqual
                                ? OpCode.JumpIfFalse
                                : OpCode.JumpIfTrue;
                            chunk.Emit(OpCode.Dup2);
                            chunk.Emit(OpCode.GetIndex);               // [obj, key, old]
                            var skip = chunk.EmitJump(jumpOp);         // pop old; check → [obj, key] or skip
                            CompileBase();                             // [obj, key, b]
                            chunk.Emit(OpCode.SetIndex);               // [b]
                            var end = chunk.EmitJump(OpCode.Jump);
                            chunk.PatchJump(skip);
                            chunk.Emit(OpCode.GetIndex);               // [old] (consumes obj,key)
                            chunk.PatchJump(end);
                        }
                        return;
                    }

                    if (lexer.TokenType is ScriptLex.LexTypes.PlusPlus or ScriptLex.LexTypes.MinusMinus)
                    {
                        var op = lexer.TokenType == ScriptLex.LexTypes.PlusPlus ? (ScriptLex.LexTypes)'+' : (ScriptLex.LexTypes)'-';
                        lexer.Match(lexer.TokenType);
                        chunk.Emit(OpCode.Dup2);                // keep obj,key
                        chunk.Emit(OpCode.GetIndex);            // current value
                        var operandStart6 = chunk.Code.Count;
                        EmitConstantInt(1);
                        EmitBinary((int)op, operandStart6);
                        chunk.Emit(OpCode.SetIndex);            // leaves new value
                        return;
                    }

                    chunk.Emit(OpCode.GetIndex);
                }
                else // '(' : call the value already on the stack (this = undefined)
                {
                    var argc = CompileArguments();
                    if (argc < 0) chunk.Emit(OpCode.CallSpread);
                    else chunk.Emit(OpCode.Call, argc);
                }
            }
        }

        // Returns the argument count (>= 0) for normal calls, or -1 when any
        // spread argument is present (in which case a NewArray + elements are
        // on the stack instead of individual args, and the caller must emit
        // CallSpread / CallMethodSpread / NewSpread instead of Call/CallMethod/New).
        private int CompileArguments()
        {
            lexer.Match((ScriptLex.LexTypes)'(');

            // Fast-scan to see whether any spread is present at top-level depth.
            bool hasSpread;
            {
                var clone = lexer.CloneToEnd(lexer.TokenStart);
                int depth = 0;
                hasSpread = false;
                while (clone.TokenType != (ScriptLex.LexTypes)')' || depth > 0)
                {
                    if (clone.TokenType == ScriptLex.LexTypes.Eof) break;
                    if (clone.TokenType == ScriptLex.LexTypes.Ellipsis && depth == 0)
                    { hasSpread = true; break; }
                    if (clone.TokenType == (ScriptLex.LexTypes)'(' ||
                        clone.TokenType == (ScriptLex.LexTypes)'[' ||
                        clone.TokenType == (ScriptLex.LexTypes)'{') depth++;
                    else if (clone.TokenType == (ScriptLex.LexTypes)')' ||
                             clone.TokenType == (ScriptLex.LexTypes)']' ||
                             clone.TokenType == (ScriptLex.LexTypes)'}') depth--;
                    clone.GetNextToken();
                }
            }

            if (!hasSpread)
            {
                // No spread — original fast path: push args individually
                var count = 0;
                while (lexer.TokenType != (ScriptLex.LexTypes)')')
                {
                    CompileBase();
                    count++;
                    if (lexer.TokenType != (ScriptLex.LexTypes)')') lexer.Match((ScriptLex.LexTypes)',');
                }
                lexer.Match((ScriptLex.LexTypes)')');
                return count;
            }

            // Spread path: build a NewArray and append each arg
            chunk.Emit(OpCode.NewArray);
            while (lexer.TokenType != (ScriptLex.LexTypes)')')
            {
                if (lexer.TokenType == ScriptLex.LexTypes.Ellipsis)
                {
                    lexer.Match(ScriptLex.LexTypes.Ellipsis);
                    CompileBase();          // spreadArr on stack; array below
                    chunk.Emit(OpCode.PushSpread);
                }
                else
                {
                    // Non-spread arg: dynamic append via GetProp("length") + SetPropDynamic
                    chunk.Emit(OpCode.Dup);
                    chunk.Emit(OpCode.GetProp, chunk.AddName("length"));
                    CompileBase();
                    chunk.Emit(OpCode.SetPropDynamic);
                }
                if (lexer.TokenType != (ScriptLex.LexTypes)')') lexer.Match((ScriptLex.LexTypes)',');
            }
            lexer.Match((ScriptLex.LexTypes)')');
            return -1; // sentinel: caller must emit *Spread opcode
        }

        private void CompileNew()
        {
            lexer.Match(ScriptLex.LexTypes.RNew);

            var name = lexer.TokenString;
            lexer.Match(ScriptLex.LexTypes.Id);
            chunk.Emit(OpCode.GetVar, chunk.AddName(name));

            // dotted / indexed constructor reference (without calling)
            while (lexer.TokenType is (ScriptLex.LexTypes)'.' or (ScriptLex.LexTypes)'[')
            {
                if (lexer.TokenType == (ScriptLex.LexTypes)'.')
                {
                    lexer.Match((ScriptLex.LexTypes)'.');
                    var propName = lexer.TokenString;
                    lexer.MatchPropertyName();
                    chunk.Emit(OpCode.GetProp, chunk.AddName(propName));
                }
                else
                {
                    lexer.Match((ScriptLex.LexTypes)'[');
                    CompileBase();
                    lexer.Match((ScriptLex.LexTypes)']');
                    chunk.Emit(OpCode.GetIndex);
                }
            }

            if (lexer.TokenType == (ScriptLex.LexTypes)'(')
            {
                var argc = CompileArguments();
                if (argc < 0) chunk.Emit(OpCode.NewSpread);
                else chunk.Emit(OpCode.New, argc);
            }
            else
            {
                chunk.Emit(OpCode.New, 0);
            }
        }

        // Compile a function's "(params) { body }" into a nested chunk and
        // register it; returns its index in the enclosing chunk's function table.
        private int CompileFunctionRest(string name, bool isGenerator = false, bool isAsync = false)
        {
            var fnChunk = new Chunk { Name = string.IsNullOrEmpty(name) ? "<anonymous>" : name, IsGenerator = isGenerator, IsAsync = isAsync };

            // capture the source span so the function can be rendered back to
            // text by JSON.stringify / GetParsableString and re-parsed by eval
            var sourceStart = lexer.TokenStart;

            // Parse parameter list, collecting default expressions as source strings.
            var paramDefaults = new List<(string ParamName, string DefaultSrc)>();

            lexer.Match((ScriptLex.LexTypes)'(');
            while (lexer.TokenType != (ScriptLex.LexTypes)')')
            {
                // Rest parameter: ...name (must be last)
                if (lexer.TokenType == ScriptLex.LexTypes.Ellipsis)
                {
                    lexer.Match(ScriptLex.LexTypes.Ellipsis);
                    var restName = lexer.TokenString;
                    lexer.Match(ScriptLex.LexTypes.Id);
                    fnChunk.RestParamIndex = fnChunk.Parameters.Count;
                    fnChunk.Parameters.Add(restName);
                    paramDefaults.Add((restName, null));
                    break; // rest param must be last
                }

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

            // Emit default-value guards at the start of the function body.
            EmitDefaultParamGuards(paramDefaults);

            CompileBlock();
            chunk.Emit(OpCode.PushUndefined);
            chunk.Emit(OpCode.Return);
            ExitFunctionBody(savedLoops, savedFinally);
            chunk = saved;

            fnChunk.Source = "function " + name + lexer.GetSubString(sourceStart);

            return saved.AddFunction(fnChunk);
        }

        // Emit default-value guards for any parameters that carry a default expression.
        // Pattern: GetVar name; JumpIfDefined → skip; <default expr>; SetVar name; Pop; skip:
        private void EmitDefaultParamGuards(List<(string ParamName, string DefaultSrc)> paramDefaults)
        {
            foreach (var (paramName, defaultSrc) in paramDefaults)
            {
                if (defaultSrc == null) continue;

                var nameIdx = chunk.AddName(paramName);
                chunk.Emit(OpCode.GetVar, nameIdx);
                var jumpToSkip = chunk.EmitJump(OpCode.JumpIfDefined);

                var savedLexer = lexer;
                using var defaultLexer = new ScriptLex(defaultSrc);
                lexer = defaultLexer;
                CompileBase();
                lexer = savedLexer;

                chunk.Emit(OpCode.SetVar, nameIdx);
                chunk.Emit(OpCode.Pop);
                chunk.PatchJump(jumpToSkip);
            }
        }

        // Skip tokens for a default parameter expression (stopping at ',' or ')' at depth 0).
        // Returns the captured source text of the expression.
        private string ReadDefaultExpression()
        {
            var start = lexer.TokenStart;
            var depth = 0;
            while (lexer.TokenType != ScriptLex.LexTypes.Eof)
            {
                var t = lexer.TokenType;
                if (depth == 0 && (t == (ScriptLex.LexTypes)',' || t == (ScriptLex.LexTypes)')' ||
                                   t == (ScriptLex.LexTypes)']' || t == (ScriptLex.LexTypes)'}'))
                    break;
                if (t == (ScriptLex.LexTypes)'(' || t == (ScriptLex.LexTypes)'[' || t == (ScriptLex.LexTypes)'{')
                    depth++;
                else if (t == (ScriptLex.LexTypes)')' || t == (ScriptLex.LexTypes)']' || t == (ScriptLex.LexTypes)'}')
                    depth--;
                lexer.GetNextToken();
            }
            return lexer.GetSubString(start);
        }

        // Returns true if the current token sequence starts an arrow function
        // (`identifier =>` or `( params ) =>`). Uses a lexer clone so the
        // main lexer position is not disturbed.
        private bool IsArrowFunction()
        {
            if (lexer.TokenType == ScriptLex.LexTypes.Id)
            {
                var clone = lexer.CloneToEnd(lexer.TokenStart);
                clone.GetNextToken();
                return clone.TokenType == ScriptLex.LexTypes.Arrow;
            }

            if (lexer.TokenType == (ScriptLex.LexTypes)'(')
            {
                var clone = lexer.CloneToEnd(lexer.TokenStart);
                clone.GetNextToken(); // skip '('
                var depth = 1;
                while (clone.TokenType != ScriptLex.LexTypes.Eof)
                {
                    if (clone.TokenType == (ScriptLex.LexTypes)'(') depth++;
                    else if (clone.TokenType == (ScriptLex.LexTypes)')')
                    {
                        depth--;
                        if (depth == 0) break;
                    }
                    clone.GetNextToken();
                }
                clone.GetNextToken(); // skip ')'
                return clone.TokenType == ScriptLex.LexTypes.Arrow;
            }

            return false;
        }

        // Compile `x => expr`, `(x, y) => expr`, or `(params) => { block }`.
        private void CompileArrowFunction()
        {
            var fnChunk = new Chunk { Name = "<arrow>" };
            var sourceStart = lexer.TokenStart;
            var paramDefaults = new List<(string ParamName, string DefaultSrc)>();

            if (lexer.TokenType == ScriptLex.LexTypes.Id)
            {
                // Single unparenthesised param: x => body (no default supported here)
                fnChunk.Parameters.Add(lexer.TokenString);
                paramDefaults.Add((lexer.TokenString, null));
                lexer.Match(ScriptLex.LexTypes.Id);
            }
            else
            {
                // Parenthesised param list: () => body  or  (x, y) => body
                lexer.Match((ScriptLex.LexTypes)'(');
                while (lexer.TokenType != (ScriptLex.LexTypes)')')
                {
                    // Rest parameter: ...name (must be last)
                    if (lexer.TokenType == ScriptLex.LexTypes.Ellipsis)
                    {
                        lexer.Match(ScriptLex.LexTypes.Ellipsis);
                        var restName = lexer.TokenString;
                        lexer.Match(ScriptLex.LexTypes.Id);
                        fnChunk.RestParamIndex = fnChunk.Parameters.Count;
                        fnChunk.Parameters.Add(restName);
                        paramDefaults.Add((restName, null));
                        if (lexer.TokenType != (ScriptLex.LexTypes)')')
                            lexer.Match((ScriptLex.LexTypes)',');
                        break;
                    }

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
            }

            lexer.Match(ScriptLex.LexTypes.Arrow);

            var saved = chunk;
            chunk = fnChunk;
            EnterFunctionBody(out var savedLoops, out var savedFinally);

            // Emit default-value guards at the start of the function body.
            EmitDefaultParamGuards(paramDefaults);

            if (lexer.TokenType == (ScriptLex.LexTypes)'{')
            {
                // Block body: (x) => { statements; }
                CompileBlock();
                chunk.Emit(OpCode.PushUndefined);
                chunk.Emit(OpCode.Return);
            }
            else
            {
                // Expression body: (x) => expr  — implicit return
                CompileBase();
                chunk.Emit(OpCode.Return);
            }

            ExitFunctionBody(savedLoops, savedFinally);
            chunk = saved;

            fnChunk.Source = lexer.GetSubString(sourceStart);

            var idx = chunk.AddFunction(fnChunk);
            chunk.Emit(OpCode.MakeClosure, idx);
            chunk.MakesClosure = true;
        }

        // Isolate the new function body from the enclosing function's loop and
        // finally contexts so that break/continue/return inside the body don't
        // accidentally target the outer function's control structures.
        private void EnterFunctionBody(out Stack<LoopContext> savedLoops,
                                       out Stack<FinallyContext> savedFinally)
        {
            savedLoops   = loops;
            savedFinally = finallyStack;
            loops        = new Stack<LoopContext>();
            finallyStack = new Stack<FinallyContext>();
        }

        private void ExitFunctionBody(Stack<LoopContext> savedLoops,
                                      Stack<FinallyContext> savedFinally)
        {
            loops        = savedLoops;
            finallyStack = savedFinally;
        }

        // Compile a template literal `...${expr}...` into a sequence of string
        // constants and expressions joined by the + (string-concatenation) operator.
        private void CompileTemplateLiteral()
        {
            var raw = lexer.TokenString;
            lexer.Match(ScriptLex.LexTypes.TemplateLiteral);

            // Split raw into alternating literal/expression segments and collect them.
            var parts = new System.Collections.Generic.List<(bool IsExpr, string Text)>();
            var sb = new System.Text.StringBuilder();
            var i = 0;

            while (i < raw.Length)
            {
                if (raw[i] == '\\' && i + 1 < raw.Length)
                {
                    switch (raw[i + 1])
                    {
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case '\\': sb.Append('\\'); break;
                        case '`':  sb.Append('`');  break;
                        case '$':  sb.Append('$');  break; // \$ → literal, no interpolation
                        default:   sb.Append('\\'); sb.Append(raw[i + 1]); break;
                    }
                    i += 2;
                }
                else if (raw[i] == '$' && i + 1 < raw.Length && raw[i + 1] == '{')
                {
                    parts.Add((false, sb.ToString()));
                    sb.Clear();
                    i += 2; // skip '${'

                    // Find matching '}', tracking { } depth.
                    int depth = 1, exprStart = i;
                    while (i < raw.Length && depth > 0)
                    {
                        if (raw[i] == '{') depth++;
                        else if (raw[i] == '}') { if (--depth == 0) break; }
                        i++;
                    }
                    parts.Add((true, raw.Substring(exprStart, i - exprStart)));
                    if (i < raw.Length) i++; // skip '}'
                }
                else
                {
                    sb.Append(raw[i]);
                    i++;
                }
            }
            parts.Add((false, sb.ToString())); // trailing literal (possibly empty)

            // Emit code: push each part; concatenate all with Binary '+'.
            // Skip leading/trailing empty literal strings where possible.
            var emitted = 0;
            foreach (var (isExpr, text) in parts)
            {
                if (isExpr)
                {
                    var savedLexer = lexer;
                    using var subLexer = new ScriptLex(text);
                    lexer = subLexer;
                    CompileBase();
                    lexer = savedLexer;
                }
                else if (emitted == 0 || text.Length > 0)
                {
                    // Always emit the first part (even if empty) to guarantee a string
                    // is on the stack; skip subsequent empty literal segments.
                    chunk.Emit(OpCode.Constant,
                        chunk.AddConstant(ConstantValue.String(text)));
                }
                else
                {
                    // Empty literal segment between/after expressions: skip it.
                    continue;
                }

                if (emitted > 0) chunk.Emit(OpCode.Binary, (int)(ScriptLex.LexTypes)'+');
                emitted++;
            }
        }

        private void CompileObjectLiteral()
        {
            lexer.Match((ScriptLex.LexTypes)'{');
            chunk.Emit(OpCode.NewObject);

            while (lexer.TokenType != (ScriptLex.LexTypes)'}')
            {
                // Spread property: { ...expr }
                if (lexer.TokenType == ScriptLex.LexTypes.Ellipsis)
                {
                    lexer.Match(ScriptLex.LexTypes.Ellipsis);
                    CompileBase();              // source object on stack, target below
                    chunk.Emit(OpCode.MergeObject);
                }
                // Computed property: { [expr]: value }
                else if (lexer.TokenType == (ScriptLex.LexTypes)'[')
                {
                    lexer.Match((ScriptLex.LexTypes)'[');
                    CompileBase();                  // key expression → stack: [obj, key]
                    lexer.Match((ScriptLex.LexTypes)']');
                    lexer.Match((ScriptLex.LexTypes)':');
                    CompileBase();                  // value → stack: [obj, key, value]
                    chunk.Emit(OpCode.SetPropDynamic); // obj[key] = value; stack: [obj]
                }
                else
                {
                    var name = lexer.TokenString;

                    if (lexer.TokenType == ScriptLex.LexTypes.Str)
                        lexer.Match(ScriptLex.LexTypes.Str);
                    else
                        lexer.Match(ScriptLex.LexTypes.Id);

                    // Shorthand property: `{ x, y }` — no colon, use var with same name as key
                    if (lexer.TokenType == (ScriptLex.LexTypes)',' || lexer.TokenType == (ScriptLex.LexTypes)'}')
                        chunk.Emit(OpCode.GetVar, chunk.AddName(name));
                    else
                    {
                        lexer.Match((ScriptLex.LexTypes)':');
                        CompileBase();
                    }

                    chunk.Emit(OpCode.InitProp, chunk.AddName(name));
                }

                if (lexer.TokenType != (ScriptLex.LexTypes)'}')
                {
                    lexer.Match((ScriptLex.LexTypes)',');
                }
            }

            lexer.Match((ScriptLex.LexTypes)'}');
        }

        private void CompileArrayLiteral()
        {
            lexer.Match((ScriptLex.LexTypes)'[');
            chunk.Emit(OpCode.NewArray);

            // Single-pass parse with seenSpread flag — no lexer clone needed.
            // Elements before the first spread use compile-time indices (InitElem),
            // avoiding both a full re-parse and O(n) runtime GetProp "length" calls.
            // Elements after any spread fall back to the runtime-length append path.
            var staticCount = 0;
            var seenSpread = false;

            while (lexer.TokenType != (ScriptLex.LexTypes)']')
            {
                if (lexer.TokenType == ScriptLex.LexTypes.Ellipsis)
                {
                    lexer.Match(ScriptLex.LexTypes.Ellipsis);
                    CompileBase();
                    chunk.Emit(OpCode.PushSpread);
                    seenSpread = true;
                }
                else if (!seenSpread)
                {
                    CompileBase();
                    chunk.Emit(OpCode.InitElem, staticCount);
                    staticCount++;
                }
                else
                {
                    chunk.Emit(OpCode.Dup);
                    chunk.Emit(OpCode.GetProp, chunk.AddName("length"));
                    CompileBase();
                    chunk.Emit(OpCode.SetPropDynamic);
                }

                if (lexer.TokenType != (ScriptLex.LexTypes)']')
                    lexer.Match((ScriptLex.LexTypes)',');
            }

            lexer.Match((ScriptLex.LexTypes)']');
        }

        private static bool TryGetCompoundOp(ScriptLex.LexTypes token, out ScriptLex.LexTypes baseOp, out bool isShift)
        {
            isShift = false;
            switch (token)
            {
                case ScriptLex.LexTypes.PlusEqual: baseOp = (ScriptLex.LexTypes)'+'; return true;
                case ScriptLex.LexTypes.MinusEqual: baseOp = (ScriptLex.LexTypes)'-'; return true;
                case ScriptLex.LexTypes.TimesEqual: baseOp = (ScriptLex.LexTypes)'*'; return true;
                case ScriptLex.LexTypes.SlashEqual: baseOp = (ScriptLex.LexTypes)'/'; return true;
                case ScriptLex.LexTypes.PercentEqual: baseOp = (ScriptLex.LexTypes)'%'; return true;
                case ScriptLex.LexTypes.AndEqual: baseOp = (ScriptLex.LexTypes)'&'; return true;
                case ScriptLex.LexTypes.OrEqual: baseOp = (ScriptLex.LexTypes)'|'; return true;
                case ScriptLex.LexTypes.XorEqual: baseOp = (ScriptLex.LexTypes)'^'; return true;
                case ScriptLex.LexTypes.LShiftEqual: baseOp = ScriptLex.LexTypes.LShift; isShift = true; return true;
                case ScriptLex.LexTypes.RShiftEqual: baseOp = ScriptLex.LexTypes.RShift; isShift = true; return true;
                case ScriptLex.LexTypes.RShiftUnsignedEqual: baseOp = ScriptLex.LexTypes.RShiftUnsigned; isShift = true; return true;
                default: baseOp = default; return false;
            }
        }

        private void EmitBinaryOrShift(ScriptLex.LexTypes baseOp, bool isShift, int operandStart = -1)
        {
            if (isShift)
                chunk.Emit(OpCode.Shift, (int)baseOp);
            else
                EmitBinary((int)baseOp, operandStart >= 0 ? operandStart : chunk.Code.Count - 1);
        }
    }
}
