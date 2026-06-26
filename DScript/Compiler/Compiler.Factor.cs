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
using System.Numerics;
using DScript.Vm;

namespace DScript.Compiler
{
    public sealed partial class DScriptCompiler
    {
        private void CompileFactor(bool canAssign)
        {
            // These two must be checked before the arrow-function and identifier cases.
            if (lexer.TokenType == ScriptLex.LexTypes.RYield) { CompileYieldExpression(); return; }
            if (lexer.TokenType == ScriptLex.LexTypes.RAwait) { CompileAwaitExpression(); return; }

            // Arrow function: `x => body` or `(params) => body` — must be checked before
            // the '(' and Id cases so the disambiguation runs before any tokens are consumed.
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
                    CompileExpression();   // grouping allows the comma operator: (a, b)
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
                    if (chunk.IsStrict && IsLegacyOctal(lexer.TokenString))
                        throw new ScriptException("SyntaxError: Octal literals are not allowed in strict mode");
                    var tok = lexer.TokenString;
                    lexer.Match(ScriptLex.LexTypes.Int);
                    // JS has no int type — a literal that exceeds int32 is a double,
                    // not an overflow. int32 is only DScript's small-value fast path.
                    if (ScriptVar.TryParseIntegerLiteral(tok, out var intLit, out var dblLit))
                        EmitConstantInt(intLit);
                    else
                        chunk.Emit(OpCode.Constant, chunk.AddConstant(ConstantValue.Double(dblLit)));
                    break;
                }
                case ScriptLex.LexTypes.Float:
                {
                    var value = ScriptVar.ParseLiteral(lexer.TokenString, ScriptVar.Flags.Double).Float;
                    lexer.Match(ScriptLex.LexTypes.Float);
                    chunk.Emit(OpCode.Constant, chunk.AddConstant(ConstantValue.Double(value)));
                    break;
                }
                case ScriptLex.LexTypes.BigIntLiteral:
                    CompileBigIntLiteral();
                    break;
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
                    CompileAsyncFunctionExpression();
                    break;
                case ScriptLex.LexTypes.RFunction:
                    CompileFunctionExpression();
                    break;
                case ScriptLex.LexTypes.RNew:
                    CompileNew();
                    break;
                case ScriptLex.LexTypes.RSuper:
                    CompileSuper();
                    return;
                case ScriptLex.LexTypes.Id:
                    CompileIdentifierChain(canAssign);
                    return;
                case ScriptLex.LexTypes.PrivateName:
                    CompilePrivateNameExpression();
                    return;
                case ScriptLex.LexTypes.RImport:
                    CompileImportExpression();
                    break;
                default:
                    lexer.Match(ScriptLex.LexTypes.Eof);
                    return;
            }

            // Allow member access / calls on any primary expression:
            // 'str'.method(), [1,2].indexOf(x), (expr).prop, new Foo().bar, etc.
            CompileMemberChain(false);
        }

        private void CompileYieldExpression()
        {
            lexer.Match(ScriptLex.LexTypes.RYield);
            if (lexer.TokenType != (ScriptLex.LexTypes)';' &&
                lexer.TokenType != (ScriptLex.LexTypes)'}' &&
                lexer.TokenType != ScriptLex.LexTypes.Eof)
                CompileBase();
            else
                chunk.Emit(OpCode.PushUndefined);
            chunk.Emit(OpCode.Yield);
        }

        // Compiles identically to yield; the async driver intercepts the yielded
        // Promise and resumes with the resolved value.
        private void CompileAwaitExpression()
        {
            lexer.Match(ScriptLex.LexTypes.RAwait);
            if (lexer.TokenType != (ScriptLex.LexTypes)';' &&
                lexer.TokenType != (ScriptLex.LexTypes)'}' &&
                lexer.TokenType != ScriptLex.LexTypes.Eof)
                CompileBase();
            else
                chunk.Emit(OpCode.PushUndefined);
            chunk.Emit(OpCode.Yield);
        }

        private void CompileBigIntLiteral()
        {
            var raw = lexer.TokenString;
            lexer.Match(ScriptLex.LexTypes.BigIntLiteral);
            BigInteger bigVal;
            if (raw.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase))
                bigVal = BigInteger.Parse("0" + raw[2..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
            else if (raw.StartsWith("0b", System.StringComparison.OrdinalIgnoreCase))
            {
                bigVal = BigInteger.Zero;
                foreach (var ch in raw[2..])
                    bigVal = bigVal * 2 + (ch == '1' ? BigInteger.One : BigInteger.Zero);
            }
            else if (raw.StartsWith("0o", System.StringComparison.OrdinalIgnoreCase))
            {
                bigVal = BigInteger.Zero;
                foreach (var ch in raw[2..])
                    bigVal = bigVal * 8 + (ch - '0');
            }
            else
                bigVal = BigInteger.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
            chunk.Emit(OpCode.Constant, chunk.AddConstant(ConstantValue.BigInt(bigVal)));
        }

        private void CompileAsyncFunctionExpression()
        {
            lexer.Match(ScriptLex.LexTypes.RAsync);

            // async () => body  or  async x => body  (async arrow function)
            if (IsArrowFunction())
            {
                CompileArrowFunction(isAsync: true);
                return;
            }

            lexer.Match(ScriptLex.LexTypes.RFunction);
            var isAsyncGen = lexer.TokenType == (ScriptLex.LexTypes)'*';
            if (isAsyncGen) lexer.Match((ScriptLex.LexTypes)'*');
            var fnName = string.Empty;
            if (lexer.TokenType == ScriptLex.LexTypes.Id)
            {
                fnName = lexer.TokenString;
                lexer.Match(ScriptLex.LexTypes.Id);
            }
            var idx = CompileFunctionRest(fnName, isGenerator: isAsyncGen, isAsync: true);
            chunk.Emit(OpCode.MakeClosure, idx);
            chunk.MakesClosure = true;
        }

        private void CompileFunctionExpression()
        {
            lexer.Match(ScriptLex.LexTypes.RFunction);
            var isGenerator = lexer.TokenType == (ScriptLex.LexTypes)'*';
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
        }

        // #name used as an expression is only valid in `#name in obj`
        private void CompilePrivateNameExpression()
        {
            var privateName = lexer.TokenString;
            lexer.Match(ScriptLex.LexTypes.PrivateName);
            chunk.Emit(OpCode.Constant, chunk.AddConstant(ConstantValue.String(privateName)));
            CompileMemberChain(false);
        }

        private void CompileImportExpression()
        {
            lexer.Match(ScriptLex.LexTypes.RImport);
            if (lexer.TokenType == (ScriptLex.LexTypes)'(')
            {
                lexer.Match((ScriptLex.LexTypes)'(');
                CompileBase();
                lexer.Match((ScriptLex.LexTypes)')');
                chunk.Emit(OpCode.DynamicImport);
            }
            else
            {
                // import.meta
                lexer.Match((ScriptLex.LexTypes)'.');
                if (lexer.TokenString != "meta")
                    throw new JITException("Expected 'meta' or '(' after 'import'");
                lexer.Match(ScriptLex.LexTypes.Id);
                chunk.Emit(OpCode.PushImportMeta);
            }
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
            // Substitute a propagated constant directly rather than emitting a
            // GetVar, so downstream peephole passes can fold the expression.
            if (EnableOptimizer && TryGetPropagatedConst(name, out var cv))
                chunk.Emit(OpCode.Constant, chunk.AddConstant(cv));
            else
                chunk.Emit(OpCode.GetVar, chunk.AddName(name));
            CompileMemberChain(canAssign);
        }

        private void CompileMemberChain(bool canAssign)
        {
            while (lexer.TokenType is (ScriptLex.LexTypes)'.' or (ScriptLex.LexTypes)'[' or (ScriptLex.LexTypes)'('
                                   or ScriptLex.LexTypes.QuestionDot
                                   or ScriptLex.LexTypes.TemplateLiteral)
            {
                if (lexer.TokenType == ScriptLex.LexTypes.QuestionDot)
                {
                    CompileOptionalChainSegment();
                    continue;
                }

                if (lexer.TokenType == (ScriptLex.LexTypes)'.')
                {
                    if (CompilePropSegment(canAssign)) return;
                }
                else if (lexer.TokenType == (ScriptLex.LexTypes)'[')
                {
                    if (CompileIndexSegment(canAssign)) return;
                }
                else if (lexer.TokenType == ScriptLex.LexTypes.TemplateLiteral)
                {
                    CompileTaggedTemplateLiteralCall();
                }
                else // '(' : call the value already on the stack (this = undefined)
                {
                    var argc = CompileArguments();
                    if (argc < 0) chunk.Emit(OpCode.CallSpread);
                    else chunk.Emit(OpCode.Call, argc);
                }
            }
        }

        // Optional chaining `?.` — skip the access if the base is null/undefined.
        private void CompileOptionalChainSegment()
        {
            lexer.Match(ScriptLex.LexTypes.QuestionDot);
            var optEnd = chunk.EmitJump(OpCode.JumpIfNullOrUndefined);

            if (lexer.TokenType == (ScriptLex.LexTypes)'(')
            {
                var argc = CompileArguments();
                if (argc < 0) chunk.Emit(OpCode.CallSpread);
                else chunk.Emit(OpCode.Call, argc);
            }
            else if (lexer.TokenType == (ScriptLex.LexTypes)'[')
            {
                lexer.Match((ScriptLex.LexTypes)'[');
                CompileBase();
                lexer.Match((ScriptLex.LexTypes)']');
                chunk.Emit(OpCode.GetIndex);
            }
            else
            {
                // a?.b  or  a?.b(args)
                var prop = lexer.TokenString;
                lexer.MatchPropertyName();
                var nameIndex = chunk.AddName(prop);

                if (lexer.TokenType == (ScriptLex.LexTypes)'(')
                {
                    var getpropAt2 = chunk.Code.Count;
                    chunk.Emit(OpCode.GetPropMethod, nameIndex);
                    var argc = CompileArguments();
                    if (argc == 0)
                        chunk.Code[getpropAt2] = (byte)OpCode.GetPropCall0;
                    else if (argc < 0)
                        chunk.Emit(OpCode.CallMethodSpread);
                    else
                        chunk.Emit(OpCode.CallMethod, argc);
                }
                else
                {
                    chunk.Emit(OpCode.GetProp, nameIndex);
                }
            }

            chunk.PatchJump(optEnd);
        }

        // Compiles a `.prop` segment. Returns true when a mutation terminates the chain.
        private bool CompilePropSegment(bool canAssign)
        {
            lexer.Match((ScriptLex.LexTypes)'.');
            var prop = lexer.TokenString;
            lexer.MatchPropertyName();
            if (prop.Length > 0 && prop[0] == '#' &&
                (_currentClassPrivateNames == null || !_currentClassPrivateNames.Contains(prop)))
                throw new JITException($"Private field '{prop}' must be declared in an enclosing class");
            var nameIndex = chunk.AddName(prop);

            if (lexer.TokenType == (ScriptLex.LexTypes)'(')
            {
                var getpropAt = chunk.Code.Count;
                chunk.Emit(OpCode.GetPropMethod, nameIndex);
                var argc = CompileArguments();
                if (argc == 0)
                    chunk.Code[getpropAt] = (byte)OpCode.GetPropCall0; // inline 0-arg call
                else if (argc < 0)
                    chunk.Emit(OpCode.CallMethodSpread);
                else
                    chunk.Emit(OpCode.CallMethod, argc);
                return false;
            }

            if (lexer.TokenType is ScriptLex.LexTypes.PlusPlus or ScriptLex.LexTypes.MinusMinus)
            {
                var op = lexer.TokenType == ScriptLex.LexTypes.PlusPlus ? (ScriptLex.LexTypes)'+' : (ScriptLex.LexTypes)'-';
                lexer.Match(lexer.TokenType);
                chunk.Emit(OpCode.Dup);
                chunk.Emit(OpCode.GetProp, nameIndex);
                var operandStart = chunk.Code.Count;
                EmitConstantInt(1);
                EmitBinary((int)op, operandStart);
                chunk.Emit(OpCode.SetProp, nameIndex);
                return true;
            }

            if (canAssign && lexer.TokenType == (ScriptLex.LexTypes)'=')
            {
                lexer.Match((ScriptLex.LexTypes)'=');
                CompileBase();
                chunk.Emit(OpCode.SetProp, nameIndex);
                return true;
            }

            if (canAssign && TryGetCompoundOp(lexer.TokenType, out var baseOp, out var isShift))
            {
                lexer.Match(lexer.TokenType);
                chunk.Emit(OpCode.Dup);
                chunk.Emit(OpCode.GetProp, nameIndex);
                var operandStart = chunk.Code.Count;
                CompileBase();
                EmitBinaryOrShift(baseOp, isShift, operandStart);
                chunk.Emit(OpCode.SetProp, nameIndex);
                return true;
            }

            if (canAssign && lexer.TokenType is ScriptLex.LexTypes.AndAndEqual
                                              or ScriptLex.LexTypes.OrOrEqual
                                              or ScriptLex.LexTypes.NullCoalesceEqual)
            {
                EmitPropLogicalAssign(nameIndex);
                return true;
            }

            chunk.Emit(OpCode.GetProp, nameIndex);
            return false;
        }

        // Compiles a `[key]` segment. Returns true when a mutation terminates the chain.
        private bool CompileIndexSegment(bool canAssign)
        {
            lexer.Match((ScriptLex.LexTypes)'[');
            CompileBase();
            lexer.Match((ScriptLex.LexTypes)']');

            if (canAssign && lexer.TokenType == (ScriptLex.LexTypes)'=')
            {
                lexer.Match((ScriptLex.LexTypes)'=');
                CompileBase();
                chunk.Emit(OpCode.SetIndex);
                return true;
            }

            if (canAssign && TryGetCompoundOp(lexer.TokenType, out var baseOp, out var isShift))
            {
                lexer.Match(lexer.TokenType);
                chunk.Emit(OpCode.Dup2);
                chunk.Emit(OpCode.GetIndex);
                var operandStart = chunk.Code.Count;
                CompileBase();
                EmitBinaryOrShift(baseOp, isShift, operandStart);
                chunk.Emit(OpCode.SetIndex);
                return true;
            }

            if (canAssign && lexer.TokenType is ScriptLex.LexTypes.AndAndEqual
                                              or ScriptLex.LexTypes.OrOrEqual
                                              or ScriptLex.LexTypes.NullCoalesceEqual)
            {
                EmitIndexLogicalAssign();
                return true;
            }

            if (lexer.TokenType is ScriptLex.LexTypes.PlusPlus or ScriptLex.LexTypes.MinusMinus)
            {
                var op = lexer.TokenType == ScriptLex.LexTypes.PlusPlus ? (ScriptLex.LexTypes)'+' : (ScriptLex.LexTypes)'-';
                lexer.Match(lexer.TokenType);
                chunk.Emit(OpCode.Dup2);
                chunk.Emit(OpCode.GetIndex);
                var operandStart = chunk.Code.Count;
                EmitConstantInt(1);
                EmitBinary((int)op, operandStart);
                chunk.Emit(OpCode.SetIndex);
                return true;
            }

            if (lexer.TokenType == (ScriptLex.LexTypes)'(')
            {
                chunk.Emit(OpCode.GetIndexMethod);
                var argc = CompileArguments();
                if (argc < 0) chunk.Emit(OpCode.CallMethodSpread);
                else chunk.Emit(OpCode.CallMethod, argc);
                return false;
            }

            chunk.Emit(OpCode.GetIndex);
            return false;
        }

        // obj.prop &&= / ||= / ??= b
        private void EmitPropLogicalAssign(int nameIndex)
        {
            var logOp = lexer.TokenType;
            lexer.Match(logOp);
            if (logOp == ScriptLex.LexTypes.NullCoalesceEqual)
            {
                chunk.Emit(OpCode.Dup);
                chunk.Emit(OpCode.GetProp, nameIndex);
                var isNull = chunk.EmitJump(OpCode.JumpIfNullOrUndefined);
                chunk.Emit(OpCode.Pop);
                chunk.Emit(OpCode.GetProp, nameIndex);
                var skipAssign = chunk.EmitJump(OpCode.Jump);
                chunk.PatchJump(isNull);
                chunk.Emit(OpCode.Pop);
                CompileBase();
                chunk.Emit(OpCode.SetProp, nameIndex);
                chunk.PatchJump(skipAssign);
            }
            else
            {
                var jumpOp = logOp == ScriptLex.LexTypes.AndAndEqual ? OpCode.JumpIfFalse : OpCode.JumpIfTrue;
                chunk.Emit(OpCode.Dup);
                chunk.Emit(OpCode.GetProp, nameIndex);
                var skip = chunk.EmitJump(jumpOp);
                CompileBase();
                chunk.Emit(OpCode.SetProp, nameIndex);
                var end = chunk.EmitJump(OpCode.Jump);
                chunk.PatchJump(skip);
                chunk.Emit(OpCode.GetProp, nameIndex);
                chunk.PatchJump(end);
            }
        }

        // obj[key] &&= / ||= / ??= b
        private void EmitIndexLogicalAssign()
        {
            var logOp = lexer.TokenType;
            lexer.Match(logOp);
            if (logOp == ScriptLex.LexTypes.NullCoalesceEqual)
            {
                chunk.Emit(OpCode.Dup2);
                chunk.Emit(OpCode.GetIndex);
                var isNull = chunk.EmitJump(OpCode.JumpIfNullOrUndefined);
                chunk.Emit(OpCode.Pop);
                chunk.Emit(OpCode.GetIndex);
                var skipAssign = chunk.EmitJump(OpCode.Jump);
                chunk.PatchJump(isNull);
                chunk.Emit(OpCode.Pop);
                CompileBase();
                chunk.Emit(OpCode.SetIndex);
                chunk.PatchJump(skipAssign);
            }
            else
            {
                var jumpOp = logOp == ScriptLex.LexTypes.AndAndEqual ? OpCode.JumpIfFalse : OpCode.JumpIfTrue;
                chunk.Emit(OpCode.Dup2);
                chunk.Emit(OpCode.GetIndex);
                var skip = chunk.EmitJump(jumpOp);
                CompileBase();
                chunk.Emit(OpCode.SetIndex);
                var end = chunk.EmitJump(OpCode.Jump);
                chunk.PatchJump(skip);
                chunk.Emit(OpCode.GetIndex);
                chunk.PatchJump(end);
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

        // Parse "(param, param = default, ...rest)" populating fnChunk.Parameters and
        // fnChunk.RestParamIndex.  Returns per-parameter default-expression source
        // strings (null entry = no default).
        private List<(string ParamName, string DefaultSrc)> ParseParameterList(Chunk fnChunk)
        {
            var paramDefaults = new List<(string ParamName, string DefaultSrc)>();
            lexer.Match((ScriptLex.LexTypes)'(');
            while (lexer.TokenType != (ScriptLex.LexTypes)')')
            {
                if (lexer.TokenType == ScriptLex.LexTypes.Ellipsis)
                {
                    lexer.Match(ScriptLex.LexTypes.Ellipsis);
                    var restName = lexer.TokenString;
                    lexer.Match(ScriptLex.LexTypes.Id);
                    fnChunk.RestParamIndex = fnChunk.Parameters.Count;
                    fnChunk.Parameters.Add(restName);
                    paramDefaults.Add((restName, null));
                    break; // rest must be last
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
                if (lexer.TokenType != (ScriptLex.LexTypes)')') lexer.Match((ScriptLex.LexTypes)',');
            }
            lexer.Match((ScriptLex.LexTypes)')');
            return paramDefaults;
        }

        // Compile a function's "(params) { body }" into a nested chunk and
        // register it; returns its index in the enclosing chunk's function table.
        private int CompileFunctionRest(string name, bool isGenerator = false, bool isAsync = false)
        {
            var fnChunk = new Chunk { Name = string.IsNullOrEmpty(name) ? "<anonymous>" : name, IsGenerator = isGenerator, IsAsync = isAsync, IsStrict = chunk.IsStrict };

            // capture the source span so the function can be rendered back to
            // text by JSON.stringify / GetParsableString and re-parsed by eval
            var sourceStart = lexer.TokenStart;

            var paramDefaults = ParseParameterList(fnChunk);

            // Push a parameter-barrier scope so outer `const` propagation cannot
            // substitute a value for a name that this function shadows with a parameter.
            if (_constScopes != null)
            {
                var barrier = new Dictionary<string, ConstantValue>();
                foreach (var p in fnChunk.Parameters)
                    barrier[p] = null; // null = blocked
                _constScopes.Push(barrier);
            }

            var saved = chunk;
            chunk = fnChunk;
            EnterFunctionBody(out var savedLoops, out var savedFinally, out var savedBlockDepth);

            // Emit default-value guards at the start of the function body.
            EmitDefaultParamGuards(paramDefaults);

            CompileBlock(checkDirective: true);
            chunk.Emit(OpCode.PushUndefined);
            chunk.Emit(OpCode.Return);
            ExitFunctionBody(savedLoops, savedFinally, savedBlockDepth);
            FinalizeArgumentsUsage(fnChunk, saved);
            chunk = saved;

            _constScopes?.Pop();

            fnChunk.Source = "function " + name + lexer.GetSubString(sourceStart);

            // Strict-mode parameter checks (run after CompileBlock so that "use strict"
            // inside the body is detected before the parameters are validated).
            if (fnChunk.IsStrict)
            {
                var seen = new System.Collections.Generic.HashSet<string>();
                foreach (var p in fnChunk.Parameters)
                {
                    if (p == "eval" || p == "arguments")
                        throw new ScriptException($"SyntaxError: '{p}' cannot be used as a parameter name in strict mode");
                    if (!seen.Add(p))
                        throw new ScriptException($"SyntaxError: Duplicate parameter name '{p}' not allowed in strict mode");
                }
            }

            return saved.AddFunction(fnChunk);
        }

        // Returns true for legacy-octal integer literals such as 0777.
        // These are the bare-zero-prefixed forms disallowed in strict mode.
        private static bool IsLegacyOctal(string s)
            => s.Length >= 2 && s[0] == '0' && char.IsDigit(s[1]);

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

                CompileInSubLexer(defaultSrc, CompileBase);

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
        // Pass isAsync: true for  `async (x) => expr` / `async x => expr`.
        private void CompileArrowFunction(bool isAsync = false)
        {
            var fnChunk = new Chunk { Name = "<arrow>", IsArrow = true, IsAsync = isAsync, IsStrict = chunk.IsStrict };
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
                paramDefaults = ParseParameterList(fnChunk);
            }

            lexer.Match(ScriptLex.LexTypes.Arrow);

            // Push a parameter-barrier scope (same as CompileFunctionRest).
            if (_constScopes != null)
            {
                var barrier = new Dictionary<string, ConstantValue>();
                foreach (var p in fnChunk.Parameters)
                    barrier[p] = null;
                _constScopes.Push(barrier);
            }

            var saved = chunk;
            chunk = fnChunk;
            EnterFunctionBody(out var savedLoops, out var savedFinally, out var savedBlockDepth);

            // Emit default-value guards at the start of the function body.
            EmitDefaultParamGuards(paramDefaults);

            if (lexer.TokenType == (ScriptLex.LexTypes)'{')
            {
                // Block body: (x) => { statements; }
                CompileBlock(checkDirective: true);
                chunk.Emit(OpCode.PushUndefined);
                chunk.Emit(OpCode.Return);
            }
            else
            {
                // Expression body: (x) => expr  — implicit return
                CompileBase();
                chunk.Emit(OpCode.Return);
            }

            ExitFunctionBody(savedLoops, savedFinally, savedBlockDepth);
            FinalizeArgumentsUsage(fnChunk, saved);
            chunk = saved;

            _constScopes?.Pop();

            fnChunk.Source = lexer.GetSubString(sourceStart);

            var idx = chunk.AddFunction(fnChunk);
            chunk.Emit(OpCode.MakeClosure, idx);
            chunk.MakesClosure = true;
        }

        // Isolate the new function body from the enclosing function's loop and
        // finally contexts so that break/continue/return inside the body don't
        // accidentally target the outer function's control structures.
        private void EnterFunctionBody(out Stack<LoopContext> savedLoops,
                                       out Stack<FinallyContext> savedFinally,
                                       out int savedBlockDepth)
        {
            savedLoops       = loops;
            savedFinally     = finallyStack;
            savedBlockDepth  = _blockDepth;
            loops            = new Stack<LoopContext>();
            finallyStack     = new Stack<FinallyContext>();
            // Reset block depth: function declarations at the top of a function
            // body are still hoisted; only declarations inside nested blocks are
            // block-scoped in strict mode.
            _blockDepth      = 0;
        }

        private void ExitFunctionBody(Stack<LoopContext> savedLoops,
                                      Stack<FinallyContext> savedFinally,
                                      int savedBlockDepth)
        {
            loops        = savedLoops;
            finallyStack = savedFinally;
            _blockDepth  = savedBlockDepth;
        }

        // Parse the raw template string into alternating (cooked, raw, isExpr) segments.
        // Returns list of (isExpr: bool, cooked: string, raw: string) tuples.
        // Literal segments have isExpr=false; expression segments have isExpr=true and
        // contain the expression source in 'cooked' (raw is unused for expressions).
        private static System.Collections.Generic.List<(bool IsExpr, string Cooked, string Raw)>
            ParseTemplateParts(string raw)
        {
            var parts = new System.Collections.Generic.List<(bool, string, string)>();
            var cooked = new System.Text.StringBuilder();
            var rawSeg = new System.Text.StringBuilder();
            var i = 0;

            while (i < raw.Length)
            {
                if (raw[i] == '\\' && i + 1 < raw.Length)
                {
                    rawSeg.Append('\\');
                    rawSeg.Append(raw[i + 1]);
                    switch (raw[i + 1])
                    {
                        case 'n':  cooked.Append('\n'); break;
                        case 'r':  cooked.Append('\r'); break;
                        case 't':  cooked.Append('\t'); break;
                        case '\\': cooked.Append('\\'); break;
                        case '`':  cooked.Append('`');  break;
                        case '$':  cooked.Append('$');  break;
                        default:   cooked.Append('\\'); cooked.Append(raw[i + 1]); break;
                    }
                    i += 2;
                }
                else if (raw[i] == '$' && i + 1 < raw.Length && raw[i + 1] == '{')
                {
                    parts.Add((false, cooked.ToString(), rawSeg.ToString()));
                    cooked.Clear();
                    rawSeg.Clear();
                    i += 2;
                    int depth = 1, exprStart = i;
                    while (i < raw.Length && depth > 0)
                    {
                        if (raw[i] == '{') depth++;
                        else if (raw[i] == '}') { if (--depth == 0) break; }
                        i++;
                    }
                    parts.Add((true, raw.Substring(exprStart, i - exprStart), string.Empty));
                    if (i < raw.Length) i++;
                }
                else
                {
                    rawSeg.Append(raw[i]);
                    cooked.Append(raw[i]);
                    i++;
                }
            }
            parts.Add((false, cooked.ToString(), rawSeg.ToString()));
            return parts;
        }

        // Compile a tagged template call: tag`...${expr}...`
        // The tag function is already on the stack; this method emits the strings
        // array (with .raw), each expression value, and a TaggedTemplate opcode.
        private void CompileTaggedTemplateLiteralCall()
        {
            var raw = lexer.TokenString;
            lexer.Match(ScriptLex.LexTypes.TemplateLiteral);

            var parts = ParseTemplateParts(raw);

            // Separate literal and expression parts
            var literals = new System.Collections.Generic.List<(string Cooked, string Raw)>();
            var exprTexts = new System.Collections.Generic.List<string>();
            foreach (var (isExpr, cooked, rawStr) in parts)
            {
                if (isExpr) exprTexts.Add(cooked);
                else literals.Add((cooked, rawStr));
            }

            // Emit cooked strings
            foreach (var (cookedStr, _) in literals)
                chunk.Emit(OpCode.Constant, chunk.AddConstant(ConstantValue.String(cookedStr)));

            // Emit raw strings
            foreach (var (_, rawStr) in literals)
                chunk.Emit(OpCode.Constant, chunk.AddConstant(ConstantValue.String(rawStr)));

            // Compile each expression
            foreach (var exprText in exprTexts)
                CompileInSubLexer(exprText, CompileBase);

            chunk.Emit(OpCode.TaggedTemplate, literals.Count, exprTexts.Count);
        }

        // Compile a template literal `...${expr}...` into a sequence of string
        // constants and expressions joined by the + (string-concatenation) operator.
        private void CompileTemplateLiteral()
        {
            var rawStr = lexer.TokenString;
            lexer.Match(ScriptLex.LexTypes.TemplateLiteral);

            var parts = ParseTemplateParts(rawStr);

            // Emit code: push each part; concatenate all with Binary '+'.
            // Skip subsequent empty literal strings between/after expressions.
            var emitted = 0;
            foreach (var (isExpr, cooked, _) in parts)
            {
                if (isExpr)
                {
                    CompileInSubLexer(cooked, CompileBase);
                }
                else if (emitted == 0 || cooked.Length > 0)
                {
                    // Always emit the first part (even if empty) to guarantee a string
                    // is on the stack; skip subsequent empty literal segments.
                    chunk.Emit(OpCode.Constant,
                        chunk.AddConstant(ConstantValue.String(cooked)));
                }
                else
                {
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

            // Once a spread has been merged, later static keys may collide with a
            // merged-in property and must overwrite it (the literal key wins). Plain
            // InitProp appends without a lookup, so only switch after the first spread.
            var initPropOp = OpCode.InitProp;

            while (lexer.TokenType != (ScriptLex.LexTypes)'}')
            {
                // Spread property: { ...expr }
                if (lexer.TokenType == ScriptLex.LexTypes.Ellipsis)
                {
                    lexer.Match(ScriptLex.LexTypes.Ellipsis);
                    CompileBase();              // source object on stack, target below
                    chunk.Emit(OpCode.MergeObject);
                    initPropOp = OpCode.InitPropOverwrite;
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

                    // get/set accessors — contextual: "get"/"set" is only an accessor keyword
                    // when the next token is another identifier (the property name).
                    if ((name == "get" || name == "set") && lexer.TokenType == ScriptLex.LexTypes.Id)
                    {
                        var isGetter = name == "get";
                        var propName = lexer.TokenString;
                        lexer.Match(ScriptLex.LexTypes.Id);
                        var fnIdx = CompileFunctionRest(propName);
                        chunk.Emit(OpCode.MakeClosure, fnIdx);
                        chunk.MakesClosure = true;
                        chunk.Emit(isGetter ? OpCode.DefineGetter : OpCode.DefineSetter, chunk.AddName(propName));
                    }
                    // Method shorthand: `{ foo() { } }`
                    else if (lexer.TokenType == (ScriptLex.LexTypes)'(')
                    {
                        var fnIdx = CompileFunctionRest(name);
                        chunk.Emit(OpCode.MakeClosure, fnIdx);
                        chunk.MakesClosure = true;
                        chunk.Emit(initPropOp, chunk.AddName(name));
                    }
                    // Shorthand property: `{ x, y }` — no colon, use var with same name as key
                    else if (lexer.TokenType == (ScriptLex.LexTypes)',' || lexer.TokenType == (ScriptLex.LexTypes)'}')
                    {
                        chunk.Emit(OpCode.GetVar, chunk.AddName(name));
                        chunk.Emit(initPropOp, chunk.AddName(name));
                    }
                    else
                    {
                        lexer.Match((ScriptLex.LexTypes)':');
                        CompileBase();
                        chunk.Emit(initPropOp, chunk.AddName(name));
                    }
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
                    CompileBase();
                    chunk.Emit(OpCode.AppendElem); // O(1) cached-length append
                }

                if (lexer.TokenType != (ScriptLex.LexTypes)']')
                    lexer.Match((ScriptLex.LexTypes)',');
            }

            lexer.Match((ScriptLex.LexTypes)']');
        }

        private static readonly Dictionary<ScriptLex.LexTypes, (ScriptLex.LexTypes BaseOp, bool IsShift)> CompoundOps = new()
        {
            { ScriptLex.LexTypes.PlusEqual,           ((ScriptLex.LexTypes)'+',          false) },
            { ScriptLex.LexTypes.MinusEqual,          ((ScriptLex.LexTypes)'-',          false) },
            { ScriptLex.LexTypes.TimesEqual,          ((ScriptLex.LexTypes)'*',          false) },
            { ScriptLex.LexTypes.SlashEqual,          ((ScriptLex.LexTypes)'/',          false) },
            { ScriptLex.LexTypes.PercentEqual,        ((ScriptLex.LexTypes)'%',          false) },
            { ScriptLex.LexTypes.PowerEqual,          (ScriptLex.LexTypes.Power,         false) },
            { ScriptLex.LexTypes.AndEqual,            ((ScriptLex.LexTypes)'&',          false) },
            { ScriptLex.LexTypes.OrEqual,             ((ScriptLex.LexTypes)'|',          false) },
            { ScriptLex.LexTypes.XorEqual,            ((ScriptLex.LexTypes)'^',          false) },
            { ScriptLex.LexTypes.LShiftEqual,         (ScriptLex.LexTypes.LShift,        true)  },
            { ScriptLex.LexTypes.RShiftEqual,         (ScriptLex.LexTypes.RShift,        true)  },
            { ScriptLex.LexTypes.RShiftUnsignedEqual, (ScriptLex.LexTypes.RShiftUnsigned, true) },
        };

        private static bool TryGetCompoundOp(ScriptLex.LexTypes token, out ScriptLex.LexTypes baseOp, out bool isShift)
        {
            if (CompoundOps.TryGetValue(token, out var entry))
            {
                (baseOp, isShift) = entry;
                return true;
            }
            baseOp = default;
            isShift = false;
            return false;
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
