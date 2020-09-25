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

namespace DScript
{
    public partial class ScriptEngine
    {
        private ScriptVarLink FunctionCall(ref bool execute, ScriptVarLink function, ScriptVar parent)
        {
            if (execute)
            {
                if (!function.Var.IsFunction)
                {
                    throw new ScriptException(String.Format("{0} is not a function", function.Name));
                }

                currentLexer.Match((ScriptLex.LexTypes)'(');
                var functionRoot = new ScriptVar(null, ScriptVar.Flags.Function);

                if (parent != null)
                {
                    functionRoot.AddChildNoDup("this", parent);
                }

                var v = function.Var.FirstChild;
                while (v != null)
                {
                    var value = Base(ref execute);
                    if (execute)
                    {
                        if (value.Var.IsBasic)
                        {
                            //pass by val
                            functionRoot.AddChild(v.Name, value.Var.DeepCopy());
                        }
                        else
                        {
                            //pass by ref
                            functionRoot.AddChild(v.Name, value.Var);
                        }
                    }

                    if (currentLexer.TokenType != (ScriptLex.LexTypes)')')
                    {
                        currentLexer.Match((ScriptLex.LexTypes)',');
                    }

                    v = v.Next;
                }

                currentLexer.Match((ScriptLex.LexTypes)')');

                var returnVarLink = functionRoot.AddChild(ScriptVar.ReturnVarName, null);

                scopes.PushBack(functionRoot);

                //callStack.PushBack(string.Format("{0} from line {1}", function.Name, currentLexer.LineNumber));

                if (function.Var.IsNative)
                {
                    var func = function.Var.GetCallback();
                    func?.Invoke(functionRoot, function.Var.GetCallbackUserData());
                }
                else
                {
                    var oldLex = currentLexer;
                    var newLex = new ScriptLex(function.Var.String);
                    currentLexer = newLex;

                    try
                    {
                        Block(ref execute);

                        execute = true;
                    }
                    catch
                    {
                        throw;
                    }
                    finally
                    {
                        currentLexer = oldLex;
                    }
                }

                //callStack.PopBack();
                scopes.PopBack();

                var returnVar = new ScriptVarLink(returnVarLink.Var, null);
                functionRoot.RemoveLink(returnVarLink);

                return returnVar;
            }
            else
            {

                //not executing the function, just parsing it out
                currentLexer.Match((ScriptLex.LexTypes)'(');

                while (currentLexer.TokenType != (ScriptLex.LexTypes)')')
                {
                    Base(ref execute);

                    if (currentLexer.TokenType != (ScriptLex.LexTypes)')')
                    {
                        currentLexer.Match((ScriptLex.LexTypes)',');
                    }
                }

                currentLexer.Match((ScriptLex.LexTypes)')');

                if (currentLexer.TokenType == (ScriptLex.LexTypes)'{') //WTF?
                {
                    Block(ref execute);
                }

                return function;
            }
        }
    }
}
