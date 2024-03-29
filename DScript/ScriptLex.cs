﻿/*
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
using System.Text.RegularExpressions;

namespace DScript
{
    public class ScriptLex : IDisposable
    {
        #region IDisposable
        private bool _disposed;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (dataOwned)
                    {
                        data = string.Empty;
                    }
                }

                // Indicate that the instance has been disposed.
                _disposed = true;
            }
        }
        #endregion

        private readonly LexTypes[] notAllowedBeforeRegex = new LexTypes[]
        {
            LexTypes.Id, LexTypes.Int, LexTypes.Float, LexTypes.Str, 
            LexTypes.RTrue, LexTypes.RFalse, LexTypes.RNull, (LexTypes)']', 
            (LexTypes)')', (LexTypes)'.', LexTypes.PlusPlus, LexTypes.MinusMinus,
            LexTypes.Eof
        };

        private string data;
        private readonly bool dataOwned;
        private readonly int dataStart;
        private readonly int dataEnd;
        private int dataPos;

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
            RDefault
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

            while (CurrentChar != (char)0 && CurrentChar.IsWhitespace())
            {
                GetNextChar();
            }

            //single line comment
            if (CurrentChar == '/' && NextChar == '/')
            {
                while (CurrentChar != 0 && CurrentChar != '\n') GetNextChar();
                GetNextChar();
                GetNextToken();
                return;
            }

            //multi line comment
            if (CurrentChar == '/' && NextChar == '*')
            {
                while (CurrentChar != 0 && (CurrentChar != '*' || NextChar != '/')) GetNextChar();
                GetNextChar();
                GetNextChar();
                GetNextToken();
                return;
            }

            TokenStart = dataPos - 2;

            if (CurrentChar.IsAlpha()) //IDs
            {
                while (CurrentChar.IsAlpha() || CurrentChar.IsNumeric())
                {
                    TokenString += CurrentChar;
                    GetNextChar();
                }

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
                }
            }
            else if (CurrentChar.IsNumeric()) //Numbers
            {
                var isHex = false;
                if (CurrentChar == '0')
                {
                    TokenString += CurrentChar;
                    GetNextChar();
                }

                if (CurrentChar == 'x')
                {
                    isHex = true;
                    TokenString += CurrentChar;
                    GetNextChar();
                }

                TokenType = LexTypes.Int;

                while (CurrentChar.IsNumeric() || (isHex && CurrentChar.IsHexadecimal()))
                {
                    TokenString += CurrentChar;
                    GetNextChar();
                }

                if (!isHex && CurrentChar == '.')
                {
                    TokenType = LexTypes.Float;
                    TokenString += '.';
                    GetNextChar();
                    while (CurrentChar.IsNumeric())
                    {
                        TokenString += CurrentChar;
                        GetNextChar();
                    }
                }

                if (!isHex && (CurrentChar == 'e' || CurrentChar == 'E'))
                {
                    TokenType = LexTypes.Float;
                    TokenString += CurrentChar;
                    GetNextChar();
                    if (CurrentChar == '-')
                    {
                        TokenString += CurrentChar;
                        GetNextChar();
                    }
                    while (CurrentChar.IsNumeric())
                    {
                        TokenString += CurrentChar;
                        GetNextChar();
                    }
                }
            }
            else if (CurrentChar == '\'' || CurrentChar == '\"') //Strings again
            {
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
                                TokenString += '\n';
                                break;
                            case 'r':
                                TokenString += '\r';
                                break;
                            case 'a':
                                TokenString += '\a';
                                break;
                            case 'b':
                                TokenString += '\b';
                                break;
                            case 'f':
                                TokenString += '\f';
                                break;
                            case 't':
                                TokenString += '\t';
                                break;
                            case 'v':
                                TokenString += '\v';
                                break;
                            case 'x':
                                {
                                    var str = "";
                                    GetNextChar();
                                    str += CurrentChar;
                                    GetNextChar();
                                    str += CurrentChar;
                                    TokenString += (char)Convert.ToInt64(str);
                                }
                                break;
                            default:
                                if (CurrentChar >= '0' && CurrentChar <= '7')
                                {
                                    var str = "";
                                    str += CurrentChar;
                                    GetNextChar();
                                    str += CurrentChar;
                                    GetNextChar();
                                    str += CurrentChar;
                                    TokenString += (char)Convert.ToInt64(str);
                                }
                                else
                                {
                                    TokenString += CurrentChar;
                                }
                                break;
                        }
                    }
                    else
                    {
                        TokenString += CurrentChar;
                    }

                    GetNextChar();
                }

                GetNextChar();

                TokenType = LexTypes.Str;
            }
            else //Single character
            {
                TokenType = (LexTypes)CurrentChar;

                if (CurrentChar != (char)0)
                {
                    GetNextChar();
                }

                if (TokenType == (LexTypes)'=' && CurrentChar == '=') // ==
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
                else if (TokenType == (LexTypes)'&' && CurrentChar == '&') // &&
                {
                    TokenType = LexTypes.AndAnd;
                    GetNextChar();
                }
                else if (TokenType == (LexTypes)'|' && CurrentChar == '=') // |=
                {
                    TokenType = LexTypes.OrEqual;
                    GetNextChar();
                }
                else if (TokenType == (LexTypes)'|' && CurrentChar == '|') // ||
                {
                    TokenType = LexTypes.OrOr;
                    GetNextChar();
                }
                else if (TokenType == (LexTypes)'^' && CurrentChar == '=') // ^=
                {
                    TokenType = LexTypes.XorEqual;
                    GetNextChar();
                }
                else if (TokenType == (LexTypes)'/')
                {
                    //omit regex for now
                    
                    TokenType = LexTypes.RegExp;
                    foreach (var item in notAllowedBeforeRegex)
                    {
                        if(item == PreviousTokenType)
                        {
                            TokenType = (LexTypes)'/';
                            break;
                        }
                    }

                    if (TokenType == LexTypes.RegExp)
                    {
                        TokenString = "/";

                        while(CurrentChar != 0 && CurrentChar != '/' && CurrentChar != '\n')
                        {
                            if(CurrentChar == '\\' && NextChar == '/')
                            {
                                TokenString += CurrentChar;
                                GetNextChar();
                            }

                            TokenString += CurrentChar;
                            GetNextChar();
                        }

                        if(CurrentChar == '/')
                        {
                            var regexStr = TokenString.Substring(1);
                            try
                            {
                                var regexObj = new Regex(regexStr, RegexOptions.ECMAScript);
                            }
                            catch(Exception ex)
                            {
                                throw new ScriptException("Invalid RegEx", ex);
                            }
                            
                            do
                            {
                                TokenString += CurrentChar;
                                GetNextChar();
                            } while (CurrentChar == 'g' || CurrentChar == 'i' || CurrentChar == 'm' || CurrentChar == 'y');
                        }
                        else
                        {

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
            }

            /* Something broke... */
            TokenLastEnd = TokenEnd;
            TokenEnd = dataPos - 3;
        }

        public ScriptLex GetSubLex(int lastPosition)
        {
            int lastCharIdx = TokenLastEnd + 1;

            if (lastCharIdx < dataEnd)
            {
                return new ScriptLex(this, lastPosition, lastCharIdx);
            }

            return new ScriptLex(this, lastPosition, dataEnd);
        }

        public string GetSubString(int pos)
        {
            int lastCharIndex = TokenLastEnd + 1;

            if (lastCharIndex < dataEnd)
            {
                return data.Substring(pos, lastCharIndex - pos);
            }

            return data.Substring(pos);
        }

        public void Match(LexTypes type)
        {
            if (TokenType != type)
            {
                var expectedName = Enum.GetName(typeof(LexTypes), type);
                if (string.IsNullOrEmpty(expectedName))
                {
                    expectedName = $"{(char)type}";
                }

                var foundName = Enum.GetName(typeof(LexTypes), TokenType);
                if (string.IsNullOrEmpty(foundName))
                {
                    foundName = $"{(char)TokenType}";
                }

                throw new ScriptException($"Unexpected token type. Expected {expectedName}, found {foundName}. Line: {LineNumber}, Col: {ColumnNumber}");
            }

            GetNextToken();
        }

        public static string LexTypesToString(LexTypes lexTypes)
        {
            if(lexTypes < (LexTypes)256 && lexTypes > 0)
            {
                return $"{(char)lexTypes}";
            }

            return lexTypes.ToString();
        }
    }
}
