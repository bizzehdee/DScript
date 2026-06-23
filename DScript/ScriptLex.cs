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

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DScript
{
    public sealed class ScriptLex : IDisposable
    {
        #region IDisposable
        private bool disposed;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposed) return;
            
            if (disposing)
            {
                if (dataOwned)
                {
                    data = string.Empty;
                }
            }

            // Indicate that the instance has been disposed.
            disposed = true;
        }
        #endregion

        // One allocation per app domain; O(1) Contains vs O(12) foreach scan.
        private static readonly HashSet<LexTypes> NotAllowedBeforeRegex =
        [
            LexTypes.Id, LexTypes.Int, LexTypes.Float, LexTypes.Str, LexTypes.BigIntLiteral,
            LexTypes.RTrue, LexTypes.RFalse, LexTypes.RNull, (LexTypes)']',
            (LexTypes)')', (LexTypes)'.', LexTypes.PlusPlus, LexTypes.MinusMinus,
            LexTypes.Eof
        ];

        private string data;
        private readonly bool dataOwned;
        private readonly int dataStart;
        private readonly int dataEnd;
        private int dataPos;
        private readonly StringBuilder tokenBuilder = new(64);

        public char CurrentChar { get; private set; }
        public char NextChar { get; private set; }
        public LexTypes TokenType { get; private set; }
        public LexTypes PreviousTokenType { get; private set; }
        public int TokenStart { get; private set; }
        public int TokenEnd { get; private set; }
        public int TokenLastEnd { get; private set; }
        public int LineNumber { get; private set; }
        public int ColumnNumber { get; private set; }
        public string TokenString { get; private set; }

        public enum LexTypes
        {
            Eof = 0,
            Id = 256,
            Int,
            Float,
            Str,
            Equal,
            TypeEqual,
            NEqual,
            NTypeEqual,
            LEqual,
            LShift,
            LShiftEqual,
            GEqual,
            RShift,
            RShiftUnsigned,
            RShiftUnsignedEqual,
            RShiftEqual,
            PlusEqual,
            MinusEqual,
            SlashEqual,
            PercentEqual,
            TimesEqual,
            PlusPlus,
            MinusMinus,
            AndEqual,
            AndAnd,
            OrEqual,
            OrOr,
            XorEqual,
            RegExp,
            RListStart,
            RIf = RListStart,
            RElse,
            RDo,
            RWhile,
            RFor,
            RBreak,
            RContinue,
            RFunction,
            RReturn,
            RVar,
            RTrue,
            RFalse,
            RNull,
            RUndefined,
            RNew,
            RTypeOf,
            RTry,
            RCatch,
            RFinally,
            RThrow,
            RConst,
            RListEnd,
            RSwitch,
            RCase,
            RDefault,
            RInstanceOf,
            RIn,
            RDelete,
            Arrow,          // =>
            TemplateLiteral, // `...`
            RClass,
            RExtends,
            RSuper,
            RStatic,
            RLet,
            QuestionDot,    // ?.
            NullCoalesce,   // ??
            Ellipsis,       // ...
            ROf,            // of (contextual keyword in for...of)
            RYield,         // yield keyword
            RAsync,         // async keyword
            RAwait,         // await keyword
            RExport,        // export keyword
            RImport,        // import keyword
            RFrom,          // from contextual keyword
            AndAndEqual,    // &&=
            OrOrEqual,      // ||=
            NullCoalesceEqual, // ??=
            PrivateName,    // #identifier (private class field/method name)
            BigIntLiteral,  // BigInt literal: 42n, 0xFFn, 0b101n, 0o77n
        }

        public ScriptLex(string input)
        {
            data = input;
            dataOwned = true;
            dataStart = 0;
            dataEnd = data.Length;

            Reset();
        }

        public ScriptLex(ScriptLex owner, int start, int end)
        {
            data = owner.data;
            dataOwned = false;
            dataStart = start;
            dataEnd = end;

            Reset();
        }
        
        public ScriptLex(string input, int startPos, int lineNumber, int columnNumber)
        {
            data = input;
            dataOwned = true;
            dataStart = 0;
            dataEnd = data.Length;
            
            dataPos = startPos;
            TokenStart = startPos;
            TokenEnd = startPos;
            TokenLastEnd = startPos;
            TokenType = 0;
            TokenString = "";
            LineNumber = lineNumber;
            ColumnNumber = columnNumber;

            GetNextChar();
            GetNextChar();
            GetNextToken();
        }

        public void Reset()
        {
            dataPos = dataStart;
            TokenStart = 0;
            TokenEnd = 0;
            TokenLastEnd = 0;
            TokenType = 0;
            TokenString = "";

            LineNumber = 1;
            ColumnNumber = 1;

            GetNextChar();
            GetNextChar();
            GetNextToken();
        }

        public void GetNextChar()
        {
            CurrentChar = NextChar;

            if (dataPos < dataEnd)
            {
                NextChar = data[dataPos];
            }
            else
            {
                NextChar = (char)0;
            }

            dataPos++;

            ColumnNumber++;

            if (CurrentChar == '\n')
            {
                LineNumber++;
                ColumnNumber = 1;
            }
        }

        public void GetNextToken()
        {
            PreviousTokenType = TokenType;
            TokenType = LexTypes.Eof;
            TokenString = string.Empty;

            // Skip whitespace and comments iteratively (avoids a call-stack entry
            // per comment block and a function-call overhead per whitespace run).
            while (true)
            {
                while (CurrentChar != (char)0 && CurrentChar.IsWhitespace())
                    GetNextChar();

                if (CurrentChar == '#' && NextChar == '!' && dataPos == dataStart + 2)
                {
                    while (CurrentChar != 0 && CurrentChar != '\n') GetNextChar();
                    continue;
                }

                if (CurrentChar == '/' && NextChar == '/')
                {
                    while (CurrentChar != 0 && CurrentChar != '\n') GetNextChar();
                    GetNextChar();
                    continue;
                }

                if (CurrentChar == '/' && NextChar == '*')
                {
                    while (CurrentChar != 0 && (CurrentChar != '*' || NextChar != '/')) GetNextChar();
                    GetNextChar();
                    GetNextChar();
                    continue;
                }

                break;
            }

            TokenStart = dataPos - 2;

            if (CurrentChar.IsAlpha()) //IDs
            {
                tokenBuilder.Clear();
                while (CurrentChar.IsAlpha() || CurrentChar.IsNumeric())
                {
                    tokenBuilder.Append(CurrentChar);
                    GetNextChar();
                }

                TokenString = tokenBuilder.ToString();
                TokenType = LexTypes.Id;
                switch (TokenString)
                {
                    case "if": TokenType = LexTypes.RIf; break;
                    case "else": TokenType = LexTypes.RElse; break;
                    case "do": TokenType = LexTypes.RDo; break;
                    case "while": TokenType = LexTypes.RWhile; break;
                    case "for": TokenType = LexTypes.RFor; break;
                    case "break": TokenType = LexTypes.RBreak; break;
                    case "continue": TokenType = LexTypes.RContinue; break;
                    case "function": TokenType = LexTypes.RFunction; break;
                    case "return": TokenType = LexTypes.RReturn; break;
                    case "var": TokenType = LexTypes.RVar; break;
                    case "true": TokenType = LexTypes.RTrue; break;
                    case "false": TokenType = LexTypes.RFalse; break;
                    case "null": TokenType = LexTypes.RNull; break;
                    case "undefined": TokenType = LexTypes.RUndefined; break;
                    case "new": TokenType = LexTypes.RNew; break;
                    case "typeof": TokenType = LexTypes.RTypeOf; break;
                    case "try": TokenType = LexTypes.RTry; break;
                    case "catch": TokenType = LexTypes.RCatch; break;
                    case "finally": TokenType = LexTypes.RFinally; break;
                    case "throw": TokenType = LexTypes.RThrow; break;
                    case "const": TokenType = LexTypes.RConst; break;
                    case "switch": TokenType = LexTypes.RSwitch; break;
                    case "case": TokenType = LexTypes.RCase; break;
                    case "default": TokenType = LexTypes.RDefault; break;
                    case "instanceof": TokenType = LexTypes.RInstanceOf; break;
                    case "in": TokenType = LexTypes.RIn; break;
                    case "delete": TokenType = LexTypes.RDelete; break;
                    case "class": TokenType = LexTypes.RClass; break;
                    case "extends": TokenType = LexTypes.RExtends; break;
                    case "super": TokenType = LexTypes.RSuper; break;
                    case "static": TokenType = LexTypes.RStatic; break;
                    case "let": TokenType = LexTypes.RLet; break;
                    case "of": TokenType = LexTypes.ROf; break;
                    case "yield": TokenType = LexTypes.RYield; break;
                    case "async": TokenType = LexTypes.RAsync; break;
                    case "await": TokenType = LexTypes.RAwait; break;
                    case "export": TokenType = LexTypes.RExport; break;
                    case "import": TokenType = LexTypes.RImport; break;
                    case "from": TokenType = LexTypes.RFrom; break;
                }
            }
            else if (CurrentChar == '#' && NextChar.IsAlpha()) // Private class field/method name: #identifier
            {
                tokenBuilder.Clear();
                tokenBuilder.Append('#');
                GetNextChar(); // consume '#'
                while (CurrentChar.IsAlpha() || CurrentChar.IsNumeric())
                {
                    tokenBuilder.Append(CurrentChar);
                    GetNextChar();
                }
                TokenString = tokenBuilder.ToString();
                TokenType = LexTypes.PrivateName;
            }
            else if (CurrentChar.IsNumeric()) //Numbers
            {
                tokenBuilder.Clear();
                var isHex = false;
                var isBinary = false;
                var isOctal = false;

                if (CurrentChar == '0')
                {
                    tokenBuilder.Append(CurrentChar);
                    GetNextChar();
                }

                if (CurrentChar == 'x' || CurrentChar == 'X')
                {
                    isHex = true;
                    tokenBuilder.Append(CurrentChar);
                    GetNextChar();
                    if (CurrentChar == '_') throw new ScriptException("Numeric separator cannot appear after 0x prefix");
                }
                else if (CurrentChar == 'b' || CurrentChar == 'B')
                {
                    isBinary = true;
                    tokenBuilder.Append(CurrentChar);
                    GetNextChar();
                    if (CurrentChar == '_') throw new ScriptException("Numeric separator cannot appear after 0b prefix");
                }
                else if (CurrentChar == 'o' || CurrentChar == 'O')
                {
                    isOctal = true;
                    tokenBuilder.Append(CurrentChar);
                    GetNextChar();
                    if (CurrentChar == '_') throw new ScriptException("Numeric separator cannot appear after 0o prefix");
                }

                TokenType = LexTypes.Int;

                // Read integer digits, skipping numeric separators
                var prevWasSep = false;
                while (true)
                {
                    if (CurrentChar == '_')
                    {
                        if (prevWasSep) throw new ScriptException("Consecutive numeric separators are not allowed");
                        prevWasSep = true;
                        GetNextChar();
                        continue;
                    }
                    bool valid = isHex    ? CurrentChar.IsHexadecimal() :
                                 isBinary ? (CurrentChar == '0' || CurrentChar == '1') :
                                 isOctal  ? (CurrentChar >= '0' && CurrentChar <= '7') :
                                            CurrentChar.IsNumeric();
                    if (!valid) break;
                    prevWasSep = false;
                    tokenBuilder.Append(CurrentChar);
                    GetNextChar();
                }
                if (prevWasSep) throw new ScriptException("Numeric separator cannot appear at the end of a numeric literal");

                if (!isHex && !isBinary && !isOctal && CurrentChar == '.')
                {
                    TokenType = LexTypes.Float;
                    tokenBuilder.Append('.');
                    GetNextChar();
                    if (CurrentChar == '_') throw new ScriptException("Numeric separator cannot appear after decimal point");
                    prevWasSep = false;
                    while (true)
                    {
                        if (CurrentChar == '_')
                        {
                            if (prevWasSep) throw new ScriptException("Consecutive numeric separators are not allowed");
                            prevWasSep = true;
                            GetNextChar();
                            continue;
                        }
                        if (!CurrentChar.IsNumeric()) break;
                        prevWasSep = false;
                        tokenBuilder.Append(CurrentChar);
                        GetNextChar();
                    }
                    if (prevWasSep) throw new ScriptException("Numeric separator cannot appear at the end of a numeric literal");
                }

                if (!isHex && !isBinary && !isOctal && (CurrentChar == 'e' || CurrentChar == 'E'))
                {
                    TokenType = LexTypes.Float;
                    tokenBuilder.Append(CurrentChar);
                    GetNextChar();
                    if (CurrentChar == '_') throw new ScriptException("Numeric separator cannot appear after exponent marker");
                    if (CurrentChar == '-' || CurrentChar == '+')
                    {
                        tokenBuilder.Append(CurrentChar);
                        GetNextChar();
                    }
                    if (CurrentChar == '_') throw new ScriptException("Numeric separator cannot appear after exponent marker");
                    prevWasSep = false;
                    while (true)
                    {
                        if (CurrentChar == '_')
                        {
                            if (prevWasSep) throw new ScriptException("Consecutive numeric separators are not allowed");
                            prevWasSep = true;
                            GetNextChar();
                            continue;
                        }
                        if (!CurrentChar.IsNumeric()) break;
                        prevWasSep = false;
                        tokenBuilder.Append(CurrentChar);
                        GetNextChar();
                    }
                    if (prevWasSep) throw new ScriptException("Numeric separator cannot appear at the end of a numeric literal");
                }

                // BigInt suffix: 123n, 0xFFn, 0b101n, 0o77n
                // Floats (with '.' or 'e') cannot have the n suffix.
                if (CurrentChar == 'n' && TokenType == LexTypes.Int)
                {
                    GetNextChar(); // consume 'n'
                    TokenType = LexTypes.BigIntLiteral;
                }

                TokenString = tokenBuilder.ToString();
            }
            else if (CurrentChar == '`') // Template literal — store raw content, compiler handles interpolation
            {
                tokenBuilder.Clear();
                GetNextChar(); // consume opening `
                while (CurrentChar != '\0')
                {
                    if (CurrentChar == '\\')
                    {
                        tokenBuilder.Append('\\');
                        GetNextChar();
                        if (CurrentChar != '\0')
                        {
                            tokenBuilder.Append(CurrentChar);
                            GetNextChar();
                        }
                    }
                    else if (CurrentChar == '`')
                    {
                        GetNextChar(); // consume closing `
                        break;
                    }
                    else
                    {
                        tokenBuilder.Append(CurrentChar);
                        GetNextChar();
                    }
                }
                TokenString = tokenBuilder.ToString();
                TokenType = LexTypes.TemplateLiteral;
            }
            else if (CurrentChar == '\'' || CurrentChar == '\"') //Strings again
            {
                tokenBuilder.Clear();
                var endChar = CurrentChar;
                GetNextChar();

                while (CurrentChar != (char)0 && CurrentChar != endChar)
                {
                    if (CurrentChar == '\\')
                    {
                        GetNextChar();

                        switch (CurrentChar)
                        {
                            case '\n': break;
                            case 'n':
                                tokenBuilder.Append('\n');
                                break;
                            case 'r':
                                tokenBuilder.Append('\r');
                                break;
                            case 'a':
                                tokenBuilder.Append('\a');
                                break;
                            case 'b':
                                tokenBuilder.Append('\b');
                                break;
                            case 'f':
                                tokenBuilder.Append('\f');
                                break;
                            case 't':
                                tokenBuilder.Append('\t');
                                break;
                            case 'v':
                                tokenBuilder.Append('\v');
                                break;
                            case 'x':
                                {
                                    GetNextChar();
                                    var hi = HexDigitValue(CurrentChar);
                                    GetNextChar();
                                    var lo = HexDigitValue(CurrentChar);
                                    tokenBuilder.Append((char)((hi << 4) | lo));
                                }
                                break;
                            default:
                                if (CurrentChar is >= '0' and <= '7')
                                {
                                    var v = (CurrentChar - '0') * 64;
                                    GetNextChar();
                                    v += (CurrentChar - '0') * 8;
                                    GetNextChar();
                                    v += (CurrentChar - '0');
                                    tokenBuilder.Append((char)v);
                                }
                                else
                                {
                                    tokenBuilder.Append(CurrentChar);
                                }
                                break;
                        }
                    }
                    else
                    {
                        tokenBuilder.Append(CurrentChar);
                    }

                    GetNextChar();
                }

                GetNextChar();

                TokenString = tokenBuilder.ToString();
                TokenType = LexTypes.Str;
            }
            else //Single character
            {
                TokenType = (LexTypes)CurrentChar;

                if (CurrentChar != (char)0)
                {
                    GetNextChar();
                }

                if (TokenType == (LexTypes)'=' && CurrentChar == '>') // =>
                {
                    TokenType = LexTypes.Arrow;
                    GetNextChar();
                }
                else if (TokenType == (LexTypes)'=' && CurrentChar == '=') // ==
                {
                    TokenType = LexTypes.Equal;
                    GetNextChar();

                    if (CurrentChar == '=') //===
                    {
                        TokenType = LexTypes.TypeEqual;
                        GetNextChar();
                    }
                }
                else if (TokenType == (LexTypes)'!' && CurrentChar == '=') // !=
                {
                    TokenType = LexTypes.NEqual;
                    GetNextChar();
                    if (CurrentChar == '=') // !==
                    {
                        TokenType = LexTypes.NTypeEqual;
                        GetNextChar();
                    }
                }
                else if (TokenType == (LexTypes)'<' && CurrentChar == '=') // <=
                {
                    TokenType = LexTypes.LEqual;
                    GetNextChar();
                }
                else if (TokenType == (LexTypes)'<' && CurrentChar == '<') // <<
                {
                    TokenType = LexTypes.LShift;
                    GetNextChar();
                    if (CurrentChar == '=') //<<=
                    {
                        TokenType = LexTypes.LShiftEqual;
                        GetNextChar();
                    }
                }
                else if (TokenType == (LexTypes)'>' && CurrentChar == '=') // >=
                {
                    TokenType = LexTypes.GEqual;
                    GetNextChar();
                }
                else if (TokenType == (LexTypes)'>' && CurrentChar == '>') // >>
                {
                    TokenType = LexTypes.RShift;
                    GetNextChar();

                    if (CurrentChar == '=') // >>=
                    {
                        TokenType = LexTypes.RShiftEqual;
                        GetNextChar();
                    }
                    else if (CurrentChar == '>') // >>>
                    {
                        TokenType = LexTypes.RShiftUnsigned;
                        GetNextChar();

                        if (CurrentChar == '=') // >>>=
                        {
                            TokenType = LexTypes.RShiftUnsignedEqual;
                            GetNextChar();
                        }
                    }
                }
                else if (TokenType == (LexTypes)'+' && CurrentChar == '=') // +=
                {
                    TokenType = LexTypes.PlusEqual;
                    GetNextChar();
                }
                else if (TokenType == (LexTypes)'-' && CurrentChar == '=') // -=
                {
                    TokenType = LexTypes.MinusEqual;
                    GetNextChar();
                }
                else if (TokenType == (LexTypes)'+' && CurrentChar == '+') // ++
                {
                    TokenType = LexTypes.PlusPlus;
                    GetNextChar();
                }
                else if (TokenType == (LexTypes)'-' && CurrentChar == '-') // --
                {
                    TokenType = LexTypes.MinusMinus;
                    GetNextChar();
                }
                else if (TokenType == (LexTypes)'&' && CurrentChar == '=') // &=
                {
                    TokenType = LexTypes.AndEqual;
                    GetNextChar();
                }
                else if (TokenType == (LexTypes)'&' && CurrentChar == '&') // && or &&=
                {
                    TokenType = LexTypes.AndAnd;
                    GetNextChar();
                    if (CurrentChar == '=') { TokenType = LexTypes.AndAndEqual; GetNextChar(); }
                }
                else if (TokenType == (LexTypes)'|' && CurrentChar == '=') // |=
                {
                    TokenType = LexTypes.OrEqual;
                    GetNextChar();
                }
                else if (TokenType == (LexTypes)'|' && CurrentChar == '|') // || or ||=
                {
                    TokenType = LexTypes.OrOr;
                    GetNextChar();
                    if (CurrentChar == '=') { TokenType = LexTypes.OrOrEqual; GetNextChar(); }
                }
                else if (TokenType == (LexTypes)'^' && CurrentChar == '=') // ^=
                {
                    TokenType = LexTypes.XorEqual;
                    GetNextChar();
                }
                else if (TokenType == (LexTypes)'?' && CurrentChar == '?') // ?? or ??=
                {
                    TokenType = LexTypes.NullCoalesce;
                    GetNextChar();
                    if (CurrentChar == '=') { TokenType = LexTypes.NullCoalesceEqual; GetNextChar(); }
                }
                else if (TokenType == (LexTypes)'?' && CurrentChar == '.') // ?.
                {
                    TokenType = LexTypes.QuestionDot;
                    GetNextChar();
                }
                else if (TokenType == (LexTypes)'.' && CurrentChar == '.' && NextChar == '.') // ...
                {
                    TokenType = LexTypes.Ellipsis;
                    GetNextChar(); // consume second '.'
                    GetNextChar(); // consume third '.'
                }
                else if (TokenType == (LexTypes)'/')
                {
                    //omit regex for now
                    
                    TokenType = NotAllowedBeforeRegex.Contains(PreviousTokenType)
                        ? (LexTypes)'/'
                        : LexTypes.RegExp;

                    if (TokenType == LexTypes.RegExp)
                    {
                        tokenBuilder.Clear();
                        tokenBuilder.Append('/');

                        while(CurrentChar != 0 && CurrentChar != '/' && CurrentChar != '\n')
                        {
                            if(CurrentChar == '\\' && NextChar == '/')
                            {
                                tokenBuilder.Append(CurrentChar);
                                GetNextChar();
                            }

                            tokenBuilder.Append(CurrentChar);
                            GetNextChar();
                        }

                        if(CurrentChar == '/')
                        {
                            var regexStr = tokenBuilder.ToString().Substring(1);
                            try
                            {
                                _ = new Regex(regexStr, RegexOptions.ECMAScript);
                            }
                            catch(Exception ex)
                            {
                                throw new ScriptException("Invalid RegEx", ex);
                            }
                            
                            do
                            {
                                tokenBuilder.Append(CurrentChar);
                                GetNextChar();
                            } while (CurrentChar == 'g' || CurrentChar == 'i' || CurrentChar == 'm' || CurrentChar == 'y');
                            
                            TokenString = tokenBuilder.ToString();
                        }
                    }
                    else if (CurrentChar == '=') // /=
                    {
                        TokenType = LexTypes.SlashEqual;
                        GetNextChar();
                    }
                }
                else if (TokenType == (LexTypes)'%' && CurrentChar == '=') // %=
                {
                    TokenType = LexTypes.PercentEqual;
                    GetNextChar();
                }
                else if (TokenType == (LexTypes)'*' && CurrentChar == '=') // *=
                {
                    TokenType = LexTypes.TimesEqual;
                    GetNextChar();
                }
            }

            /* Something broke... */
            TokenLastEnd = TokenEnd;
            TokenEnd = dataPos - 3;
        }

        /// <summary>
        /// Create a read-only lexer spanning from <paramref name="startPos"/> to the
        /// end of the current data, positioned at the token that starts there. Used
        /// for limited look-ahead (e.g. distinguishing for...in from a C-style for)
        /// without disturbing this lexer's position.
        /// </summary>
        public ScriptLex CloneToEnd(int startPos)
        {
            return new ScriptLex(this, startPos, dataEnd);
        }

        public ScriptLex GetSubLex(int lastPosition)
        {
            var lastCharIdx = TokenLastEnd + 1;

            if (lastCharIdx < dataEnd)
            {
                return new ScriptLex(this, lastPosition, lastCharIdx);
            }

            return new ScriptLex(this, lastPosition, dataEnd);
        }

        public string GetSubString(int pos)
        {
            var lastCharIndex = TokenLastEnd + 1;

            if (lastCharIndex < dataEnd)
            {
                return data.Substring(pos, lastCharIndex - pos);
            }

            return data.Substring(pos);
        }
        
        public string GetCode()
        {
            if (dataStart == 0 && dataEnd == data.Length)
            {
                return data;
            }
            
            return data.Substring(dataStart, dataEnd - dataStart);
        }

        public void Match(LexTypes type)
        {
            if (TokenType != type)
            {
                var expectedName = Enum.GetName<LexTypes>(type);
                if (string.IsNullOrEmpty(expectedName))
                {
                    expectedName = $"{(char)type}";
                }

                var foundName = Enum.GetName<LexTypes>(TokenType);
                if (string.IsNullOrEmpty(foundName))
                {
                    foundName = $"{(char)TokenType}";
                }

                throw new ScriptException($"Unexpected token type. Expected {expectedName}, found {foundName}. Line: {LineNumber}, Col: {ColumnNumber}");
            }

            GetNextToken();
        }

        /// <summary>
        /// Accept an identifier OR any reserved keyword used as a property name
        /// (e.g. <c>obj.catch</c>, <c>obj.then</c>, <c>obj.delete</c>).
        /// Advances the lexer and returns true. Throws on non-identifier tokens.
        /// </summary>
        public void MatchPropertyName()
        {
            // Accept Id, private names, or any reserved word (>= RListStart) that starts
            // an alpha char (i.e. it was lexed as a keyword but is used here as a property name).
            if (TokenType == LexTypes.Id || TokenType == LexTypes.PrivateName
                || (int)TokenType >= (int)LexTypes.RListStart)
            {
                GetNextToken();
                return;
            }
            throw new ScriptException($"Expected property name, found {Enum.GetName<LexTypes>(TokenType) ?? ((char)TokenType).ToString()}. Line: {LineNumber}, Col: {ColumnNumber}");
        }

        public static string LexTypesToString(LexTypes lexTypes)
        {
            if(lexTypes is < (LexTypes)256 and > 0)
            {
                return $"{(char)lexTypes}";
            }

            return lexTypes.ToString();
        }

        // Convert a single ASCII hex digit to its integer value without allocating
        // a string. Behaviour is undefined for non-hex characters.
        private static int HexDigitValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return c - 'A' + 10; // 'A'..'F'
        }
    }
}
