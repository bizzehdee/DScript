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
        /// Create a fresh call scope, binding <c>this</c> when a receiver is
        /// supplied. The caller then binds the declared parameters before running
        /// the function via <see cref="RunFunction"/>.
        /// </summary>
        private static ScriptVar CreateCallScope(ScriptVar thisArg)
        {
            var functionRoot = new ScriptVar(null, ScriptVar.Flags.Function);

            if (thisArg != null)
            {
                functionRoot.AddChildNoDup("this", thisArg);
            }

            return functionRoot;
        }

        /// <summary>
        /// Shared invocation tail used by every call path. Adds the return slot,
        /// pushes the call scope/stack, runs the native callback or the script body,
        /// then pops and yields the return value. <paramref name="functionRoot"/>
        /// must already hold <c>this</c> and the bound parameters.
        /// </summary>
        /// <param name="function">The function being invoked (used for the body/callback).</param>
        /// <param name="functionRoot">The prepared call scope.</param>
        /// <param name="functionLink">The link pushed onto the call stack.</param>
        /// <param name="execute">Execution flag, set true once the body completes.</param>
        /// <returns>The function's return value.</returns>
        private ScriptVar RunFunction(ScriptVar function, ScriptVar functionRoot, ScriptVarLink functionLink, ref bool execute)
        {
            var returnVarLink = functionRoot.AddChild(ScriptVar.ReturnVarName, null);

            scopes.PushBack(functionRoot);
            callStack.Push(functionLink);

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
                    execute = true;
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

            var functionRoot = CreateCallScope(thisArg);

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

            var execute = true;
            return RunFunction(function, functionRoot, new ScriptVarLink(function, null), ref execute);
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

            var functionRoot = CreateCallScope(parent);

            //no arguments supplied: every declared parameter is undefined
            var v = function.Var.FirstChild;
            while (v != null)
            {
                functionRoot.AddChild(v.Name, new ScriptVar(null, ScriptVar.Flags.Undefined));
                v = v.Next;
            }

            var result = RunFunction(function.Var, functionRoot, function, ref execute);

            return new ScriptVarLink(result, null);
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

                var functionRoot = CreateCallScope(parent);

                var v = function.Var.FirstChild;

                // Bind the supplied arguments to the declared parameters. Extra
                // arguments are still evaluated (for side effects) but discarded;
                // parameters with no matching argument are left undefined.
                while (currentLexer.TokenType != (ScriptLex.LexTypes)')')
                {
                    var value = Base(ref execute);
                    if (execute && v != null)
                    {
                        //primitives by value, objects/functions by reference
                        functionRoot.AddChild(v.Name, value.Var.IsBasic ? value.Var.DeepCopy() : value.Var);
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

                var result = RunFunction(function.Var, functionRoot, function, ref execute);

                return new ScriptVarLink(result, null);
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
