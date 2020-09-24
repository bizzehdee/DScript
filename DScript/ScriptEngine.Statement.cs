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
        private void Statement(ref bool execute)
        {
            if (currentLexer.TokenType == (ScriptLex.LexTypes)'{')
            {
                //code block
                Block(ref execute);
            }
            else if (currentLexer.TokenType == (ScriptLex.LexTypes)';')
            {
                //allow for multiple semi colon such as ;;;
                currentLexer.Match((ScriptLex.LexTypes)';');
            }
            else if (currentLexer.TokenType == ScriptLex.LexTypes.RVar)
            {
                //creating variables
                //TODO: make this less shit

                currentLexer.Match(ScriptLex.LexTypes.RVar);

                while (currentLexer.TokenType != (ScriptLex.LexTypes)';')
                {
                    ScriptVarLink a = null;
                    if (execute)
                    {
                        a = scopes.Back().FindChildOrCreate(currentLexer.TokenString);
                    }

                    currentLexer.Match(ScriptLex.LexTypes.Id);

                    //get through the dots
                    while (currentLexer.TokenType == (ScriptLex.LexTypes)'.')
                    {
                        currentLexer.Match((ScriptLex.LexTypes)'.');
                        if (execute)
                        {
                            var aLast = a;
                            if (aLast != null)
                            {
                                a = aLast.Var.FindChildOrCreate(currentLexer.TokenString);
                            }
                        }

                        currentLexer.Match(ScriptLex.LexTypes.Id);
                    }

                    //initialiser
                    if (currentLexer.TokenType == (ScriptLex.LexTypes)'=')
                    {
                        currentLexer.Match((ScriptLex.LexTypes)'=');
                        var varLink = Base(ref execute);
                        if (execute)
                        {
                            if (a != null)
                            {
                                a.ReplaceWith(varLink);
                            }
                        }
                    }

                    if (currentLexer.TokenType != (ScriptLex.LexTypes)';')
                    {
                        currentLexer.Match((ScriptLex.LexTypes)',');
                    }
                }

                currentLexer.Match((ScriptLex.LexTypes)';');
            }
            else if (currentLexer.TokenType == ScriptLex.LexTypes.RIf)
            {
                //if condition
                currentLexer.Match(ScriptLex.LexTypes.RIf);
                currentLexer.Match((ScriptLex.LexTypes)'(');
                var varLink = Base(ref execute);
                currentLexer.Match((ScriptLex.LexTypes)')');

                bool condition = execute && varLink.Var.GetBool();
                bool noExecute = false;
                if (condition)
                {
                    Statement(ref execute);
                }
                else
                {
                    Statement(ref noExecute);
                }

                if (currentLexer.TokenType == ScriptLex.LexTypes.RElse)
                {
                    //else part of an if
                    currentLexer.Match(ScriptLex.LexTypes.RElse);

                    if (condition)
                    {
                        Statement(ref noExecute);
                    }
                    else
                    {
                        Statement(ref execute);
                    }
                }
            }
            else if (currentLexer.TokenType == ScriptLex.LexTypes.RWhile)
            {
                //while loop
                currentLexer.Match(ScriptLex.LexTypes.RWhile);
                currentLexer.Match((ScriptLex.LexTypes)'(');

                var whileConditionStart = currentLexer.TokenStart;
                var noExecute = false;
                var condition = Base(ref execute);
                var loopCondition = execute && condition.Var.GetBool();

                var whileCond = currentLexer.GetSubLex(whileConditionStart);
                currentLexer.Match((ScriptLex.LexTypes)')');

                var whileBodyStart = currentLexer.TokenStart;

                if(loopCondition)
                {
                    Statement(ref execute);
                }
                else
                {
                    Statement(ref noExecute);
                }
                
                var whileBody = currentLexer.GetSubLex(whileBodyStart);
                var oldLex = currentLexer;

                //TODO: possible maximum itteration limit?
                while (loopCondition)
                {
                    whileCond.Reset();

                    currentLexer = whileCond;

                    condition = Base(ref execute);

                    loopCondition = execute && condition.Var.GetBool();

                    if (loopCondition)
                    {
                        whileBody.Reset();
                        currentLexer = whileBody;
                        Statement(ref execute);
                    }
                }

                currentLexer = oldLex;
            }
            else if (currentLexer.TokenType == ScriptLex.LexTypes.RFor)
            {
                //for loop
                currentLexer.Match(ScriptLex.LexTypes.RFor);
                currentLexer.Match((ScriptLex.LexTypes)'(');

                Statement(ref execute); //init

                var forConditionStart = currentLexer.TokenStart;
                var condition = Base(ref execute);
                var noExecute = false;
                var loopCondition = execute && condition.Var.GetBool();

                var forCondition = currentLexer.GetSubLex(forConditionStart);

                currentLexer.Match((ScriptLex.LexTypes)';');

                var forIterStart = currentLexer.TokenStart;

                Base(ref noExecute);

                var forIter = currentLexer.GetSubLex(forIterStart);

                currentLexer.Match((ScriptLex.LexTypes)')');

                var forBodyStart = currentLexer.TokenStart;

                if (loopCondition)
                {
                    Statement(ref execute);
                }
                else
                {
                    Statement(ref noExecute);
                }

                var forBody = currentLexer.GetSubLex(forBodyStart);
                var oldLex = currentLexer;
                if (loopCondition)
                {
                    forIter.Reset();
                    currentLexer = forIter;

                    Base(ref execute);
                }

                //TODO: limit number of iterations?
                while (execute && loopCondition)
                {
                    forCondition.Reset();
                    currentLexer = forCondition;

                    condition = Base(ref execute);

                    loopCondition = condition.Var.GetBool();

                    if (execute && loopCondition)
                    {
                        forBody.Reset();
                        currentLexer = forBody;

                        Statement(ref execute);
                    }

                    if (execute && loopCondition)
                    {
                        forIter.Reset();
                        currentLexer = forIter;

                        Base(ref execute);
                    }
                }

                currentLexer = oldLex;
            }
            else if (currentLexer.TokenType == ScriptLex.LexTypes.RReturn)
            {
                currentLexer.Match(ScriptLex.LexTypes.RReturn);

                ScriptVarLink res = null;
                if (currentLexer.TokenType != (ScriptLex.LexTypes)';')
                {
                    res = Base(ref execute);
                }
                if (execute)
                {
                    var resultVar = scopes.Back().FindChild(ScriptVar.ReturnVarName);
                    if (resultVar != null)
                    {
                        resultVar.ReplaceWith(res);
                    }
                    else
                    {
                        //return statement outside of function???
                        System.Diagnostics.Trace.TraceWarning("Return statement outside of a function, what is going on?");
                    }
                    execute = false;
                }

                currentLexer.Match((ScriptLex.LexTypes)';');
            }
            else if (currentLexer.TokenType == ScriptLex.LexTypes.RFunction)
            {
                //function
                var funcVar = ParseFunctionDefinition();
                if (execute)
                {
                    if (funcVar.Name == string.Empty)
                    {
                        //functions must have a name at statement level
                    }
                    else
                    {
                        var v = scopes.Back();
                        v.AddChildNoDup(funcVar.Name, funcVar.Var);
                    }
                }
            }
            else 
            {
                //execute a basic statement
                Base(ref execute);
                currentLexer.Match((ScriptLex.LexTypes)';');
            }
        }
    }
}
