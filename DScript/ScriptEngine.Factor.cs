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
    public sealed partial class ScriptEngine
    {
        private ScriptVarLink Factor(ref bool execute)
        {
            switch (currentLexer.TokenType)
            {
                case (ScriptLex.LexTypes)'(':
                {
                    currentLexer.Match((ScriptLex.LexTypes)'(');
                    var a = Base(ref execute);
                    currentLexer.Match((ScriptLex.LexTypes)')');
                    return a;
                }
                case ScriptLex.LexTypes.RTrue:
                    currentLexer.Match(ScriptLex.LexTypes.RTrue);
                    return new ScriptVarLink(new ScriptVar(1), null);
                case ScriptLex.LexTypes.RFalse:
                    currentLexer.Match(ScriptLex.LexTypes.RFalse);
                    return new ScriptVarLink(new ScriptVar(0), null);
                case ScriptLex.LexTypes.RNull:
                    currentLexer.Match(ScriptLex.LexTypes.RNull);
                    return new ScriptVarLink(new ScriptVar(null, ScriptVar.Flags.Null), null);
                case ScriptLex.LexTypes.RUndefined:
                    currentLexer.Match(ScriptLex.LexTypes.RUndefined);
                    return new ScriptVarLink(new ScriptVar(null, ScriptVar.Flags.Undefined), null);
                case ScriptLex.LexTypes.Id:
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
                        switch (currentLexer.TokenType)
                        {
                            // function call
                            case (ScriptLex.LexTypes)'(':
                                a = FunctionCall(ref execute, a, parent);
                                break;
                            // child access
                            case (ScriptLex.LexTypes)'.':
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
                                break;
                            }
                            // array access
                            case (ScriptLex.LexTypes)'[':
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

                                break;
                            }
                            default:
                                throw new ScriptException("WTF?");
                        }
                    }

                    return a;
                }
                case ScriptLex.LexTypes.Int:
                case ScriptLex.LexTypes.Float:
                {
                    var a = new ScriptVar(currentLexer.TokenString, currentLexer.TokenType == ScriptLex.LexTypes.Int ? ScriptVar.Flags.Integer : ScriptVar.Flags.Double);
                    currentLexer.Match(currentLexer.TokenType);
                    return new ScriptVarLink(a, null);
                }
                case ScriptLex.LexTypes.Str:
                {
                    var a = new ScriptVar(currentLexer.TokenString, ScriptVar.Flags.String);
                    currentLexer.Match(currentLexer.TokenType);
                    return new ScriptVarLink(a, null);
                }
                case (ScriptLex.LexTypes)'{':
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
                case (ScriptLex.LexTypes)'[':
                {
                    var idx = 0;
                    var contents = new ScriptVar(null, ScriptVar.Flags.Array);
                    //looking for JSON like arrays
                    currentLexer.Match((ScriptLex.LexTypes)'[');
                    while (currentLexer.TokenType != (ScriptLex.LexTypes)']')
                    {
                        if (execute)
                        {
                            var id = $"{idx}";

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
                case ScriptLex.LexTypes.RFunction:
                {
                    var funcVar = ParseFunctionDefinition();
                    if (funcVar.Name != string.Empty)
                    {
                        System.Diagnostics.Trace.TraceWarning("Functions not defined at statement level are not supposed to have a name");
                    }
                    return funcVar;
                }
                // new
                case ScriptLex.LexTypes.RNew:
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

                        // Resolve dotted / indexed access so that `new foo.Bar()`
                        // and `new ns["Widget"]()` locate the actual constructor.
                        while (currentLexer.TokenType == (ScriptLex.LexTypes)'.' ||
                               currentLexer.TokenType == (ScriptLex.LexTypes)'[')
                        {
                            if (currentLexer.TokenType == (ScriptLex.LexTypes)'.')
                            {
                                currentLexer.Match((ScriptLex.LexTypes)'.');
                                var memberName = currentLexer.TokenString;
                                var member = classOrFuncObject.Var.FindChild(memberName) ??
                                             FindInParentClasses(classOrFuncObject.Var, memberName);
                                classOrFuncObject = member ?? classOrFuncObject.Var.AddChild(memberName, null);
                                currentLexer.Match(ScriptLex.LexTypes.Id);
                            }
                            else
                            {
                                currentLexer.Match((ScriptLex.LexTypes)'[');
                                var indexVar = Base(ref execute);
                                currentLexer.Match((ScriptLex.LexTypes)']');
                                classOrFuncObject = classOrFuncObject.Var.FindChildOrCreate(indexVar.Var.String);
                            }
                        }

                        var obj = new ScriptVar(null, ScriptVar.Flags.Object);
                        var objLink = new ScriptVarLink(obj, null);

                        if (classOrFuncObject.Var.IsFunction)
                        {
                            // Link the new instance back to its constructor so that
                            // members defined on the constructor (e.g. Ctor.method =
                            // function(){}) are shared by every instance via the
                            // prototype-chain walk in FindInParentClasses.
                            obj.AddChild(ScriptVar.PrototypeClassName, classOrFuncObject.Var);

                            // Invoke the constructor: with arguments when a call list
                            // is present, otherwise (`new Ctor`) with no arguments.
                            var ctorResult = currentLexer.TokenType == (ScriptLex.LexTypes)'('
                                ? FunctionCall(ref execute, classOrFuncObject, obj)
                                : InvokeFunction(ref execute, classOrFuncObject, obj);

                            // If the constructor explicitly returns an object, that
                            // object becomes the result of the `new` expression.
                            if (ctorResult != null && ctorResult.Var.IsObject)
                            {
                                objLink = ctorResult;
                            }
                        }
                        else
                        {
                            obj.AddChild(ScriptVar.PrototypeClassName, classOrFuncObject.Var);

                            if (currentLexer.TokenType != (ScriptLex.LexTypes)'(') return objLink;
                            
                            currentLexer.Match((ScriptLex.LexTypes)'(');
                            currentLexer.Match((ScriptLex.LexTypes)')');
                        }

                        return objLink;
                    }

                    currentLexer.Match(ScriptLex.LexTypes.Id);

                    // Skip dotted / indexed member access on the constructor name.
                    while (currentLexer.TokenType == (ScriptLex.LexTypes)'.' ||
                           currentLexer.TokenType == (ScriptLex.LexTypes)'[')
                    {
                        if (currentLexer.TokenType == (ScriptLex.LexTypes)'.')
                        {
                            currentLexer.Match((ScriptLex.LexTypes)'.');
                            currentLexer.Match(ScriptLex.LexTypes.Id);
                        }
                        else
                        {
                            currentLexer.Match((ScriptLex.LexTypes)'[');
                            Base(ref execute);
                            currentLexer.Match((ScriptLex.LexTypes)']');
                        }
                    }

                    if (currentLexer.TokenType == (ScriptLex.LexTypes)'(')
                    {
                        currentLexer.Match((ScriptLex.LexTypes)'(');

                        // Parse (and discard) any constructor arguments so that
                        // `new X(a, b)` inside a not-taken branch does not throw.
                        while (currentLexer.TokenType != (ScriptLex.LexTypes)')')
                        {
                            Base(ref execute);

                            if (currentLexer.TokenType != (ScriptLex.LexTypes)')')
                            {
                                currentLexer.Match((ScriptLex.LexTypes)',');
                            }
                        }

                        currentLexer.Match((ScriptLex.LexTypes)')');
                    }

                    break;
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
