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
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DScript.Compiler;
using DScript.Debugger;
using DScript.Profiler;
using DScript.Registrars;
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
                functionClass.UnRef();
                Root.UnRef();
            }

            disposed = true;
        }
        #endregion

        private readonly ScriptVar stringClass;
        private readonly ScriptVar objectClass;
        private readonly ScriptVar arrayClass;
        internal readonly ScriptVar functionClass;

        // The global (outermost) lexical scope; its bindings live on Root.
        private readonly VmEnvironment globalEnvironment;

        // --- module system --------------------------------------------------

        /// <summary>
        /// Called by require() to resolve a module. Given (importPath, currentModulePath),
        /// return the module source, or null if not found.
        /// </summary>
        public Func<string, string, IReadOnlyDictionary<string, string>, string> ModuleLoader { get; set; }

        /// <summary>Path of the module currently being executed (used for relative imports).</summary>
        public string CurrentModulePath { get; set; } = string.Empty;

        /// <summary>
        /// When true (the default), the bytecode optimiser runs during compilation
        /// (constant folding, dead-code elimination, super-instruction fusion, narrow
        /// encoding). Set false to compile straight, unoptimised bytecode — honoured
        /// by <see cref="Execute"/>, <see cref="EvalComplex"/>, and module compilation.
        /// </summary>
        public bool EnableOptimizer { get; set; } = true;

        /// <summary>
        /// Maximum depth of nested script function calls before a catchable
        /// <c>"Maximum call stack size exceeded"</c> error is thrown (the JS engine's
        /// RangeError analogue). This bounds recursion short of the point where the
        /// native .NET stack would overflow — an uncatchable condition that crashes
        /// the process. Top-level execution runs on a large (256&nbsp;MB) stack, so the
        /// default (10000) sits well below its real ceiling — with room for the error
        /// to unwind from that depth — while matching typical engine limits.
        /// </summary>
        public int MaxCallStackDepth { get; set; } = 10000;

        private readonly Dictionary<string, ScriptVar> _moduleCache = new();

        // --- host event system -----------------------------------------------

        /// <summary>
        /// Set by <c>DScript.Extras.EngineFunctionLoader</c> when the host event
        /// system is wired up. Invoke by calling <see cref="RaiseEvent"/>.
        /// </summary>
        public Action<string, ScriptVar[]> HostEventDispatch { get; set; }

        /// <summary>
        /// Fire a named event, invoking any script-side handlers registered with
        /// the global <c>on()</c> / <c>once()</c> functions.
        /// If <see cref="HostEventDispatch"/> is <c>null</c> (i.e.
        /// <c>EngineFunctionLoader.RegisterFunctions</c> was not called) this is a no-op.
        /// </summary>
        /// <param name="name">Event name, e.g. <c>"playerDied"</c>.</param>
        /// <param name="args">Arguments forwarded to every matching handler.</param>
        public void RaiseEvent(string name, params ScriptVar[] args)
        {
            HostEventDispatch?.Invoke(name, args);
            DrainMicroTasks();
        }

        // --- resource limits ------------------------------------------------
        // 0 means no limit; positive values are enforced by the VM.
        private long _instructionLimit;
        private TimeSpan _executionTimeout;

        /// <summary>
        /// Cancel execution after this many VM instructions. Pass 0 to disable.
        /// The limit is checked on every instruction; exceeding it throws
        /// <see cref="ScriptTimeoutException"/> in the host (not catchable by script).
        /// </summary>
        public void SetInstructionLimit(long limit) => _instructionLimit = limit;

        /// <summary>
        /// Cancel execution after this wall-clock duration elapses. Pass
        /// <see cref="TimeSpan.Zero"/> to disable.
        /// The deadline is sampled every 1 024 instructions; exceeding it throws
        /// <see cref="ScriptTimeoutException"/> in the host (not catchable by script).
        /// </summary>
        public void SetTimeout(TimeSpan timeout) => _executionTimeout = timeout;

        internal long InstructionLimit => _instructionLimit;
        internal TimeSpan ExecutionTimeout => _executionTimeout;

        // --- debugger -------------------------------------------------------
        private IDebugger _debugger;
        private DebugAction _debugInitialAction = DebugAction.StepIn;
        private readonly HashSet<(string Source, int Line)> _breakpoints = [];

        // --- profiler -------------------------------------------------------
        private IProfiler _profiler;

        public delegate void ScriptCallbackCB(ScriptVar var, object userdata);

        public ScriptVar Root { get; private set; }

        public ScriptEngine()
        {
            Root = (ScriptVar.CreateObject()).Ref();

            objectClass = (ScriptVar.CreateObject()).Ref();
            stringClass = (ScriptVar.CreateObject()).Ref();
            arrayClass = (ScriptVar.CreateObject()).Ref();
            functionClass = (ScriptVar.CreateObject()).Ref();

            Root.AddChild("Object", objectClass);
            Root.AddChild("String", stringClass);
            Root.AddChild("Array", arrayClass);
            Root.AddChild("Function", functionClass);

            // Global numeric constants (typeof is "number"). In JS these are
            // non-writable/non-configurable; DScript exposes them as ordinary
            // values, which is sufficient for read access and coercion.
            Root.AddChild("NaN", ScriptVar.FromDouble(double.NaN));
            Root.AddChild("Infinity", ScriptVar.FromDouble(double.PositiveInfinity));

            RegisterPromiseBuiltin();
            RegisterRequireBuiltin();
            SymbolRegistrar.Register(this);
            BigIntRegistrar.Register(this);
            ReflectRegistrar.Register(this);
            ProxyRegistrar.Register(this);
            IntlRegistrar.Register(this);

            globalEnvironment = new VmEnvironment(Root, null);
        }

        private void RegisterRequireBuiltin()
        {
            var requireVar = ScriptVar.CreateNativeFunction();
            requireVar.AddChild("path", ScriptVar.CreateUndefined());
            requireVar.AddChild("attrs", ScriptVar.CreateUndefined());
            requireVar.SetCallback((scope, _) =>
            {
                var pathVar = scope.FindChild("path")?.Var;
                var path = pathVar?.String ?? string.Empty;

                // Check cache first — also handles circular requires (pre-seeded below).
                if (_moduleCache.TryGetValue(path, out var cached))
                {
                    scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(cached);
                    return;
                }

                // Build the import-attributes dictionary from the optional second argument.
                IReadOnlyDictionary<string, string> importAttrs = new Dictionary<string, string>();
                var attrsVar = scope.FindChild("attrs")?.Var;
                if (attrsVar != null && !attrsVar.IsUndefined)
                {
                    var dict = new Dictionary<string, string>();
                    var attrLink = attrsVar.FirstChild;
                    while (attrLink != null)
                    {
                        if (attrLink.Name != ScriptVar.PrototypeClassName)
                            dict[attrLink.Name] = attrLink.Var?.String ?? "";
                        attrLink = attrLink.Next;
                    }
                    importAttrs = dict;
                }

                // Load source via the user-supplied loader.
                string source = null;
                if (ModuleLoader != null)
                    source = ModuleLoader(path, CurrentModulePath, importAttrs);

                if (source == null)
                    throw new ScriptException($"Cannot find module '{path}'");

                // Pre-seed the cache BEFORE running the module to break circular require cycles.
                var exportsObj = ScriptVar.CreateObject();
                _moduleCache[path] = exportsObj;

                // Build an isolated module environment:
                //   - a fresh root object containing __exports__ and copies of all globals
                var moduleRoot = ScriptVar.CreateObject();
                moduleRoot.AddChild("__exports__", exportsObj);

                // module object: { exports, filename, loaded }
                var moduleObj = ScriptVar.CreateObject();
                moduleObj.AddChild("exports", exportsObj);
                moduleObj.AddChild("filename", ScriptVar.FromString(path));
                moduleObj.AddChild("loaded", ScriptVar.FromInt(0));
                moduleRoot.AddChild("module", moduleObj);

                // exports shorthand (alias for __exports__ / module.exports)
                moduleRoot.AddChild("exports", exportsObj);

                // __filename and __dirname
                moduleRoot.AddChild("__filename", ScriptVar.FromString(path));
                var dirName = GetDirName(path);
                moduleRoot.AddChild("__dirname", ScriptVar.FromString(dirName));

                // Copy globals (require, Promise, String, …) into the module root.
                var link = Root.FirstChild;
                while (link != null)
                {
                    moduleRoot.AddChildNoDup(link.Name, link.Var);
                    link = link.Next;
                }

                var moduleEnv = new Vm.Environment(moduleRoot, null);

                // Compile and run the module.
                var savedPath = CurrentModulePath;
                CurrentModulePath = path;
                try
                {
                    using var compiler = new Compiler.DScriptCompiler { EnableOptimizer = EnableOptimizer };
                    var chunk = compiler.CompileProgram(source);
                    var vm = new Vm.VirtualMachine(this);
                    vm.Run(chunk, moduleEnv);
                }
                finally
                {
                    CurrentModulePath = savedPath;
                }

                // Mark module as loaded.
                var loadedLink = moduleObj.FindChild("loaded");
                if (loadedLink != null) loadedLink.ReplaceWith(ScriptVar.FromBool(true));

                // Retrieve exports — prefer module.exports over __exports__ (script may
                // have replaced module.exports with a different object, e.g. module.exports = fn)
                var moduleExports = moduleObj.FindChild("exports")?.Var;
                var exports = moduleExports ?? moduleRoot.FindChild("__exports__")?.Var ?? exportsObj;

                // Update the cache entry in case it was replaced, and return.
                _moduleCache[path] = exports;
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(exports);
            }, null);

            Root.AddChild("require", requireVar);
        }

        private static string GetDirName(string path)
        {
            if (string.IsNullOrEmpty(path)) return ".";
            var sep = path.LastIndexOfAny(['/', '\\']);
            return sep > 0 ? path[..sep] : ".";
        }

        private static ScriptVar MakeAggregateError(ScriptVar[] reasons, int len)
        {
            var errorsArr = ScriptVar.CreateUndefined(); errorsArr.SetArray();
            for (var j = 0; j < len; j++)
                errorsArr.SetArrayIndex(j, reasons[j] ?? ScriptVar.CreateUndefined());
            var err = ScriptVar.CreateObject();
            err.AddChild("name", ScriptVar.FromString("AggregateError"));
            err.AddChild("message", ScriptVar.FromString("All promises were rejected"));
            err.AddChild("errors", errorsArr);
            return err;
        }

        private void RegisterPromiseBuiltin()
        {
            // Promise constructor: new Promise(function(resolve, reject) { ... })
            var promiseCtorVar = ScriptVar.CreateNativeFunction();
            promiseCtorVar.AddChild("executor", ScriptVar.CreateUndefined());
            promiseCtorVar.SetCallback((scope, _) =>
            {
                var executor = scope.FindChild("executor")?.Var;
                var promiseObj = new Vm.PromiseObject();
                var vm = new VirtualMachine(this);

                // resolve callback
                var resolveFn = ScriptVar.CreateNativeFunction();
                resolveFn.AddChild("value", ScriptVar.CreateUndefined());
                resolveFn.SetCallback((s, __) =>
                {
                    var v = s.FindChild("value")?.Var ?? ScriptVar.CreateUndefined();
                    promiseObj.Resolve(v);
                }, null);

                // reject callback
                var rejectFn = ScriptVar.CreateNativeFunction();
                rejectFn.AddChild("reason", ScriptVar.CreateUndefined());
                rejectFn.SetCallback((s, __) =>
                {
                    var r = s.FindChild("reason")?.Var ?? ScriptVar.CreateUndefined();
                    promiseObj.Reject(r);
                }, null);

                if (executor != null && executor.IsFunction)
                {
                    try { vm.InvokeCallable(executor, null, [resolveFn, rejectFn]); }
                    catch (Exception ex) { promiseObj.Reject(ScriptVar.FromString(ex.Message)); }
                }

                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(promiseObj.ToScriptVar(vm));
            }, null);

            // Promise.resolve(value) static method
            var promiseClass = ScriptVar.CreateObject();
            promiseClass.SetData(promiseCtorVar); // store ctor reference

            // Copy the constructor callback to the class var so `new Promise(fn)` works
            // We make promiseClass a function so the VM's `new` opcode can call it
            var promiseVar = ScriptVar.CreateNativeFunction();
            promiseVar.AddChild("executor", ScriptVar.CreateUndefined());
            promiseVar.SetCallback(promiseCtorVar.GetCallback(), null);

            // Promise.resolve
            var resolveFnStatic = ScriptVar.CreateNativeFunction();
            resolveFnStatic.AddChild("value", ScriptVar.CreateUndefined());
            resolveFnStatic.SetCallback((scope, _) =>
            {
                var v = scope.FindChild("value")?.Var ?? ScriptVar.CreateUndefined();
                var vm = new VirtualMachine(this);
                var p = Vm.PromiseObject.Resolved(v);
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(p.ToScriptVar(vm));
            }, null);
            promiseVar.AddChild("resolve", resolveFnStatic);

            // Promise.reject
            var rejectFnStatic = ScriptVar.CreateNativeFunction();
            rejectFnStatic.AddChild("reason", ScriptVar.CreateUndefined());
            rejectFnStatic.SetCallback((scope, _) =>
            {
                var r = scope.FindChild("reason")?.Var ?? ScriptVar.CreateUndefined();
                var vm = new VirtualMachine(this);
                var p = Vm.PromiseObject.Rejected(r);
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(p.ToScriptVar(vm));
            }, null);
            promiseVar.AddChild("reject", rejectFnStatic);

            // Promise.all(arr)
            var allFn = ScriptVar.CreateNativeFunction();
            allFn.AddChild("arr", ScriptVar.CreateUndefined());
            allFn.SetCallback((scope, _) =>
            {
                var arr = scope.FindChild("arr")?.Var ?? ScriptVar.CreateUndefined();
                var vm2 = new VirtualMachine(this);
                var len = arr.IsArray ? arr.GetArrayLength() : 0;
                if (len == 0)
                {
                    var emptyArr = ScriptVar.CreateUndefined(); emptyArr.SetArray();
                    scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(
                        Vm.PromiseObject.Resolved(emptyArr).ToScriptVar(vm2));
                    return;
                }
                var result = new Vm.PromiseObject();
                var values = new ScriptVar[len];
                var remaining = new[] { len };
                for (var i = 0; i < len; i++)
                {
                    var idx = i;
                    var p = Vm.PromiseObject.Wrap(arr.GetArrayIndex(i));
                    if (p.Status == Vm.PromiseObject.PromiseState.Rejected)
                    { result.Reject(p.Reason); break; }
                    p.Then(v => { values[idx] = v; remaining[0]--; if (remaining[0] == 0) { var r = ScriptVar.CreateUndefined(); r.SetArray(); for (var j = 0; j < len; j++) r.SetArrayIndex(j, values[j] ?? ScriptVar.CreateUndefined()); result.Resolve(r); } },
                           reason => result.Reject(reason));
                }
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(result.ToScriptVar(vm2));
            }, null);
            promiseVar.AddChild("all", allFn);

            // Promise.allSettled(arr)
            var allSettledFn = ScriptVar.CreateNativeFunction();
            allSettledFn.AddChild("arr", ScriptVar.CreateUndefined());
            allSettledFn.SetCallback((scope, _) =>
            {
                var arr = scope.FindChild("arr")?.Var ?? ScriptVar.CreateUndefined();
                var vm2 = new VirtualMachine(this);
                var len = arr.IsArray ? arr.GetArrayLength() : 0;
                var result = new Vm.PromiseObject();
                var results = new ScriptVar[len];
                var remaining = new[] { len == 0 ? -1 : len };
                if (len == 0) { var emptyArr = ScriptVar.CreateUndefined(); emptyArr.SetArray(); result.Resolve(emptyArr); }
                for (var i = 0; i < len; i++)
                {
                    var idx = i;
                    var p = Vm.PromiseObject.Wrap(arr.GetArrayIndex(i));
                    Action<ScriptVar, bool> settle = (v, fulfilled) =>
                    {
                        var entry = ScriptVar.CreateObject();
                        entry.AddChild("status", ScriptVar.FromString(fulfilled ? "fulfilled" : "rejected"));
                        if (fulfilled) entry.AddChild("value", v); else entry.AddChild("reason", v);
                        results[idx] = entry;
                        remaining[0]--;
                        if (remaining[0] == 0) { var r = ScriptVar.CreateUndefined(); r.SetArray(); for (var j = 0; j < len; j++) r.SetArrayIndex(j, results[j]); result.Resolve(r); }
                    };
                    p.Then(v => settle(v, true), r => settle(r, false));
                }
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(result.ToScriptVar(vm2));
            }, null);
            promiseVar.AddChild("allSettled", allSettledFn);

            // Promise.race(arr)
            var raceFn = ScriptVar.CreateNativeFunction();
            raceFn.AddChild("arr", ScriptVar.CreateUndefined());
            raceFn.SetCallback((scope, _) =>
            {
                var arr = scope.FindChild("arr")?.Var ?? ScriptVar.CreateUndefined();
                var vm2 = new VirtualMachine(this);
                var len = arr.IsArray ? arr.GetArrayLength() : 0;
                var result = new Vm.PromiseObject();
                for (var i = 0; i < len; i++)
                {
                    var p = Vm.PromiseObject.Wrap(arr.GetArrayIndex(i));
                    p.Then(v => result.Resolve(v), r => result.Reject(r));
                }
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(result.ToScriptVar(vm2));
            }, null);
            promiseVar.AddChild("race", raceFn);

            // Promise.any(arr)
            var anyFn = ScriptVar.CreateNativeFunction();
            anyFn.AddChild("arr", ScriptVar.CreateUndefined());
            anyFn.SetCallback((scope, _) =>
            {
                var arr = scope.FindChild("arr")?.Var ?? ScriptVar.CreateUndefined();
                var vm2 = new VirtualMachine(this);
                var len = arr.IsArray ? arr.GetArrayLength() : 0;
                var result = new Vm.PromiseObject();
                var rejectedCount = 0;
                var reasons = new ScriptVar[len];
                if (len == 0) { result.Reject(MakeAggregateError(reasons, 0)); }
                for (var i = 0; i < len; i++)
                {
                    var idx = i;
                    var p = Vm.PromiseObject.Wrap(arr.GetArrayIndex(i));
                    p.Then(v => result.Resolve(v), r =>
                    {
                        reasons[idx] = r;
                        rejectedCount++;
                        if (rejectedCount == len)
                            result.Reject(MakeAggregateError(reasons, len));
                    });
                }
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(result.ToScriptVar(vm2));
            }, null);
            promiseVar.AddChild("any", anyFn);

            // Promise.try(fn, ...args) — ES2025
            var tryFn = ScriptVar.CreateNativeFunction();
            tryFn.AddChild("fn", ScriptVar.CreateUndefined());
            tryFn.SetCallback((scope, _) =>
            {
                var fn = scope.FindChild("fn")?.Var ?? ScriptVar.CreateUndefined();
                var vm2 = new VirtualMachine(this);
                Vm.PromiseObject result;
                try
                {
                    var retVal = fn.IsFunction
                        ? CallFunction(fn, null)
                        : ScriptVar.CreateUndefined();
                    result = Vm.PromiseObject.Wrap(retVal);
                }
                catch (Exception ex)
                {
                    result = Vm.PromiseObject.Rejected(ScriptVar.FromString(ex.Message));
                }
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(result.ToScriptVar(vm2));
            }, null);
            promiseVar.AddChild("try", tryFn);

            // Promise.withResolvers()
            var withResolversFn = ScriptVar.CreateNativeFunction();
            withResolversFn.SetCallback((scope, _) =>
            {
                var vm2 = new VirtualMachine(this);
                var promiseObj = new Vm.PromiseObject();

                var resolveFn = ScriptVar.CreateNativeFunction();
                resolveFn.AddChild("value", ScriptVar.CreateUndefined());
                resolveFn.SetCallback((s, __) =>
                {
                    var v = s.FindChild("value")?.Var ?? ScriptVar.CreateUndefined();
                    promiseObj.Resolve(v);
                }, null);

                var rejectFn = ScriptVar.CreateNativeFunction();
                rejectFn.AddChild("reason", ScriptVar.CreateUndefined());
                rejectFn.SetCallback((s, __) =>
                {
                    var r = s.FindChild("reason")?.Var ?? ScriptVar.CreateUndefined();
                    promiseObj.Reject(r);
                }, null);

                var result = ScriptVar.CreateObject();
                result.AddChild("promise", promiseObj.ToScriptVar(vm2));
                result.AddChild("resolve", resolveFn);
                result.AddChild("reject", rejectFn);
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(result);
            }, null);
            promiseVar.AddChild("withResolvers", withResolversFn);

            Root.AddChild("Promise", promiseVar);
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
                using var compiler = new DScriptCompiler { EnableOptimizer = EnableOptimizer };
                Run(compiler.CompileProgram(code));
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
                using (var compiler = new DScriptCompiler { EnableOptimizer = EnableOptimizer })
                {
                    chunk = compiler.CompileExpression(code);
                }

                var value = new VirtualMachine(this).Run(chunk, globalEnvironment);
                return new ScriptVarLink(value, null);
            }
            catch (Exception ex) when (ex is ScriptException || ex is JITException)
            {
                Console.Error.WriteLine($"ERROR [{ex.Message}]");
                return new ScriptVarLink(ScriptVar.FromString(null), null);
            }
        }

        /// <summary>
        /// Lever A: when true, the compiler rewrites fully-slottable <c>let</c>/<c>var</c>
        /// locals to positional GetLocal/SetLocal (see <see cref="Chunk.PromoteLocalSlots"/>).
        /// Enabled only on the AOT/closure build — the closure JIT and interpreter read
        /// slots, while the Reflection.Emit back-end declines slotted chunks. Off by
        /// default so the default build's bytecode and behaviour are unchanged.
        /// </summary>
        public static bool EnableLocalSlots { get; set; }

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
            if (_profiler != null)
                vm.AttachProfiler(_profiler);
            vm.Run(program, globalEnvironment);
        }

        // --- host object injection ------------------------------------------

        /// <summary>
        /// Exposes a C# object as a named global. Public instance members decorated
        /// with <see cref="ScriptVisibleAttribute"/> are accessible from script:
        /// methods become callable functions; properties become readable (and, if
        /// also decorated with <see cref="ScriptWritableAttribute"/>, writable).
        /// Supported C# types: <c>int</c>, <c>double</c>, <c>bool</c>, <c>string</c>,
        /// <c>void</c>, and <c>ScriptVar</c> (passed through directly).
        /// </summary>
        [RequiresUnreferencedCode("Uses reflection to discover ScriptVisible members on the host object.")]
        public void SetGlobal(string name, object obj)
        {
            Root.AddChildNoDup(name, BuildHostObjectWrapper(obj));
        }

        [RequiresUnreferencedCode("Uses reflection.")]
        private static ScriptVar BuildHostObjectWrapper(object obj)
        {
            var wrapper = ScriptVar.CreateObject();
            if (obj == null) return wrapper;

            var type = obj.GetType();

            // Methods
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!Attribute.IsDefined(method, typeof(ScriptVisibleAttribute))) continue;
                var captured = method;
                var capturedObj = obj;
                var parameters = captured.GetParameters();

                var fnVar = ScriptVar.CreateNativeFunction();
                // Register declared parameters so the VM can resolve them by name.
                foreach (var p in parameters)
                    fnVar.AddChild(p.Name ?? $"arg{p.Position}", ScriptVar.CreateUndefined());

                fnVar.SetCallback((scope, _) =>
                {
                    var args = new object[parameters.Length];
                    for (var i = 0; i < parameters.Length; i++)
                    {
                        var pName = parameters[i].Name ?? $"arg{i}";
                        var sv = scope.FindChild(pName)?.Var ?? ScriptVar.CreateUndefined();
                        args[i] = ConvertFromScriptVar(sv, parameters[i].ParameterType);
                    }
                    var result = captured.Invoke(capturedObj, args);
                    if (captured.ReturnType != typeof(void) && result != null)
                        scope.ReturnVar = ConvertToScriptVar(result);
                }, null);

                wrapper.AddChild(captured.Name, fnVar);
            }

            // Properties
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!Attribute.IsDefined(prop, typeof(ScriptVisibleAttribute))) continue;
                var captured = prop;
                var capturedObj = obj;
                var writable = Attribute.IsDefined(prop, typeof(ScriptWritableAttribute));

                if (prop.CanRead)
                {
                    var getFn = ScriptVar.CreateNativeFunction();
                    getFn.SetCallback((scope, _) =>
                    {
                        var val = captured.GetValue(capturedObj);
                        scope.ReturnVar = val != null ? ConvertToScriptVar(val) : ScriptVar.CreateNull();
                    }, null);
                    wrapper.AddChild("get_" + prop.Name, getFn);
                }

                if (writable && prop.CanWrite)
                {
                    var setFn = ScriptVar.CreateNativeFunction();
                    setFn.AddChild("value", ScriptVar.CreateUndefined());
                    setFn.SetCallback((scope, _) =>
                    {
                        var sv = scope.FindChild("value")?.Var ?? ScriptVar.CreateUndefined();
                        captured.SetValue(capturedObj, ConvertFromScriptVar(sv, captured.PropertyType));
                    }, null);
                    wrapper.AddChild("set_" + prop.Name, setFn);
                }
            }

            return wrapper;
        }

        private static ScriptVar ConvertToScriptVar(object value) => value switch
        {
            ScriptVar sv  => sv,
            bool b        => ScriptVar.FromInt(b ? 1 : 0),
            int i         => ScriptVar.FromInt(i),
            long l        => ScriptVar.FromInt((int)l),
            double d      => ScriptVar.FromDouble(d),
            float f       => ScriptVar.FromDouble((double)f),
            string s      => ScriptVar.FromString(s),
            _             => ScriptVar.FromString(value.ToString()),
        };

        private static object ConvertFromScriptVar(ScriptVar sv, Type targetType)
        {
            if (targetType == typeof(ScriptVar)) return sv;
            if (targetType == typeof(int))    return sv.Int;
            if (targetType == typeof(long))   return (long)sv.Int;
            if (targetType == typeof(double)) return sv.Float;
            if (targetType == typeof(float))  return (float)sv.Float;
            if (targetType == typeof(bool))   return sv.Bool;
            if (targetType == typeof(string)) return sv.String;
            return sv.String; // fallback
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
        /// Attach a profiler. The profiler receives <see cref="IProfiler.Enter"/> and
        /// <see cref="IProfiler.Exit"/> calls for every synchronous function call made
        /// during subsequent <see cref="Run"/> calls. Use <see cref="CpuProfiler"/> to
        /// collect V8-compatible CPU profiles.
        /// </summary>
        public void AttachProfiler(IProfiler profiler) => _profiler = profiler;

        /// <summary>Detach the current profiler. Subsequent runs execute without profiling overhead.</summary>
        public void DetachProfiler() => _profiler = null;

        /// <summary>
        /// Drain the micro-task queue, running all pending Promise callbacks.
        /// Call this after running async code to ensure all awaited work completes.
        /// </summary>
        public static void DrainMicroTasks() => Vm.MicroTaskQueue.DrainAll();

        /// <summary>
        /// Enqueue a microtask to run on the next <see cref="DrainMicroTasks"/> call.
        /// </summary>
        public static void QueueMicrotask(Action task) => Vm.MicroTaskQueue.Enqueue(task);

        /// <summary>
        /// Invoke a script (or native) function programmatically. Lets native and
        /// host code call back into script — e.g. Array map/filter/forEach/reduce
        /// callbacks and sort comparators.
        /// </summary>
        public ScriptVar CallFunction(ScriptVar function, ScriptVar thisArg, params ScriptVar[] args)
        {
            // Script execution is single-threaded: a simple single-slot, lock-free
            // VM cache handles the common sequential callback patterns (filter/map/
            // reduce/forEach) with zero synchronisation overhead. On a normal return
            // InvokeCallable leaves sp/call-depth balanced so the VM is clean to
            // reuse. Nested callbacks see the slot as null and create a fresh VM;
            // the inner VM is cached on return and the outer VM is silently dropped
            // (the GC collects it), keeping the slot size bounded at 1.
            var vm = _callVmCache;
            _callVmCache = null;
            if (vm == null) vm = new VirtualMachine(this);

            var result = vm.InvokeCallable(function, thisArg, args ?? []);

            if (_callVmCache == null) _callVmCache = vm;

            return result;
        }

        // Allocation-free fast paths for 1/2/3-argument callbacks used by array
        // higher-order methods. Eliminates the per-call ScriptVar[] allocation that
        // the `params ScriptVar[]` overload of CallFunction always produces.
        public ScriptVar CallCallback1(ScriptVar fn, ScriptVar thisArg, ScriptVar arg1)
        {
            var vm = _callVmCache; _callVmCache = null;
            if (vm == null) vm = new VirtualMachine(this);
            var result = vm.InvokeCallable1(fn, thisArg, arg1);
            if (_callVmCache == null) _callVmCache = vm;
            return result;
        }

        public ScriptVar CallCallback2(ScriptVar fn, ScriptVar thisArg, ScriptVar arg1, ScriptVar arg2)
        {
            var vm = _callVmCache; _callVmCache = null;
            if (vm == null) vm = new VirtualMachine(this);
            var result = vm.InvokeCallable2(fn, thisArg, arg1, arg2);
            if (_callVmCache == null) _callVmCache = vm;
            return result;
        }

        public ScriptVar CallCallback3(ScriptVar fn, ScriptVar thisArg, ScriptVar arg1, ScriptVar arg2, ScriptVar arg3)
        {
            var vm = _callVmCache; _callVmCache = null;
            if (vm == null) vm = new VirtualMachine(this);
            var result = vm.InvokeCallable3(fn, thisArg, arg1, arg2, arg3);
            if (_callVmCache == null) _callVmCache = vm;
            return result;
        }

        // Fallback thread-safe pool for async/microtask contexts where concurrency
        // is possible. The hot path above uses the lock-free _callVmCache instead.
        private VirtualMachine _callVmCache;

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
                    link = baseVar.AddChild(funcName, ScriptVar.CreateObject());
                }
                baseVar = link.Var;
                funcName = lexer.TokenString;
                lexer.MatchPropertyName();
            }

            var funcVar = ScriptVar.CreateNativeFunction();
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
                    link = baseVar.AddChild(propName, ScriptVar.CreateObject());
                }
                baseVar = link.Var;
                propName = lexer.TokenString;
                lexer.MatchPropertyName();
            }

            var propVar = ScriptVar.CreateUndefined();
            callbackCB.Invoke(propVar, null);

            baseVar.AddChild(propName, propVar);
        }

        private static void ParseFunctionArguments(ScriptVar funcVar, ScriptLex lexer)
        {
            lexer.Match((ScriptLex.LexTypes)'(');
            while (lexer.TokenType != (ScriptLex.LexTypes)')')
            {
                // A rest parameter (e.g. `...args`) collects all remaining call
                // arguments into an array. Stored with the "..." prefix so the call
                // path can recognise it; it must be the last parameter.
                if (lexer.TokenType == ScriptLex.LexTypes.Ellipsis)
                {
                    lexer.Match(ScriptLex.LexTypes.Ellipsis);
                    funcVar.AddChildNoDup("..." + lexer.TokenString, null);
                    lexer.Match(ScriptLex.LexTypes.Id);
                    break;
                }

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

            if (obj.IsSymbol)
            {
                var symCtor = Root.FindChild("Symbol")?.Var;
                if (symCtor != null)
                {
                    implementation = symCtor.FindChild(name);
                    if (implementation != null) return implementation;
                }
            }

            if (obj.IsRegexp)
            {
                // Regex literals (e.g. /foo/i) are bare ScriptVars not linked to the
                // RegExp class, so resolve their methods (exec/test/…) against the
                // RegExp constructor where those methods are registered — mirroring
                // how `new RegExp(...)` instances reach them via the prototype chain.
                var regexClass = Root.FindChild("RegExp")?.Var;
                if (regexClass != null)
                {
                    implementation = regexClass.FindChild(name);
                    if (implementation != null)
                    {
                        return implementation;
                    }
                }
            }

            if (obj.IsInt || obj.IsDouble)
            {
                // Number primitives (literals and computed values) are bare ScriptVars
                // not linked to the Number class, so resolve their methods (toFixed,
                // toString, toPrecision, …) against the Number constructor where those
                // methods are registered.
                var numberClass = Root.FindChild("Number")?.Var;
                if (numberClass != null)
                {
                    implementation = numberClass.FindChild(name);
                    if (implementation != null)
                    {
                        return implementation;
                    }
                }
            }

            if (obj.IsBigInt)
            {
                // BigInt primitives are bare ScriptVars not linked to the BigInt
                // class, so resolve their methods (toString, valueOf, …) against the
                // BigInt constructor where those methods are registered.
                var bigIntClass = Root.FindChild("BigInt")?.Var;
                if (bigIntClass != null)
                {
                    implementation = bigIntClass.FindChild(name);
                    if (implementation != null)
                    {
                        return implementation;
                    }
                }
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

            if (obj.GetData() is ITypedArrayAccess)
            {
                var typedArrayClass = Root.FindChild("TypedArray")?.Var;
                if (typedArrayClass != null)
                {
                    implementation = typedArrayClass.FindChild(name);
                    if (implementation != null) return implementation;
                }
            }

            if (obj.IsFunction)
            {
                implementation = functionClass.FindChild(name);
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
