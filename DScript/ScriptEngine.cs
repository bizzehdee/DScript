﻿﻿/*
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
    public sealed partial class ScriptEngine : IDisposable
    {
        #region IDisposable
        private bool disposed;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposed) return;
            
            if (disposing)
            {
                stringClass.UnRef();
                arrayClass.UnRef();
                objectClass.UnRef();
                Root.UnRef();

                currentLexer?.Dispose();
            }

            // Indicate that the instance has been disposed.
            disposed = true;
        }
        #endregion

        private readonly ScriptVar stringClass;
        private readonly ScriptVar objectClass;
        private readonly ScriptVar arrayClass;
        private List<ScriptVar> scopes;
        private readonly Stack<ScriptVarLink> callStack;

        private ScriptLex currentLexer;

        public delegate void ScriptCallbackCB(ScriptVar var, object userdata);

        public ScriptVar Root { get; private set; }

        public ScriptEngine()
        {
            currentLexer = null;

            scopes = new List<ScriptVar>();
            callStack = new Stack<ScriptVarLink>();

            Root = (new ScriptVar(null, ScriptVar.Flags.Object)).Ref();

            objectClass = (new ScriptVar(null, ScriptVar.Flags.Object)).Ref();
            stringClass = (new ScriptVar(null, ScriptVar.Flags.Object)).Ref();
            arrayClass = (new ScriptVar(null, ScriptVar.Flags.Object)).Ref();

            Root.AddChild("Object", objectClass);
            Root.AddChild("String", stringClass);
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
            scopes = new List<ScriptVar>();
            scopes.PushBack(Root);

            var rootLink = new ScriptVarLink(Root, "root");

            callStack.Push(rootLink);

            using (currentLexer = new ScriptLex(code))
            {
                var execute = true;

                while (currentLexer.TokenType != 0)
                {
                    try
                    {
                        Statement(ref execute);
                    }
                    catch (Exception ex)
                    {
                        if (ex is ScriptException || ex is JITException)
                        {
                            var errorMessage = new StringBuilder($"ERROR on line {currentLexer.LineNumber} column {currentLexer.ColumnNumber} [{ex.Message}]");

                            Console.Error.WriteLine(errorMessage.ToString());

                            return;
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
            }

            callStack.Pop();

            currentLexer = oldLex;
            scopes = oldScopes;
        }

        public ScriptVarLink EvalComplex(string code)
        {
            var oldLex = currentLexer;
            var oldScopes = scopes;

            currentLexer = new ScriptLex(code);
            scopes = new List<ScriptVar>();
            scopes.PushBack(Root);

            ScriptVarLink v = null;

            try
            {
                var execute = true;
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
                var errorMessage = new StringBuilder($"ERROR on line {currentLexer.LineNumber} column {currentLexer.ColumnNumber} [{ex.Message}]");

                var i = 0;
                foreach (var scriptVar in scopes)
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

        public void AddNative(string funcDesc, ScriptCallbackCB callbackCB, object userData)
        {
            var oldLex = currentLexer;
            currentLexer = new ScriptLex(funcDesc);

            var baseVar = Root;
            currentLexer.Match(ScriptLex.LexTypes.RFunction);
            var funcName = currentLexer.TokenString;

            currentLexer.Match(ScriptLex.LexTypes.Id);

            while (currentLexer.TokenType == (ScriptLex.LexTypes)'.')
            {
                currentLexer.Match((ScriptLex.LexTypes)'.');

                var link = baseVar.FindChild(funcName);
                if (link == null)
                {
                    link = baseVar.AddChild(funcName, new ScriptVar(null, ScriptVar.Flags.Object));
                }
                baseVar = link.Var;
                funcName = currentLexer.TokenString;
                currentLexer.Match(ScriptLex.LexTypes.Id);
            }

            var funcVar = new ScriptVar(null, ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            funcVar.SetCallback(callbackCB, userData);
            ParseFunctionArguments(funcVar);

            currentLexer = oldLex;

            baseVar.AddChild(funcName, funcVar);
        }

        public void AddNativeProperty(string propertyDesc, ScriptCallbackCB callbackCB, object userData)
        {
            var oldLex = currentLexer;
            currentLexer = new ScriptLex(propertyDesc);

            var baseVar = Root;

            var propName = currentLexer.TokenString;

            currentLexer.Match(ScriptLex.LexTypes.Id);

            while (currentLexer.TokenType == (ScriptLex.LexTypes)'.')
            {
                currentLexer.Match((ScriptLex.LexTypes)'.');

                var link = baseVar.FindChild(propName);
                if (link == null)
                {
                    link = baseVar.AddChild(propName, new ScriptVar(null, ScriptVar.Flags.Object));
                }
                baseVar = link.Var;
                propName = currentLexer.TokenString;
                currentLexer.Match(ScriptLex.LexTypes.Id);
            }
            var propVar = new ScriptVar();
            callbackCB.Invoke(propVar, null);

            currentLexer = oldLex;

            baseVar.AddChild(propName, propVar);
        }

        private ScriptVarLink FindInScopes(string name)
        {
            for (var x = scopes.Count - 1; x >= 0; x--)
            {
                var scriptVar = scopes[x];
                var a = scriptVar.FindChild(name);
                if (a != null)
                {
                    return a;
                }
            }

            return null;
        }

        private ScriptVarLink FindInParentClasses(ScriptVar obj, string name)
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
            return ParseDefinition(ScriptLex.LexTypes.RFunction);
        }

        private ScriptVarLink ParseDefinition(ScriptLex.LexTypes lexType)
        {
            currentLexer.Match(lexType);
            var funcName = string.Empty;

            //named function
            if (currentLexer.TokenType == ScriptLex.LexTypes.Id)
            {
                funcName = currentLexer.TokenString;
                currentLexer.Match(ScriptLex.LexTypes.Id);
            }

            var funcVar = new ScriptVarLink(new ScriptVar(null, ScriptVar.Flags.Function), funcName);

            if (lexType == ScriptLex.LexTypes.RFunction || lexType == ScriptLex.LexTypes.RCatch)
            {
                ParseFunctionArguments(funcVar.Var);
            }

            var funcBegin = currentLexer.TokenStart;
            var noExecute = false;
            Block(ref noExecute);
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

        private static void CreateLink(ref ScriptVarLink link, ScriptVar res)
        {
            if(link == null || link.Owned)
            {
                link = new ScriptVarLink(res, null);
            }
            else
            {
                link.ReplaceWith(res);
            }
        }
        
        /// <summary>
        /// Serialize the current VM state (all variables and their values).
        /// This can be called at any time, even after script execution completes.
        /// Note: Native functions cannot be serialized - they must be re-registered after deserialization.
        /// </summary>
        /// <returns>A VMState object containing the serialized state</returns>
        public VMState SerializeState()
        {
            using (var ms = new System.IO.MemoryStream())
            using (var writer = new System.IO.BinaryWriter(ms))
            {
                Root.Serialize(writer);
                writer.Flush();

                // Collect all native function names for reference
                var nativeFunctions = new List<string>();
                CollectNativeFunctionNames(Root, "", nativeFunctions);

                return new VMState
                {
                    RootState = ms.ToArray(),
                    NativeFunctionNames = nativeFunctions,  // Already a List, which implements IReadOnlyList
                    Timestamp = DateTime.UtcNow,
                    Version = "1.0"
                };
            }
        }

        /// <summary>
        /// Deserialize and restore a previously saved VM state.
        /// This replaces all current variables with the saved state.
        /// Note: All native functions must be re-registered before calling this method.
        /// After deserialization, you can execute new scripts that will have access to the restored variables.
        /// </summary>
        /// <param name="state">The VMState to restore</param>
        public void DeserializeState(VMState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (currentLexer != null)
            {
                throw new InvalidOperationException("Cannot deserialize state while execution is active.");
            }

            // Deserialize the Root state
            using var ms = new System.IO.MemoryStream(state.RootState);
            using var reader = new System.IO.BinaryReader(ms);
            var deserializedRoot = ScriptVar.Deserialize(reader);

            // Merge native functions from current Root to deserialized Root
            MergeNativeFunctions(Root, deserializedRoot);

            // Replace Root contents while keeping the same instance (to maintain refs)
            Root.RemoveAllChildren();
            var link = deserializedRoot.FirstChild;
            while (link != null)
            {
                Root.AddChild(link.Name, link.Var, link.IsConst);
                link = link.Next;
            }
        }

        /// <summary>
        /// Recursively collect names of all native functions
        /// </summary>
        private static void CollectNativeFunctionNames(ScriptVar var, string path, List<string> names)
        {
            var link = var.FirstChild;
            while (link != null)
            {
                var fullPath = string.IsNullOrEmpty(path) ? link.Name : $"{path}.{link.Name}";
                
                if (link.Var.IsNative && link.Var.IsFunction)
                {
                    names.Add(fullPath);
                }
                
                if (link.Var.IsObject || link.Var.IsFunction)
                {
                    CollectNativeFunctionNames(link.Var, fullPath, names);
                }
                
                link = link.Next;
            }
        }

        /// <summary>
        /// Merge native functions from current root to deserialized root
        /// </summary>
        private static void MergeNativeFunctions(ScriptVar current, ScriptVar deserialized)
        {
            var link = current.FirstChild;
            while (link != null)
            {
                if (link.Var.IsNative && link.Var.IsFunction)
                {
                    // Find corresponding link in deserialized
                    var deserializedLink = deserialized.FindChild(link.Name);
                    if (deserializedLink != null && deserializedLink.Var.IsNative)
                    {
                        // Copy the callback from current to deserialized
                        deserializedLink.Var.SetCallback(link.Var.GetCallback(), link.Var.GetCallbackUserData());
                    }
                }
                else if (link.Var.IsObject)
                {
                    // Recursively merge nested objects
                    var deserializedLink = deserialized.FindChild(link.Name);
                    if (deserializedLink != null && deserializedLink.Var.IsObject)
                    {
                        MergeNativeFunctions(link.Var, deserializedLink.Var);
                    }
                }
                
                link = link.Next;
            }
        }
    }
}
