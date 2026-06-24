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
        public Func<string, string, string> ModuleLoader { get; set; }

        /// <summary>Path of the module currently being executed (used for relative imports).</summary>
        public string CurrentModulePath { get; set; } = string.Empty;

        private readonly Dictionary<string, ScriptVar> _moduleCache = new();

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
            Root = (new ScriptVar(ScriptVar.Flags.Object)).Ref();

            objectClass = (new ScriptVar(ScriptVar.Flags.Object)).Ref();
            stringClass = (new ScriptVar(ScriptVar.Flags.Object)).Ref();
            arrayClass = (new ScriptVar(ScriptVar.Flags.Object)).Ref();
            functionClass = (new ScriptVar(ScriptVar.Flags.Object)).Ref();

            Root.AddChild("Object", objectClass);
            Root.AddChild("String", stringClass);
            Root.AddChild("Array", arrayClass);
            Root.AddChild("Function", functionClass);

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
            var requireVar = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            requireVar.AddChild("path", new ScriptVar(ScriptVar.Flags.Undefined));
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

                // Load source via the user-supplied loader.
                string source = null;
                if (ModuleLoader != null)
                    source = ModuleLoader(path, CurrentModulePath);

                if (source == null)
                    throw new ScriptException($"Cannot find module '{path}'");

                // Pre-seed the cache BEFORE running the module to break circular require cycles.
                var exportsObj = new ScriptVar(ScriptVar.Flags.Object);
                _moduleCache[path] = exportsObj;

                // Build an isolated module environment:
                //   - a fresh root object containing __exports__ and copies of all globals
                var moduleRoot = new ScriptVar(ScriptVar.Flags.Object);
                moduleRoot.AddChild("__exports__", exportsObj);

                // module object: { exports, filename, loaded }
                var moduleObj = new ScriptVar(ScriptVar.Flags.Object);
                moduleObj.AddChild("exports", exportsObj);
                moduleObj.AddChild("filename", new ScriptVar(path));
                moduleObj.AddChild("loaded", new ScriptVar(0));
                moduleRoot.AddChild("module", moduleObj);

                // exports shorthand (alias for __exports__ / module.exports)
                moduleRoot.AddChild("exports", exportsObj);

                // __filename and __dirname
                moduleRoot.AddChild("__filename", new ScriptVar(path));
                var dirName = GetDirName(path);
                moduleRoot.AddChild("__dirname", new ScriptVar(dirName));

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
                    using var compiler = new Compiler.DScriptCompiler();
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
                if (loadedLink != null) loadedLink.Var.Bool = true;

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
            var errorsArr = new ScriptVar(); errorsArr.SetArray();
            for (var j = 0; j < len; j++)
                errorsArr.SetArrayIndex(j, reasons[j] ?? new ScriptVar(ScriptVar.Flags.Undefined));
            var err = new ScriptVar(ScriptVar.Flags.Object);
            err.AddChild("name", new ScriptVar("AggregateError"));
            err.AddChild("message", new ScriptVar("All promises were rejected"));
            err.AddChild("errors", errorsArr);
            return err;
        }

        private void RegisterPromiseBuiltin()
        {
            // Promise constructor: new Promise(function(resolve, reject) { ... })
            var promiseCtorVar = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            promiseCtorVar.AddChild("executor", new ScriptVar(ScriptVar.Flags.Undefined));
            promiseCtorVar.SetCallback((scope, _) =>
            {
                var executor = scope.FindChild("executor")?.Var;
                var promiseObj = new Vm.PromiseObject();
                var vm = new VirtualMachine(this);

                // resolve callback
                var resolveFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
                resolveFn.AddChild("value", new ScriptVar(ScriptVar.Flags.Undefined));
                resolveFn.SetCallback((s, __) =>
                {
                    var v = s.FindChild("value")?.Var ?? new ScriptVar(ScriptVar.Flags.Undefined);
                    promiseObj.Resolve(v);
                }, null);

                // reject callback
                var rejectFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
                rejectFn.AddChild("reason", new ScriptVar(ScriptVar.Flags.Undefined));
                rejectFn.SetCallback((s, __) =>
                {
                    var r = s.FindChild("reason")?.Var ?? new ScriptVar(ScriptVar.Flags.Undefined);
                    promiseObj.Reject(r);
                }, null);

                if (executor != null && executor.IsFunction)
                {
                    try { vm.InvokeCallable(executor, null, [resolveFn, rejectFn]); }
                    catch (Exception ex) { promiseObj.Reject(new ScriptVar(ex.Message)); }
                }

                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(promiseObj.ToScriptVar(vm));
            }, null);

            // Promise.resolve(value) static method
            var promiseClass = new ScriptVar(ScriptVar.Flags.Object);
            promiseClass.SetData(promiseCtorVar); // store ctor reference

            // Copy the constructor callback to the class var so `new Promise(fn)` works
            // We make promiseClass a function so the VM's `new` opcode can call it
            var promiseVar = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            promiseVar.AddChild("executor", new ScriptVar(ScriptVar.Flags.Undefined));
            promiseVar.SetCallback(promiseCtorVar.GetCallback(), null);

            // Promise.resolve
            var resolveFnStatic = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            resolveFnStatic.AddChild("value", new ScriptVar(ScriptVar.Flags.Undefined));
            resolveFnStatic.SetCallback((scope, _) =>
            {
                var v = scope.FindChild("value")?.Var ?? new ScriptVar(ScriptVar.Flags.Undefined);
                var vm = new VirtualMachine(this);
                var p = Vm.PromiseObject.Resolved(v);
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(p.ToScriptVar(vm));
            }, null);
            promiseVar.AddChild("resolve", resolveFnStatic);

            // Promise.reject
            var rejectFnStatic = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            rejectFnStatic.AddChild("reason", new ScriptVar(ScriptVar.Flags.Undefined));
            rejectFnStatic.SetCallback((scope, _) =>
            {
                var r = scope.FindChild("reason")?.Var ?? new ScriptVar(ScriptVar.Flags.Undefined);
                var vm = new VirtualMachine(this);
                var p = Vm.PromiseObject.Rejected(r);
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(p.ToScriptVar(vm));
            }, null);
            promiseVar.AddChild("reject", rejectFnStatic);

            // Promise.all(arr)
            var allFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            allFn.AddChild("arr", new ScriptVar(ScriptVar.Flags.Undefined));
            allFn.SetCallback((scope, _) =>
            {
                var arr = scope.FindChild("arr")?.Var ?? new ScriptVar(ScriptVar.Flags.Undefined);
                var vm2 = new VirtualMachine(this);
                var len = arr.IsArray ? arr.GetArrayLength() : 0;
                if (len == 0)
                {
                    var emptyArr = new ScriptVar(); emptyArr.SetArray();
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
                    p.Then(v => { values[idx] = v; remaining[0]--; if (remaining[0] == 0) { var r = new ScriptVar(); r.SetArray(); for (var j = 0; j < len; j++) r.SetArrayIndex(j, values[j] ?? new ScriptVar(ScriptVar.Flags.Undefined)); result.Resolve(r); } },
                           reason => result.Reject(reason));
                }
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(result.ToScriptVar(vm2));
            }, null);
            promiseVar.AddChild("all", allFn);

            // Promise.allSettled(arr)
            var allSettledFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            allSettledFn.AddChild("arr", new ScriptVar(ScriptVar.Flags.Undefined));
            allSettledFn.SetCallback((scope, _) =>
            {
                var arr = scope.FindChild("arr")?.Var ?? new ScriptVar(ScriptVar.Flags.Undefined);
                var vm2 = new VirtualMachine(this);
                var len = arr.IsArray ? arr.GetArrayLength() : 0;
                var result = new Vm.PromiseObject();
                var results = new ScriptVar[len];
                var remaining = new[] { len == 0 ? -1 : len };
                if (len == 0) { var emptyArr = new ScriptVar(); emptyArr.SetArray(); result.Resolve(emptyArr); }
                for (var i = 0; i < len; i++)
                {
                    var idx = i;
                    var p = Vm.PromiseObject.Wrap(arr.GetArrayIndex(i));
                    Action<ScriptVar, bool> settle = (v, fulfilled) =>
                    {
                        var entry = new ScriptVar(ScriptVar.Flags.Object);
                        entry.AddChild("status", new ScriptVar(fulfilled ? "fulfilled" : "rejected"));
                        if (fulfilled) entry.AddChild("value", v); else entry.AddChild("reason", v);
                        results[idx] = entry;
                        remaining[0]--;
                        if (remaining[0] == 0) { var r = new ScriptVar(); r.SetArray(); for (var j = 0; j < len; j++) r.SetArrayIndex(j, results[j]); result.Resolve(r); }
                    };
                    p.Then(v => settle(v, true), r => settle(r, false));
                }
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(result.ToScriptVar(vm2));
            }, null);
            promiseVar.AddChild("allSettled", allSettledFn);

            // Promise.race(arr)
            var raceFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            raceFn.AddChild("arr", new ScriptVar(ScriptVar.Flags.Undefined));
            raceFn.SetCallback((scope, _) =>
            {
                var arr = scope.FindChild("arr")?.Var ?? new ScriptVar(ScriptVar.Flags.Undefined);
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
            var anyFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            anyFn.AddChild("arr", new ScriptVar(ScriptVar.Flags.Undefined));
            anyFn.SetCallback((scope, _) =>
            {
                var arr = scope.FindChild("arr")?.Var ?? new ScriptVar(ScriptVar.Flags.Undefined);
                var vm2 = new VirtualMachine(this);
                var len = arr.IsArray ? arr.GetArrayLength() : 0;
                var result = new Vm.PromiseObject();
                var rejectedCount = new[] { 0 };
                var reasons = new ScriptVar[len];
                if (len == 0) { result.Reject(MakeAggregateError(reasons, 0)); }
                for (var i = 0; i < len; i++)
                {
                    var idx = i;
                    var p = Vm.PromiseObject.Wrap(arr.GetArrayIndex(i));
                    p.Then(v => result.Resolve(v), r =>
                    {
                        reasons[idx] = r;
                        rejectedCount[0]++;
                        if (rejectedCount[0] == len)
                            result.Reject(MakeAggregateError(reasons, len));
                    });
                }
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(result.ToScriptVar(vm2));
            }, null);
            promiseVar.AddChild("any", anyFn);

            // Promise.try(fn, ...args) — ES2025
            var tryFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            tryFn.AddChild("fn", new ScriptVar(ScriptVar.Flags.Undefined));
            tryFn.SetCallback((scope, _) =>
            {
                var fn = scope.FindChild("fn")?.Var ?? new ScriptVar(ScriptVar.Flags.Undefined);
                var vm2 = new VirtualMachine(this);
                Vm.PromiseObject result;
                try
                {
                    var retVal = fn.IsFunction
                        ? CallFunction(fn, null)
                        : new ScriptVar(ScriptVar.Flags.Undefined);
                    result = Vm.PromiseObject.Wrap(retVal);
                }
                catch (Exception ex)
                {
                    result = Vm.PromiseObject.Rejected(new ScriptVar(ex.Message));
                }
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(result.ToScriptVar(vm2));
            }, null);
            promiseVar.AddChild("try", tryFn);

            // Promise.withResolvers()
            var withResolversFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            withResolversFn.SetCallback((scope, _) =>
            {
                var vm2 = new VirtualMachine(this);
                var promiseObj = new Vm.PromiseObject();

                var resolveFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
                resolveFn.AddChild("value", new ScriptVar(ScriptVar.Flags.Undefined));
                resolveFn.SetCallback((s, __) =>
                {
                    var v = s.FindChild("value")?.Var ?? new ScriptVar(ScriptVar.Flags.Undefined);
                    promiseObj.Resolve(v);
                }, null);

                var rejectFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
                rejectFn.AddChild("reason", new ScriptVar(ScriptVar.Flags.Undefined));
                rejectFn.SetCallback((s, __) =>
                {
                    var r = s.FindChild("reason")?.Var ?? new ScriptVar(ScriptVar.Flags.Undefined);
                    promiseObj.Reject(r);
                }, null);

                var result = new ScriptVar(ScriptVar.Flags.Object);
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
            var wrapper = new ScriptVar(ScriptVar.Flags.Object);
            if (obj == null) return wrapper;

            var type = obj.GetType();

            // Methods
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!Attribute.IsDefined(method, typeof(ScriptVisibleAttribute))) continue;
                var captured = method;
                var capturedObj = obj;
                var parameters = captured.GetParameters();

                var fnVar = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
                // Register declared parameters so the VM can resolve them by name.
                foreach (var p in parameters)
                    fnVar.AddChild(p.Name ?? $"arg{p.Position}", new ScriptVar(ScriptVar.Flags.Undefined));

                fnVar.SetCallback((scope, _) =>
                {
                    var args = new object[parameters.Length];
                    for (var i = 0; i < parameters.Length; i++)
                    {
                        var pName = parameters[i].Name ?? $"arg{i}";
                        var sv = scope.FindChild(pName)?.Var ?? new ScriptVar(ScriptVar.Flags.Undefined);
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
                    var getFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
                    getFn.SetCallback((scope, _) =>
                    {
                        var val = captured.GetValue(capturedObj);
                        scope.ReturnVar = val != null ? ConvertToScriptVar(val) : new ScriptVar(ScriptVar.Flags.Null);
                    }, null);
                    wrapper.AddChild("get_" + prop.Name, getFn);
                }

                if (writable && prop.CanWrite)
                {
                    var setFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
                    setFn.AddChild("value", new ScriptVar(ScriptVar.Flags.Undefined));
                    setFn.SetCallback((scope, _) =>
                    {
                        var sv = scope.FindChild("value")?.Var ?? new ScriptVar(ScriptVar.Flags.Undefined);
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
            bool b        => new ScriptVar(b ? 1 : 0),
            int i         => new ScriptVar(i),
            long l        => new ScriptVar((int)l),
            double d      => new ScriptVar(d),
            float f       => new ScriptVar((double)f),
            string s      => new ScriptVar(s),
            _             => new ScriptVar(value.ToString()),
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
                lexer.MatchPropertyName();
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
                lexer.MatchPropertyName();
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

            if (obj.IsSymbol)
            {
                var symCtor = Root.FindChild("Symbol")?.Var;
                if (symCtor != null)
                {
                    implementation = symCtor.FindChild(name);
                    if (implementation != null) return implementation;
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
