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

namespace DScript
{
    public partial class ScriptEngine
    {
        private ScriptVarLink Shift(ref bool execute)
        {
            var a = Expression(ref execute);

            if (currentLexer.TokenType == ScriptLex.LexTypes.LShift ||
                currentLexer.TokenType == ScriptLex.LexTypes.RShift ||
                currentLexer.TokenType == ScriptLex.LexTypes.RShiftUnsigned)
            {
                var op = currentLexer.TokenType;
                currentLexer.Match(op);
                var b = Base(ref execute);

                var shift = execute ? b.Var.Int : 0;

                if (execute)
                {
                    if (op == ScriptLex.LexTypes.LShift)
                    {
                        a.Var.Int = (a.Var.Int << shift);
                    }

                    if (op == ScriptLex.LexTypes.RShift)
                    {
                        a.Var.Int = (a.Var.Int >> shift);
                    }

                    if (op == ScriptLex.LexTypes.RShiftUnsigned)
                    {
                        a.Var.Int = ((int)(((uint)a.Var.Int) >> shift));
                    }
                }
            }

            return a;
        }
    }
}
