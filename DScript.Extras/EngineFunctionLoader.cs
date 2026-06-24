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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

namespace DScript.Extras
{
    public class EngineFunctionLoader
    {
        /// <summary>
        /// Registers all built-in DScript.Extras providers with the engine using
        /// the compile-time <see cref="GeneratedFunctionRegistrar"/>. This path is
        /// reflection-free and therefore trim- and Native-AOT-safe.
        /// </summary>
        /// <param name="permissions">
        /// Optional permission set. Defaults to <see cref="EnginePermissions.All"/> for
        /// backwards compatibility. Pass a restricted set to sandbox the script.
        /// </param>
        public void RegisterFunctions(ScriptEngine engine, EnginePermissions permissions = EnginePermissions.All)
        {
            EnginePermissionStore.Set(engine, permissions);
            // Map and Set constructors must be registered before the generated
            // method registrar so that AddNative("function Map.get(...)", ...)
            // attaches methods directly onto the constructor var rather than
            // creating a separate plain-Object placeholder that the constructor
            // var would later shadow in the child list.
            MapRegistrar.Register(engine);
            SetRegistrar.Register(engine);
            WeakMapRegistrar.Register(engine);
            WeakSetRegistrar.Register(engine);
            WeakRefRegistrar.Register(engine);
            DateRegistrar.Register(engine);
            RegExpRegistrar.Register(engine);
            BufferRegistrar.Register(engine);
            EventEmitterRegistrar.Register(engine);
            EventEmitterRegistrar.RegisterHostEvents(engine);
            GeneratedFunctionRegistrar.RegisterAll(engine, engine);
            SetupErrorPrototypeChain(engine);
        }

        private static void SetupErrorPrototypeChain(ScriptEngine engine)
        {
            // Error hierarchy: TypeError/RangeError/etc.prototype → Error
            // This enables `new TypeError() instanceof Error` by adding a "prototype"
            // child on each subtype constructor pointing to the Error constructor.
            var errorCtor = engine.Root.FindChild("Error")?.Var;
            if (errorCtor == null) return;
            foreach (var subtype in new[] { "TypeError", "RangeError", "ReferenceError", "SyntaxError", "URIError", "EvalError", "AggregateError" })
            {
                var subCtor = engine.Root.FindChild(subtype)?.Var;
                if (subCtor != null && subCtor.FindChild(ScriptVar.PrototypeClassName) == null)
                    subCtor.AddChild(ScriptVar.PrototypeClassName, errorCtor);
            }
        }

        /// <summary>
        /// Populates <c>process.argv</c> with the supplied string array.
        /// Call this after <see cref="RegisterFunctions"/> to inject host arguments.
        /// </summary>
        public static void SetArgv(ScriptEngine engine, string[] argv)
        {
            var arr = new ScriptVar();
            arr.SetArray();
            for (var i = 0; i < argv.Length; i++)
                arr.SetArrayIndex(i, new ScriptVar(argv[i]));
            engine.Root.AddChildNoDup("__argv__", arr);
        }

        /// <summary>
        /// Legacy registration that scans every loaded assembly for
        /// <see cref="ScriptClassAttribute"/> providers via reflection. Use this only
        /// when providers live in assemblies the source generator does not run over
        /// (e.g. third-party plugin DLLs discovered at runtime). Not trim/AOT-safe.
        /// </summary>
        [RequiresUnreferencedCode(
            "Scans all loaded assemblies via reflection; provider types may be trimmed. " +
            "Prefer RegisterFunctions, which uses the source-generated registrar.")]
        public void RegisterFunctionsViaReflection(ScriptEngine engine)
        {
            var typesWithMyAttribute =
            // Note the AsParallel here, this will parallelize everything after.
            from a in AppDomain.CurrentDomain.GetAssemblies().AsParallel()
            from t in a.GetTypes()
            where t.IsClass && t.IsPublic && t.IsAbstract
            let attributes = t.GetCustomAttributes(typeof(ScriptClassAttribute), true)
            where attributes != null && attributes.Length > 0
            select new { Type = t, Attributes = attributes.Cast<ScriptClassAttribute>() };

            foreach (var myType in typesWithMyAttribute)
            {
                ProcessTypes(myType.Type, myType.Attributes.FirstOrDefault(), engine);

            }
        }

        [RequiresUnreferencedCode("Uses reflection and Delegate.CreateDelegate over discovered types.")]
        private void ProcessTypes(Type t, ScriptClassAttribute attribute, ScriptEngine engine)
        {
            var publicStaticMethods = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.CustomAttributes.Any(x => (x.AttributeType == typeof(ScriptMethodAttribute) || x.AttributeType == typeof(ScriptPropertyAttribute)) && Delegate.CreateDelegate(typeof(ScriptEngine.ScriptCallbackCB), f) != null));

            foreach (var method in publicStaticMethods)
            {
                var methodAttributes = method.GetCustomAttributes<ScriptMethodAttribute>();
                var propertyAttributes = method.GetCustomAttributes<ScriptPropertyAttribute>();

                foreach (var methodAttribute in methodAttributes)
                {
                    var name = methodAttribute.AppearAtRoot ? methodAttribute.MethodName : string.Format("{0}.{1}", attribute.ClassName, methodAttribute.MethodName);

                    var definition = "function " + name + "(";

                    if (methodAttribute.MethodParameters != null)
                    {
                        var isFirst = true;
                        foreach (var param in methodAttribute.MethodParameters)
                        {
                            if (!isFirst)
                            {
                                definition += ",";
                            }
                            isFirst = false;
                            definition += param;
                        }
                    }

                    definition += ")";


                    engine.AddNative(definition, (ScriptEngine.ScriptCallbackCB)Delegate.CreateDelegate(typeof(ScriptEngine.ScriptCallbackCB), method), engine);
                }

                foreach (var propertyAttribute in propertyAttributes)
                {
                    var name = propertyAttribute.AppearAtRoot ? propertyAttribute.PropertyName : string.Format("{0}.{1}", attribute.ClassName, propertyAttribute.PropertyName);

                    engine.AddNativeProperty(name, (ScriptEngine.ScriptCallbackCB)Delegate.CreateDelegate(typeof(ScriptEngine.ScriptCallbackCB), method), engine);
                }


            }
        }
    }
}
