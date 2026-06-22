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
                    return;

                case ScriptLex.LexTypes.RTrue:
                    lexer.Match(ScriptLex.LexTypes.RTrue);
                    chunk.Emit(OpCode.PushTrue);
                    return;

                case ScriptLex.LexTypes.RFalse:
                    lexer.Match(ScriptLex.LexTypes.RFalse);
                    chunk.Emit(OpCode.PushFalse);
                    return;

                case ScriptLex.LexTypes.RNull:
                    lexer.Match(ScriptLex.LexTypes.RNull);
                    chunk.Emit(OpCode.PushNull);
                    return;

                case ScriptLex.LexTypes.RUndefined:
                    lexer.Match(ScriptLex.LexTypes.RUndefined);
                    chunk.Emit(OpCode.PushUndefined);
                    return;

                case ScriptLex.LexTypes.Int:
                {
                    var value = new ScriptVar(lexer.TokenString, ScriptVar.Flags.Integer).Int;
                    lexer.Match(ScriptLex.LexTypes.Int);
                    EmitConstantInt(value);
                    return;
                }

                case ScriptLex.LexTypes.Float:
                {
                    var value = new ScriptVar(lexer.TokenString, ScriptVar.Flags.Double).Float;
                    lexer.Match(ScriptLex.LexTypes.Float);
                    chunk.Emit(OpCode.Constant, chunk.AddConstant(ConstantValue.Double(value)));
                    return;
                }

                case ScriptLex.LexTypes.Str:
                {
                    var value = lexer.TokenString;
                    lexer.Match(ScriptLex.LexTypes.Str);
                    chunk.Emit(OpCode.Constant, chunk.AddConstant(ConstantValue.String(value)));
                    return;
                }

                case ScriptLex.LexTypes.RegExp:
                {
                    var value = lexer.TokenString;
                    lexer.Match(ScriptLex.LexTypes.RegExp);
                    chunk.Emit(OpCode.Constant, chunk.AddConstant(ConstantValue.Regex(value)));
                    return;
                }

                case ScriptLex.LexTypes.TemplateLiteral:
                    CompileTemplateLiteral();
                    return;

                case (ScriptLex.LexTypes)'{':
                    CompileObjectLiteral();
                    return;

                case (ScriptLex.LexTypes)'[':
                    CompileArrayLiteral();
                    return;

                case ScriptLex.LexTypes.RFunction:
                {
                    lexer.Match(ScriptLex.LexTypes.RFunction);
                    var fnName = string.Empty;
                    if (lexer.TokenType == ScriptLex.LexTypes.Id)
                    {
                        fnName = lexer.TokenString;
                        lexer.Match(ScriptLex.LexTypes.Id);
                    }
                    var idx = CompileFunctionRest(fnName);
                    chunk.Emit(OpCode.MakeClosure, idx);
                    chunk.MakesClosure = true;
                    return;
                }

                case ScriptLex.LexTypes.RNew:
                    CompileNew();
                    return;

                case ScriptLex.LexTypes.Id:
                    CompileIdentifierChain(canAssign);
                    return;
            }

            // unexpected token for the current (phase-limited) grammar
            lexer.Match(ScriptLex.LexTypes.Eof);
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
                CompileBase();
                EmitBinaryOrShift(baseOp, isShift);
                chunk.Emit(OpCode.SetVar, chunk.AddName(name));
                return;
            }

            // a++ / a-- (postfix): result is the old value
            if (lexer.TokenType is ScriptLex.LexTypes.PlusPlus or ScriptLex.LexTypes.MinusMinus)
            {
                var op = lexer.TokenType == ScriptLex.LexTypes.PlusPlus ? (ScriptLex.LexTypes)'+' : (ScriptLex.LexTypes)'-';
                lexer.Match(lexer.TokenType);
                chunk.Emit(OpCode.GetVar, chunk.AddName(name)); // old value (kept as result)
                chunk.Emit(OpCode.GetVar, chunk.AddName(name)); // value to increment
                EmitConstantInt(1);
                chunk.Emit(OpCode.Binary, (int)op);
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
            while (lexer.TokenType is (ScriptLex.LexTypes)'.' or (ScriptLex.LexTypes)'[' or (ScriptLex.LexTypes)'(')
            {
                if (lexer.TokenType == (ScriptLex.LexTypes)'.')
                {
                    lexer.Match((ScriptLex.LexTypes)'.');
                    var prop = lexer.TokenString;
                    lexer.Match(ScriptLex.LexTypes.Id);
                    var nameIndex = chunk.AddName(prop);

                    if (lexer.TokenType == (ScriptLex.LexTypes)'(')
                    {
                        // method call: receiver becomes `this`
                        chunk.Emit(OpCode.Dup);
                        chunk.Emit(OpCode.GetProp, nameIndex);
                        var argc = CompileArguments();
                        chunk.Emit(OpCode.CallMethod, argc);
                        continue;
                    }

                    if (lexer.TokenType is ScriptLex.LexTypes.PlusPlus or ScriptLex.LexTypes.MinusMinus)
                    {
                        var op = lexer.TokenType == ScriptLex.LexTypes.PlusPlus ? (ScriptLex.LexTypes)'+' : (ScriptLex.LexTypes)'-';
                        lexer.Match(lexer.TokenType);
                        chunk.Emit(OpCode.Dup);                 // keep obj
                        chunk.Emit(OpCode.GetProp, nameIndex);  // current value
                        EmitConstantInt(1);
                        chunk.Emit(OpCode.Binary, (int)op);
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
                        CompileBase();
                        EmitBinaryOrShift(baseOp, isShift);
                        chunk.Emit(OpCode.SetProp, nameIndex);
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
                        CompileBase();
                        EmitBinaryOrShift(baseOp, isShift);
                        chunk.Emit(OpCode.SetIndex);
                        return;
                    }

                    if (lexer.TokenType is ScriptLex.LexTypes.PlusPlus or ScriptLex.LexTypes.MinusMinus)
                    {
                        var op = lexer.TokenType == ScriptLex.LexTypes.PlusPlus ? (ScriptLex.LexTypes)'+' : (ScriptLex.LexTypes)'-';
                        lexer.Match(lexer.TokenType);
                        chunk.Emit(OpCode.Dup2);                // keep obj,key
                        chunk.Emit(OpCode.GetIndex);            // current value
                        EmitConstantInt(1);
                        chunk.Emit(OpCode.Binary, (int)op);
                        chunk.Emit(OpCode.SetIndex);            // leaves new value
                        return;
                    }

                    chunk.Emit(OpCode.GetIndex);
                }
                else // '(' : call the value already on the stack (this = undefined)
                {
                    var argc = CompileArguments();
                    chunk.Emit(OpCode.Call, argc);
                }
            }
        }

        private int CompileArguments()
        {
            lexer.Match((ScriptLex.LexTypes)'(');
            var count = 0;
            while (lexer.TokenType != (ScriptLex.LexTypes)')')
            {
                CompileBase();
                count++;
                if (lexer.TokenType != (ScriptLex.LexTypes)')')
                {
                    lexer.Match((ScriptLex.LexTypes)',');
                }
            }
            lexer.Match((ScriptLex.LexTypes)')');
            return count;
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
                    chunk.Emit(OpCode.GetProp, chunk.AddName(lexer.TokenString));
                    lexer.Match(ScriptLex.LexTypes.Id);
                }
                else
                {
                    lexer.Match((ScriptLex.LexTypes)'[');
                    CompileBase();
                    lexer.Match((ScriptLex.LexTypes)']');
                    chunk.Emit(OpCode.GetIndex);
                }
            }

            var argc = 0;
            if (lexer.TokenType == (ScriptLex.LexTypes)'(')
            {
                argc = CompileArguments();
            }

            chunk.Emit(OpCode.New, argc);
        }

        // Compile a function's "(params) { body }" into a nested chunk and
        // register it; returns its index in the enclosing chunk's function table.
        private int CompileFunctionRest(string name)
        {
            var fnChunk = new Chunk { Name = string.IsNullOrEmpty(name) ? "<anonymous>" : name };

            // capture the source span so the function can be rendered back to
            // text by JSON.stringify / GetParsableString and re-parsed by eval
            var sourceStart = lexer.TokenStart;

            lexer.Match((ScriptLex.LexTypes)'(');
            while (lexer.TokenType != (ScriptLex.LexTypes)')')
            {
                fnChunk.Parameters.Add(lexer.TokenString);
                lexer.Match(ScriptLex.LexTypes.Id);
                if (lexer.TokenType != (ScriptLex.LexTypes)')')
                {
                    lexer.Match((ScriptLex.LexTypes)',');
                }
            }
            lexer.Match((ScriptLex.LexTypes)')');

            var saved = chunk;
            chunk = fnChunk;
            EnterFunctionBody(out var savedLoops, out var savedFinally);
            CompileBlock();
            chunk.Emit(OpCode.PushUndefined);
            chunk.Emit(OpCode.Return);
            ExitFunctionBody(savedLoops, savedFinally);
            chunk = saved;

            fnChunk.Source = "function " + name + lexer.GetSubString(sourceStart);

            return saved.AddFunction(fnChunk);
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

            if (lexer.TokenType == ScriptLex.LexTypes.Id)
            {
                // Single unparenthesised param: x => body
                fnChunk.Parameters.Add(lexer.TokenString);
                lexer.Match(ScriptLex.LexTypes.Id);
            }
            else
            {
                // Parenthesised param list: () => body  or  (x, y) => body
                lexer.Match((ScriptLex.LexTypes)'(');
                while (lexer.TokenType != (ScriptLex.LexTypes)')')
                {
                    fnChunk.Parameters.Add(lexer.TokenString);
                    lexer.Match(ScriptLex.LexTypes.Id);
                    if (lexer.TokenType != (ScriptLex.LexTypes)')')
                        lexer.Match((ScriptLex.LexTypes)',');
                }
                lexer.Match((ScriptLex.LexTypes)')');
            }

            lexer.Match(ScriptLex.LexTypes.Arrow);

            var saved = chunk;
            chunk = fnChunk;
            EnterFunctionBody(out var savedLoops, out var savedFinally);

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
                var name = lexer.TokenString;

                if (lexer.TokenType == ScriptLex.LexTypes.Str)
                {
                    lexer.Match(ScriptLex.LexTypes.Str);
                }
                else
                {
                    lexer.Match(ScriptLex.LexTypes.Id);
                }

                lexer.Match((ScriptLex.LexTypes)':');

                CompileBase();
                chunk.Emit(OpCode.InitProp, chunk.AddName(name));

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

            var index = 0;
            while (lexer.TokenType != (ScriptLex.LexTypes)']')
            {
                CompileBase();
                chunk.Emit(OpCode.InitElem, index);

                if (lexer.TokenType != (ScriptLex.LexTypes)']')
                {
                    lexer.Match((ScriptLex.LexTypes)',');
                }

                index++;
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

        private void EmitBinaryOrShift(ScriptLex.LexTypes baseOp, bool isShift)
        {
            chunk.Emit(isShift ? OpCode.Shift : OpCode.Binary, (int)baseOp);
        }
    }
}
