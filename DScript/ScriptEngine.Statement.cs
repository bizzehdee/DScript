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

namespace DScript
{
    public sealed partial class ScriptEngine
    {
        private void Statement(ref bool execute)
        {
            switch (currentLexer.TokenType)
            {
                case (ScriptLex.LexTypes)'{':
                    //code block
                    Block(ref execute);
                    break;
                case (ScriptLex.LexTypes)';':
                    //allow for multiple semi colon such as ;;;
                    currentLexer.Match((ScriptLex.LexTypes)';');
                    break;
                case ScriptLex.LexTypes.RVar:
                case ScriptLex.LexTypes.RConst:
                {
                    //creating variables
                    //TODO: make this less shit

                    var readOnly = currentLexer.TokenType == ScriptLex.LexTypes.RConst;

                    currentLexer.Match(currentLexer.TokenType);

                    while (currentLexer.TokenType != (ScriptLex.LexTypes)';')
                    {
                        ScriptVarLink a = null;
                        if (execute)
                        {
                            a = scopes.Back().FindChildOrCreate(currentLexer.TokenString, ScriptVar.Flags.Undefined, readOnly);
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
                                a?.ReplaceWith(varLink);
                            }
                        }

                        if (currentLexer.TokenType != (ScriptLex.LexTypes)';')
                        {
                            currentLexer.Match((ScriptLex.LexTypes)',');
                        }
                    }

                    currentLexer.Match((ScriptLex.LexTypes)';');
                    break;
                }
                case ScriptLex.LexTypes.RIf:
                {
                    //if condition
                    currentLexer.Match(ScriptLex.LexTypes.RIf);
                    currentLexer.Match((ScriptLex.LexTypes)'(');
                    var varLink = Base(ref execute);
                    currentLexer.Match((ScriptLex.LexTypes)')');

                    var condition = execute && varLink.Var.Bool;
                    var noExecute = false;
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

                    break;
                }
                case ScriptLex.LexTypes.RWhile:
                {
                    //while loop
                    currentLexer.Match(ScriptLex.LexTypes.RWhile);
                    currentLexer.Match((ScriptLex.LexTypes)'(');

                    var whileConditionStart = currentLexer.TokenStart;
                    var noExecute = false;
                    var condition = Base(ref execute);
                    var loopCondition = execute && condition.Var.Bool;

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

                        loopCondition = execute && condition.Var.Bool;

                        if (loopCondition)
                        {
                            whileBody.Reset();
                            currentLexer = whileBody;
                            Statement(ref execute);
                        }
                    }

                    currentLexer = oldLex;
                    break;
                }
                case ScriptLex.LexTypes.RFor:
                {
                    //for loop
                    currentLexer.Match(ScriptLex.LexTypes.RFor);
                    currentLexer.Match((ScriptLex.LexTypes)'(');

                    Statement(ref execute); //init

                    var forConditionStart = currentLexer.TokenStart;
                    var condition = Base(ref execute);
                    var noExecute = false;
                    var loopCondition = execute && condition.Var.Bool;

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

                        loopCondition = condition.Var.Bool;

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
                    break;
                }
                case ScriptLex.LexTypes.RReturn:
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
                    break;
                }
                case ScriptLex.LexTypes.RFunction:
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

                    break;
                }
                case ScriptLex.LexTypes.RTry:
                {
                    var tryBlock = ParseDefinition(ScriptLex.LexTypes.RTry);
                    ScriptVarLink catchBlock = null, finallyBlock = null;

                    var originalLexer = currentLexer;

                    if (currentLexer.TokenType == ScriptLex.LexTypes.RCatch)
                    {
                        catchBlock = ParseDefinition(ScriptLex.LexTypes.RCatch);
                    }

                    if (currentLexer.TokenType == ScriptLex.LexTypes.RFinally)
                    {
                        finallyBlock = ParseDefinition(ScriptLex.LexTypes.RFinally);
                    }

                    try
                    {
                        var oldLex = currentLexer;
                        var newLex = new ScriptLex(tryBlock.Var.String);
                        currentLexer = newLex;

                        Block(ref execute);

                        execute = true;

                        currentLexer = oldLex;
                    }
                    catch(JITException ex)
                    {
                        if (catchBlock != null)
                        {
                            var catchScope = new ScriptVar(null, ScriptVar.Flags.Object);

                            var v = catchBlock?.Var?.FirstChild;
                            if(v != null)
                            {
                                catchScope.AddChild(v.Name, ex.VarObj);
                            }

                            var oldLex = currentLexer;
                            var newLex = new ScriptLex(catchBlock.Var.String);
                            currentLexer = newLex;

                            scopes.PushBack(catchScope);

                            Block(ref execute);

                            scopes.PopBack();

                            execute = true;

                            currentLexer = oldLex;
                        }
                        else
                        {
                            throw new ScriptException(ex.Message, ex);
                        }
                    }
                    finally
                    {
                        if(finallyBlock != null)
                        {
                            var oldLex = currentLexer;
                            var newLex = new ScriptLex(finallyBlock.Var.String);
                            currentLexer = newLex;

                            Block(ref execute);

                            execute = true;

                            currentLexer = oldLex;
                        }
                    }

                    currentLexer = originalLexer;
                    break;
                }
                case ScriptLex.LexTypes.RThrow:
                {
                    currentLexer.Match(ScriptLex.LexTypes.RThrow);

                    var message = new ScriptVar();
                
                    if (currentLexer.TokenType == (ScriptLex.LexTypes)';')
                    {
                        currentLexer.Match((ScriptLex.LexTypes)';');
                    } 
                    else
                    {

                        var res = Base(ref execute);
                        message = res.Var;
                    }

                    throw new JITException(message);
                }
                case ScriptLex.LexTypes.RSwitch:
                {
                    var noExecute = false;
                    var hasMatched = false;

                    currentLexer.Match(ScriptLex.LexTypes.RSwitch);
                    currentLexer.Match((ScriptLex.LexTypes)'(');

                    var varLink = Base(ref execute);

                    currentLexer.Match((ScriptLex.LexTypes)')');

                    currentLexer.Match((ScriptLex.LexTypes)'{');
                    for (; ;)
                    {
                        if (currentLexer.TokenType is 
                            ScriptLex.LexTypes.RDefault or 
                            ScriptLex.LexTypes.RCase)
                        {
                            if (currentLexer.TokenType == ScriptLex.LexTypes.RCase)
                            {
                                currentLexer.Match(ScriptLex.LexTypes.RCase);

                                var caseVarLink = Base(ref execute);

                                currentLexer.Match((ScriptLex.LexTypes)':');

                                //var caseBodyStart = currentLexer.TokenStart;

                                if (execute && caseVarLink.Var.MathsOp(varLink.Var, ScriptLex.LexTypes.Equal).Bool)
                                {
                                    hasMatched = true;
                                    Statement(ref execute);
                                }
                                else
                                {
                                    Statement(ref noExecute);
                                }
                            }
                            else
                            {
                                currentLexer.Match(ScriptLex.LexTypes.RDefault);
                                currentLexer.Match((ScriptLex.LexTypes)':');

                                //var caseBodyStart = currentLexer.TokenStart;

                                if (execute && !hasMatched)
                                {
                                    Statement(ref execute);
                                }
                                else
                                {
                                    Statement(ref noExecute);
                                }
                            }

                            currentLexer.Match(ScriptLex.LexTypes.RBreak);
                            currentLexer.Match((ScriptLex.LexTypes)';');
                        }
                        else if (currentLexer.TokenType == (ScriptLex.LexTypes)'}')
                        {
                            break;
                        }
                        else
                        {
                            throw new ScriptException("");
                        }
                    }

                    currentLexer.Match((ScriptLex.LexTypes)'}');
                    break;
                }
                default:
                    //execute a basic statement
                    Base(ref execute);
                    currentLexer.Match((ScriptLex.LexTypes)';');
                    break;
            }
        }
    }
}
