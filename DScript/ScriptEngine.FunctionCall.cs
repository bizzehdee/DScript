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
        /// <summary>
        /// Invoke a script function (user-defined or native) programmatically with
        /// the supplied arguments. This lets native/host code call back into script
        /// functions — e.g. Array.map/filter/forEach callbacks and sort comparators.
        /// </summary>
        /// <param name="function">The function ScriptVar to invoke.</param>
        /// <param name="thisArg">Value bound to <c>this</c> in the call (may be null).</param>
        /// <param name="args">Positional arguments; extras are ignored and missing
        /// parameters are bound as undefined.</param>
        /// <returns>The function's return value (undefined if it returned nothing).</returns>
        public ScriptVar CallFunction(ScriptVar function, ScriptVar thisArg, params ScriptVar[] args)
        {
            if (function == null || !function.IsFunction)
            {
                throw new ScriptException("Value is not a function");
            }

            var functionRoot = new ScriptVar(null, ScriptVar.Flags.Function);

            if (thisArg != null)
            {
                functionRoot.AddChildNoDup("this", thisArg);
            }

            //bind arguments positionally to the declared parameters
            var argIndex = 0;
            var param = function.FirstChild;
            while (param != null)
            {
                var argValue = args != null && argIndex < args.Length && args[argIndex] != null
                    ? args[argIndex]
                    : new ScriptVar(null, ScriptVar.Flags.Undefined);

                //primitives are passed by value, objects/functions by reference
                functionRoot.AddChild(param.Name, argValue.IsBasic ? argValue.DeepCopy() : argValue);

                argIndex++;
                param = param.Next;
            }

            var returnVarLink = functionRoot.AddChild(ScriptVar.ReturnVarName, null);

            scopes.PushBack(functionRoot);
            callStack.Push(new ScriptVarLink(function, null));

            var execute = true;

            if (function.IsNative)
            {
                var callback = function.GetCallback();
                callback?.Invoke(functionRoot, function.GetCallbackUserData());
            }
            else
            {
                var oldLex = currentLexer;
                currentLexer = new ScriptLex(function.String);

                try
                {
                    Block(ref execute);
                }
                finally
                {
                    currentLexer = oldLex;
                    //a loop never spans a call boundary
                    loopControl = LoopControl.None;
                }
            }

            callStack.Pop();
            scopes.PopBack();

            return returnVarLink.Var;
        }

        /// <summary>
        /// Invoke a function with no arguments and no parentheses in the token
        /// stream (used by <c>new Ctor</c> without a call list). Returns the
        /// function's return-value link, mirroring <see cref="FunctionCall"/>.
        /// </summary>
        private ScriptVarLink InvokeFunction(ref bool execute, ScriptVarLink function, ScriptVar parent)
        {
            if (!execute) return function;

            if (!function.Var.IsFunction)
            {
                throw new ScriptException($"{function.Name} is not a function");
            }

            var functionRoot = new ScriptVar(null, ScriptVar.Flags.Function);

            if (parent != null)
            {
                functionRoot.AddChildNoDup("this", parent);
            }

            // No arguments supplied: every declared parameter is undefined.
            var v = function.Var.FirstChild;
            while (v != null)
            {
                functionRoot.AddChild(v.Name, new ScriptVar(null, ScriptVar.Flags.Undefined));
                v = v.Next;
            }

            var returnVarLink = functionRoot.AddChild(ScriptVar.ReturnVarName, null);

            scopes.PushBack(functionRoot);
            callStack.Push(function);

            if (function.Var.IsNative)
            {
                var func = function.Var.GetCallback();
                func?.Invoke(functionRoot, function.Var.GetCallbackUserData());
            }
            else
            {
                var oldLex = currentLexer;
                currentLexer = new ScriptLex(function.Var.String);

                try
                {
                    Block(ref execute);
                    execute = true;
                    //a loop construct never spans a function boundary
                    loopControl = LoopControl.None;
                }
                finally
                {
                    currentLexer = oldLex;
                }
            }

            callStack.Pop();
            scopes.PopBack();

            var returnVar = new ScriptVarLink(returnVarLink.Var, null);
            functionRoot.RemoveLink(returnVarLink);

            return returnVar;
        }

        private ScriptVarLink FunctionCall(ref bool execute, ScriptVarLink function, ScriptVar parent)
        {
            if (execute)
            {
                if (!function.Var.IsFunction)
                {
                    throw new ScriptException($"{function.Name} is not a function");
                }

                currentLexer.Match((ScriptLex.LexTypes)'(');
                var functionRoot = new ScriptVar(null, ScriptVar.Flags.Function);

                if (parent != null)
                {
                    functionRoot.AddChildNoDup("this", parent);
                }

                var v = function.Var.FirstChild;

                // Bind the supplied arguments to the declared parameters. Extra
                // arguments are still evaluated (for side effects) but discarded;
                // parameters with no matching argument are left undefined.
                while (currentLexer.TokenType != (ScriptLex.LexTypes)')')
                {
                    var value = Base(ref execute);
                    if (execute && v != null)
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

                    if (v != null)
                    {
                        v = v.Next;
                    }

                    if (currentLexer.TokenType != (ScriptLex.LexTypes)')')
                    {
                        currentLexer.Match((ScriptLex.LexTypes)',');
                    }
                }

                // Declared parameters that received no argument are bound as
                // undefined so the function body can still reference them.
                while (execute && v != null)
                {
                    functionRoot.AddChild(v.Name, new ScriptVar(null, ScriptVar.Flags.Undefined));
                    v = v.Next;
                }

                currentLexer.Match((ScriptLex.LexTypes)')');

                var returnVarLink = functionRoot.AddChild(ScriptVar.ReturnVarName, null);

                scopes.PushBack(functionRoot);

                callStack.Push(function);

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
                        //a loop construct never spans a function boundary
                        loopControl = LoopControl.None;
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

                callStack.Pop();
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
