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
using DScript.Compiler;
using DScript.Debugger;
using DScript.Vm;
using VmEnvironment = DScript.Vm.Environment;

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
            }

            disposed = true;
        }
        #endregion

        private readonly ScriptVar stringClass;
        private readonly ScriptVar objectClass;
        private readonly ScriptVar arrayClass;

        // The global (outermost) lexical scope; its bindings live on Root.
        private readonly VmEnvironment globalEnvironment;

        // --- debugger -------------------------------------------------------
        private IDebugger _debugger;
        private DebugAction _debugInitialAction = DebugAction.StepIn;
        private readonly HashSet<(string Source, int Line)> _breakpoints = [];

        public delegate void ScriptCallbackCB(ScriptVar var, object userdata);

        public ScriptVar Root { get; private set; }

        public ScriptEngine()
        {
            Root = (new ScriptVar(ScriptVar.Flags.Object)).Ref();

            objectClass = (new ScriptVar(ScriptVar.Flags.Object)).Ref();
            stringClass = (new ScriptVar(ScriptVar.Flags.Object)).Ref();
            arrayClass = (new ScriptVar(ScriptVar.Flags.Object)).Ref();

            Root.AddChild("Object", objectClass);
            Root.AddChild("String", stringClass);
            Root.AddChild("Array", arrayClass);

            globalEnvironment = new VmEnvironment(Root, null);
        }

        public void Trace()
        {
            Root.Trace(0, null);
        }

        /// <summary>
        /// Compile <paramref name="code"/> to bytecode and run it. Script and
        /// runtime errors are reported to stderr (matching prior behaviour);
        /// other exceptions propagate.
        /// </summary>
        public void Execute(string code)
        {
            try
            {
                Run(Compile(code));
            }
            catch (Exception ex) when (ex is ScriptException || ex is JITException)
            {
                Console.Error.WriteLine(ex.ToString());
            }
        }

        /// <summary>
        /// Evaluate <paramref name="code"/> as an expression and return its value
        /// (used by eval / JSON.parse).
        /// </summary>
        public ScriptVarLink EvalComplex(string code)
        {
            try
            {
                Chunk chunk;
                using (var compiler = new DScriptCompiler())
                {
                    chunk = compiler.CompileExpression(code);
                }

                var value = new VirtualMachine(this).Run(chunk, globalEnvironment);
                return new ScriptVarLink(value, null);
            }
            catch (Exception ex) when (ex is ScriptException || ex is JITException)
            {
                Console.Error.WriteLine($"ERROR [{ex.Message}]");
                return new ScriptVarLink(new ScriptVar(null), null);
            }
        }

        /// <summary>
        /// Compile source to a bytecode program <see cref="Chunk"/>. The chunk can
        /// be run with <see cref="Run(Chunk)"/>, or saved with
        /// <see cref="BytecodeSerializer"/> and re-run later.
        /// </summary>
        public static Chunk Compile(string code)
        {
            using var compiler = new DScriptCompiler();
            return compiler.CompileProgram(code);
        }

        /// <summary>
        /// Run a previously compiled (or loaded) bytecode program. Script/runtime
        /// errors propagate; use <see cref="Execute(string)"/> for the
        /// compile-and-report-errors convenience path.
        /// </summary>
        public void Run(Chunk program)
        {
            var vm = new VirtualMachine(this);
            if (_debugger != null)
            {
                vm.AttachDebugger(_debugger, _debugInitialAction);
                foreach (var (src, line) in _breakpoints)
                    vm.AddBreakpoint(src, line);
            }
            vm.Run(program, globalEnvironment);
        }

        /// <summary>
        /// Attach a debugger that will receive step and breakpoint events during
        /// subsequent <see cref="Run"/> calls. Pass <see cref="DebugAction.StepIn"/>
        /// as <paramref name="initialAction"/> to pause at the very first source line;
        /// use <see cref="DebugAction.Continue"/> to run freely until a breakpoint.
        /// </summary>
        public void AttachDebugger(IDebugger debugger, DebugAction initialAction = DebugAction.StepIn)
        {
            _debugger = debugger;
            _debugInitialAction = initialAction;
        }

        /// <summary>Detach the current debugger. Subsequent runs execute at full speed.</summary>
        public void DetachDebugger() => _debugger = null;

        /// <summary>Register a source-line breakpoint (triggers when <see cref="DebugAction.Continue"/> is active).</summary>
        public void AddBreakpoint(string source, int line) => _breakpoints.Add((source, line));

        /// <summary>Remove a previously registered breakpoint.</summary>
        public void RemoveBreakpoint(string source, int line) => _breakpoints.Remove((source, line));

        /// <summary>Remove all registered breakpoints.</summary>
        public void ClearBreakpoints() => _breakpoints.Clear();

        /// <summary>
        /// Invoke a script (or native) function programmatically. Lets native and
        /// host code call back into script — e.g. Array map/filter/forEach/reduce
        /// callbacks and sort comparators.
        /// </summary>
        public ScriptVar CallFunction(ScriptVar function, ScriptVar thisArg, params ScriptVar[] args)
        {
            return new VirtualMachine(this).InvokeCallable(function, thisArg, args ?? []);
        }

        public void AddNative(string funcDesc, ScriptCallbackCB callbackCB, object userData)
        {
            using var lexer = new ScriptLex(funcDesc);

            var baseVar = Root;
            lexer.Match(ScriptLex.LexTypes.RFunction);
            var funcName = lexer.TokenString;

            lexer.Match(ScriptLex.LexTypes.Id);

            while (lexer.TokenType == (ScriptLex.LexTypes)'.')
            {
                lexer.Match((ScriptLex.LexTypes)'.');

                var link = baseVar.FindChild(funcName);
                if (link == null)
                {
                    link = baseVar.AddChild(funcName, new ScriptVar(ScriptVar.Flags.Object));
                }
                baseVar = link.Var;
                funcName = lexer.TokenString;
                lexer.Match(ScriptLex.LexTypes.Id);
            }

            var funcVar = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            funcVar.SetCallback(callbackCB, userData);
            ParseFunctionArguments(funcVar, lexer);

            baseVar.AddChild(funcName, funcVar);
        }

        public void AddNativeProperty(string propertyDesc, ScriptCallbackCB callbackCB, object userData)
        {
            using var lexer = new ScriptLex(propertyDesc);

            var baseVar = Root;
            var propName = lexer.TokenString;

            lexer.Match(ScriptLex.LexTypes.Id);

            while (lexer.TokenType == (ScriptLex.LexTypes)'.')
            {
                lexer.Match((ScriptLex.LexTypes)'.');

                var link = baseVar.FindChild(propName);
                if (link == null)
                {
                    link = baseVar.AddChild(propName, new ScriptVar(ScriptVar.Flags.Object));
                }
                baseVar = link.Var;
                propName = lexer.TokenString;
                lexer.Match(ScriptLex.LexTypes.Id);
            }

            var propVar = new ScriptVar();
            callbackCB.Invoke(propVar, null);

            baseVar.AddChild(propName, propVar);
        }

        private static void ParseFunctionArguments(ScriptVar funcVar, ScriptLex lexer)
        {
            lexer.Match((ScriptLex.LexTypes)'(');
            while (lexer.TokenType != (ScriptLex.LexTypes)')')
            {
                funcVar.AddChildNoDup(lexer.TokenString, null);
                lexer.Match(ScriptLex.LexTypes.Id);

                if (lexer.TokenType != (ScriptLex.LexTypes)')')
                {
                    lexer.Match((ScriptLex.LexTypes)',');
                }
            }

            lexer.Match((ScriptLex.LexTypes)')');
        }

        /// <summary>
        /// Resolve a member through the prototype chain and the built-in
        /// String/Array/Object classes. Used by the VM for member access.
        /// </summary>
        internal ScriptVarLink FindInParentClasses(ScriptVar obj, string name)
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

        /// <summary>
        /// Serialize the current variable state (Root and all values). Native
        /// functions cannot be serialized and must be re-registered after restore.
        /// </summary>
        public VMState SerializeState()
        {
            using var ms = new System.IO.MemoryStream();
            using var writer = new System.IO.BinaryWriter(ms);
            Root.Serialize(writer);
            writer.Flush();

            var nativeFunctions = new List<string>();
            CollectNativeFunctionNames(Root, "", nativeFunctions);

            return new VMState
            {
                RootState = ms.ToArray(),
                NativeFunctionNames = nativeFunctions,
                Timestamp = DateTime.UtcNow,
                Version = "1.0"
            };
        }

        /// <summary>
        /// Restore a previously saved variable state. Native functions must be
        /// re-registered before calling this; their callbacks are merged back in.
        /// </summary>
        public void DeserializeState(VMState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            using var ms = new System.IO.MemoryStream(state.RootState);
            using var reader = new System.IO.BinaryReader(ms);
            var deserializedRoot = ScriptVar.Deserialize(reader);

            MergeNativeFunctions(Root, deserializedRoot);

            Root.RemoveAllChildren();
            var link = deserializedRoot.FirstChild;
            while (link != null)
            {
                Root.AddChild(link.Name, link.Var, link.IsConst);
                link = link.Next;
            }
        }

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

        private static void MergeNativeFunctions(ScriptVar current, ScriptVar deserialized)
        {
            var link = current.FirstChild;
            while (link != null)
            {
                if (link.Var.IsNative && link.Var.IsFunction)
                {
                    var deserializedLink = deserialized.FindChild(link.Name);
                    if (deserializedLink != null && deserializedLink.Var.IsNative)
                    {
                        deserializedLink.Var.SetCallback(link.Var.GetCallback(), link.Var.GetCallbackUserData());
                    }
                }
                else if (link.Var.IsObject)
                {
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
