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
        private ScriptVarLink Factor(ref bool execute)
        {
            if (currentLexer.TokenType == (ScriptLex.LexTypes)'(')
            {
                currentLexer.Match((ScriptLex.LexTypes)'(');
                var a = Base(ref execute);
                currentLexer.Match((ScriptLex.LexTypes)')');
                return a;
            }
            if (currentLexer.TokenType == ScriptLex.LexTypes.RTrue)
            {
                currentLexer.Match(ScriptLex.LexTypes.RTrue);
                return new ScriptVarLink(new ScriptVar(1), null);
            }
            if (currentLexer.TokenType == ScriptLex.LexTypes.RFalse)
            {
                currentLexer.Match(ScriptLex.LexTypes.RFalse);
                return new ScriptVarLink(new ScriptVar(0), null);
            }
            if (currentLexer.TokenType == ScriptLex.LexTypes.RNull)
            {
                currentLexer.Match(ScriptLex.LexTypes.RNull);
                return new ScriptVarLink(new ScriptVar(null, ScriptVar.Flags.Null), null);
            }
            if (currentLexer.TokenType == ScriptLex.LexTypes.RUndefined)
            {
                currentLexer.Match(ScriptLex.LexTypes.RUndefined);
                return new ScriptVarLink(new ScriptVar(null, ScriptVar.Flags.Undefined), null);
            }
            if (currentLexer.TokenType == ScriptLex.LexTypes.Id)
            {
                var a = execute ? FindInScopes(currentLexer.TokenString) : new ScriptVarLink(new ScriptVar(), null);

                ScriptVar parent = null;

                if (execute && a == null)
                {
                    a = new ScriptVarLink(new ScriptVar(), currentLexer.TokenString);
                }

                currentLexer.Match(ScriptLex.LexTypes.Id);

                while (currentLexer.TokenType == (ScriptLex.LexTypes)'(' || currentLexer.TokenType == (ScriptLex.LexTypes)'.' || currentLexer.TokenType == (ScriptLex.LexTypes)'[')
                {
                    if (currentLexer.TokenType == (ScriptLex.LexTypes)'(') // function call
                    {
                        a = FunctionCall(ref execute, a, parent);
                    }
                    else if (currentLexer.TokenType == (ScriptLex.LexTypes)'.') // child access
                    {
                        currentLexer.Match((ScriptLex.LexTypes)'.');
                        if (execute)
                        {
                            var name = currentLexer.TokenString;
                            var child = a.Var.FindChild(name);

                            if (child == null)
                            {
                                child = FindInParentClasses(a.Var, name);
                            }

                            if (child == null)
                            {
                                if (a.Var.IsArray && name == "length")
                                {
                                    var length = a.Var.GetArrayLength();
                                    child = new ScriptVarLink(new ScriptVar(length), null);
                                }
                                else if (a.Var.IsString && name == "length")
                                {
                                    var length = a.Var.String.Length;
                                    child = new ScriptVarLink(new ScriptVar(length), null);
                                }
                                else
                                {
                                    child = a.Var.AddChild(name, null);
                                }
                            }

                            parent = a.Var;
                            a = child;
                        }

                        currentLexer.Match(ScriptLex.LexTypes.Id);
                    }
                    else if (currentLexer.TokenType == (ScriptLex.LexTypes)'[') // array access
                    {
                        currentLexer.Match((ScriptLex.LexTypes)'[');
                        var index = Base(ref execute);
                        currentLexer.Match((ScriptLex.LexTypes)']');

                        if (execute)
                        {
                            var child = a.Var.FindChildOrCreate(index.Var.String);
                            parent = a.Var;
                            a = child;
                        }
                    }
                    else
                    {
                        throw new ScriptException("WTF?");
                    }
                }

                return a;
            }

            if (currentLexer.TokenType == ScriptLex.LexTypes.Int || currentLexer.TokenType == ScriptLex.LexTypes.Float)
            {
                var a = new ScriptVar(currentLexer.TokenString, currentLexer.TokenType == ScriptLex.LexTypes.Int ? ScriptVar.Flags.Integer : ScriptVar.Flags.Double);
                currentLexer.Match(currentLexer.TokenType);
                return new ScriptVarLink(a, null);
            }

            if (currentLexer.TokenType == ScriptLex.LexTypes.Str)
            {
                var a = new ScriptVar(currentLexer.TokenString, ScriptVar.Flags.String);
                currentLexer.Match(currentLexer.TokenType);
                return new ScriptVarLink(a, null);
            }

            if (currentLexer.TokenType == (ScriptLex.LexTypes)'{')
            {
                var contents = new ScriptVar(null, ScriptVar.Flags.Object);
                //looking for JSON like objects
                currentLexer.Match((ScriptLex.LexTypes)'{');
                while (currentLexer.TokenType != (ScriptLex.LexTypes)'}')
                {
                    var id = currentLexer.TokenString;

                    if (currentLexer.TokenType == ScriptLex.LexTypes.Str)
                    {
                        currentLexer.Match(ScriptLex.LexTypes.Str);
                    }
                    else
                    {
                        currentLexer.Match(ScriptLex.LexTypes.Id);
                    }

                    currentLexer.Match((ScriptLex.LexTypes)':');

                    if (execute)
                    {
                        var a = Base(ref execute);
                        contents.AddChild(id, a.Var);
                    }

                    if (currentLexer.TokenType != (ScriptLex.LexTypes)'}')
                    {
                        currentLexer.Match((ScriptLex.LexTypes)',');
                    }
                }
                currentLexer.Match((ScriptLex.LexTypes)'}');

                return new ScriptVarLink(contents, null);
            }

            if (currentLexer.TokenType == (ScriptLex.LexTypes)'[')
            {
                int idx = 0;
                var contents = new ScriptVar(null, ScriptVar.Flags.Array);
                //looking for JSON like arrays
                currentLexer.Match((ScriptLex.LexTypes)'[');
                while (currentLexer.TokenType != (ScriptLex.LexTypes)']')
                {
                    if (execute)
                    {
                        var id = string.Format("{0}", idx);

                        var a = Base(ref execute);
                        contents.AddChild(id, a.Var);
                    }

                    if (currentLexer.TokenType != (ScriptLex.LexTypes)']')
                    {
                        currentLexer.Match((ScriptLex.LexTypes)',');
                    }

                    idx++;
                }
                currentLexer.Match((ScriptLex.LexTypes)']');

                return new ScriptVarLink(contents, null);
            }

            if (currentLexer.TokenType == ScriptLex.LexTypes.RFunction)
            {
                var funcVar = ParseFunctionDefinition();
                if (funcVar.Name != string.Empty)
                {
                    System.Diagnostics.Trace.TraceWarning("Functions not defined at statement level are not supposed to have a name");
                }
                return funcVar;
            }

            if (currentLexer.TokenType == ScriptLex.LexTypes.RNew) // new
            {
                currentLexer.Match(ScriptLex.LexTypes.RNew);

                var className = currentLexer.TokenString;
                if (execute)
                {
                    var classOrFuncObject = FindInScopes(className);
                    if (classOrFuncObject == null)
                    {
                        System.Diagnostics.Trace.TraceWarning("{0} is not a valid class name", className);
                        return new ScriptVarLink(new ScriptVar(), null);
                    }

                    currentLexer.Match(ScriptLex.LexTypes.Id);

                    var obj = new ScriptVar(null, ScriptVar.Flags.Object);
                    var objLink = new ScriptVarLink(obj, null);

                    if (classOrFuncObject.Var.IsFunction)
                    {
                        FunctionCall(ref execute, classOrFuncObject, obj);
                    }
                    else
                    {
                        obj.AddChild(ScriptVar.PrototypeClassName, classOrFuncObject.Var);

                        if (currentLexer.TokenType == (ScriptLex.LexTypes)'(')
                        {
                            currentLexer.Match((ScriptLex.LexTypes)'(');
                            currentLexer.Match((ScriptLex.LexTypes)')');
                        }
                    }

                    return objLink;
                }
                else
                {
                    currentLexer.Match(ScriptLex.LexTypes.Id);
                    if (currentLexer.TokenType == (ScriptLex.LexTypes)'(')
                    {
                        currentLexer.Match((ScriptLex.LexTypes)'(');
                        currentLexer.Match((ScriptLex.LexTypes)')');
                    }
                }
            }

            if(currentLexer.TokenType == ScriptLex.LexTypes.RegExp)
            {
                var a = new ScriptVar(currentLexer.TokenString, ScriptVar.Flags.Regexp);
                currentLexer.Match(currentLexer.TokenType);
                return new ScriptVarLink(a, null);
            }

            currentLexer.Match(ScriptLex.LexTypes.Eof);

            return null;
        }
    }
}
