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
using System.Reflection;
using System.Text;

namespace DScript
{
    public partial class ScriptEngine : IDisposable
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
                    stringClass.UnRef();
                    arrayClass.UnRef();
                    objectClass.UnRef();
                    Root.UnRef();

                    if (currentLexer != null)
                    {
                        currentLexer.Dispose();
                    }
                }

                // Indicate that the instance has been disposed.
                _disposed = true;
            }
        }
        #endregion

        private readonly ScriptVar stringClass;
        private readonly ScriptVar objectClass;
        private readonly ScriptVar arrayClass;
        private Stack<ScriptVar> scopes;
        private readonly Stack<string> callStack;

        private ScriptLex currentLexer;

        public delegate void ScriptCallbackCB(ScriptVar var, object userdata, ScriptVar parent = null);

        public ScriptVar Root { get; private set; }

        public ScriptEngine()
        {
            currentLexer = null;

            scopes = new Stack<ScriptVar>();
            callStack = new Stack<string>();

            Root = (new ScriptVar(null, ScriptVar.Flags.Object)).Ref();

            stringClass = (new ScriptVar(null, ScriptVar.Flags.Object)).Ref();
            objectClass = (new ScriptVar(null, ScriptVar.Flags.Object)).Ref();
            arrayClass = (new ScriptVar(null, ScriptVar.Flags.Object)).Ref();

            Root.AddChild("String", stringClass);
            Root.AddChild("Object", objectClass);
            Root.AddChild("Array", arrayClass);
         }

        public void Trace()
        {
            Root.Trace(0, null);
        }

        public void Execute(string code)
        {
            var oldLex = currentLexer;
            var oldScopes = scopes;

            using (currentLexer = new ScriptLex(code))
            {
                scopes.Clear();
                scopes.Push(Root);
                callStack.Clear();

                try
                {
                    while (currentLexer.TokenType != 0)
                    {
                        bool execute = true;
                        Statement(ref execute);
                    }
                }
                catch (ScriptException ex)
                {
                    var errorMessage = new StringBuilder(string.Format("ERROR on line {0} column {1} [{2}]", currentLexer.LineNumber, currentLexer.ColumnNumber, ex.Message));

                    int i = 0;
                    foreach (ScriptVar scriptVar in scopes)
                    {
                        errorMessage.AppendLine();
                        errorMessage.Append(i++ + ": " + scriptVar);
                    }

                    Console.Error.WriteLine(errorMessage.ToString());
                    throw;
                }
            }

            currentLexer = oldLex;
            scopes = oldScopes;
        }

        public ScriptVarLink EvalComplex(string code)
        {
            var oldLex = currentLexer;
            var oldScopes = scopes;

            currentLexer = new ScriptLex(code);

            callStack.Clear();
            scopes.Clear();
            scopes.Push(Root);

            ScriptVarLink v = null;

            try
            {
                bool execute = true;
                do
                {
                    v = Base(ref execute);
                    if (currentLexer.TokenType != ScriptLex.LexTypes.Eof)
                    {
                        currentLexer.Match((ScriptLex.LexTypes)';');
                    }
                } while (currentLexer.TokenType != ScriptLex.LexTypes.Eof);
            }
            catch (ScriptException ex)
            {
                var errorMessage = new StringBuilder(string.Format("ERROR on line {0} column {1} [{2}]", currentLexer.LineNumber, currentLexer.ColumnNumber, ex.Message));

                int i = 0;
                foreach (ScriptVar scriptVar in scopes)
                {
                    errorMessage.AppendLine();
                    errorMessage.Append(i++ + ": " + scriptVar);
                }

                Console.Error.WriteLine(errorMessage.ToString());
            }

            currentLexer = oldLex;
            scopes = oldScopes;

            if (v != null)
            {
                return v;
            }

            return new ScriptVarLink(new ScriptVar(null), null);
        }

        public void AddObject(string[] ns, string objectName, ScriptVar val)
        {
            var baseVar = Root;

            if (ns != null)
            {
                int x = 0;
                for (; x < ns.Length; x++)
                {
                    var link = baseVar.FindChild(ns[x]);

                    if (link == null)
                    {
                        link = baseVar.AddChild(ns[x], new ScriptVar(null, ScriptVar.Flags.Object));
                    }

                    baseVar = link.Var;
                }
            }

            baseVar.AddChild(objectName, val);
        }

        public void AddMethod(string[] ns, string funcName, string[] args, ScriptCallbackCB callback, object userdata)
        {
            var fName = funcName;
            var baseVar = Root;

            if (ns != null)
            {
                int x = 0;
                for (; x < ns.Length; x++)
                {
                    var link = baseVar.FindChild(ns[x]);

                    if (link == null)
                    {
                        link = baseVar.AddChild(ns[x], new ScriptVar(null, ScriptVar.Flags.Object));
                    }

                    baseVar = link.Var;
                }
            }


            var funcVar = new ScriptVar(null, ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            funcVar.SetCallback(callback, userdata);

            //do we have any arguments to create?
            if (args != null)
            {
                foreach (string arg in args)
                {
                    funcVar.AddChildNoDup(arg, null);
                }
            }

            baseVar.AddChild(fName, funcVar);
        }

        public void AddMethod(string funcName, string[] args, ScriptCallbackCB callback, object userdata)
        {
            var funcVar = new ScriptVar(null, ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            funcVar.SetCallback(callback, userdata);

            //do we have any arguments to create?
            if (args != null)
            {
                foreach (string arg in args)
                {
                    funcVar.AddChildNoDup(arg, null);
                }
            }

            Root.AddChild(funcName, funcVar);
        }

        private ScriptVarLink FindInScopes(String name)
        {
            foreach (ScriptVar scriptVar in scopes)
            {
                ScriptVarLink a = scriptVar.FindChild(name);
                if (a != null)
                {
                    return a;
                }
            }

            return null;
        }

        private ScriptVarLink FindInParentClasses(ScriptVar obj, String name)
        {
            ScriptVarLink implementation;
            var parentClass = obj.FindChild(ScriptVar.PrototypeClassName);

            while (parentClass != null)
            {
                implementation = parentClass.Var.FindChild(name);
                if (implementation != null)
                {
                    return implementation;
                }
                parentClass = parentClass.Var.FindChild(ScriptVar.PrototypeClassName);
            }

            if (obj.IsString)
            {
                implementation = stringClass.FindChild(name);
                if (implementation != null)
                {
                    return implementation;
                }
            }

            if (obj.IsArray)
            {
                implementation = arrayClass.FindChild(name);
                if (implementation != null)
                {
                    return implementation;
                }
            }

            implementation = objectClass.FindChild(name);
            if (implementation != null)
            {
                return implementation;
            }

            return null;
        }

        private ScriptVarLink ParseFunctionDefinition()
        {
            currentLexer.Match(ScriptLex.LexTypes.RFunction);
            var funcName = String.Empty;

            //named function
            if (currentLexer.TokenType == ScriptLex.LexTypes.Id)
            {
                funcName = currentLexer.TokenString;
                currentLexer.Match(ScriptLex.LexTypes.Id);
            }

            var funcVar = new ScriptVarLink(new ScriptVar(null, ScriptVar.Flags.Function), funcName);
            ParseFunctionArguments(funcVar.Var);

            var funcBegin = currentLexer.TokenStart;
            bool execute = false;
            Block(ref execute);
            funcVar.Var.SetData(currentLexer.GetSubString(funcBegin));

            return funcVar;
        }

        private void ParseFunctionArguments(ScriptVar funcVar)
        {
            currentLexer.Match((ScriptLex.LexTypes)'(');
            while (currentLexer.TokenType != (ScriptLex.LexTypes)')')
            {
                funcVar.AddChildNoDup(currentLexer.TokenString, null);
                currentLexer.Match(ScriptLex.LexTypes.Id);

                if (currentLexer.TokenType != (ScriptLex.LexTypes)')')
                {
                    currentLexer.Match((ScriptLex.LexTypes)',');
                }
            }

            currentLexer.Match((ScriptLex.LexTypes)')');
        }
    }
}
