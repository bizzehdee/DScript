using DScript.Extras.FunctionProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DScript.Extras
{
    public class EngineFunctionLoader
    {
        public void RegisterFunctions(ScriptEngine engine)
        {
            var baseLoader = new BaseFunctionProvider();
            baseLoader.RegisterFunctions(engine);

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
                var methods = ProcessTypes(myType.Type, myType.Attributes.FirstOrDefault());

                foreach (var method in methods)
                {
                    engine.AddNative(method.Key, method.Value, engine);
                }
            }
        }

        private IDictionary<string, ScriptEngine.ScriptCallbackCB> ProcessTypes(Type t, ScriptClassAttribute attribute)
        {
            var publicStaticMethods = t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(f => f.CustomAttributes.Any(x => x.AttributeType == typeof(ScriptMethodAttribute)) && Delegate.CreateDelegate(typeof(ScriptEngine.ScriptCallbackCB), f) != null);
            var returnDict = new Dictionary<string, ScriptEngine.ScriptCallbackCB>();
            foreach (var method in publicStaticMethods)
            {
                var methodAttribute = method.GetCustomAttribute<ScriptMethodAttribute>();

                var name = methodAttribute.AppearAtRoot ? methodAttribute.MethodName : string.Format("{0}.{1}", attribute.ClassName, methodAttribute.MethodName);

                var definition = "function " + name + "(";
                var isFirst = true;
                foreach (var param in methodAttribute.MethodParameters)
                {
                    if(!isFirst)
                    {
                        definition += ",";
                    }
                    isFirst = false;
                    definition += param;
                }

                definition += ")";

                returnDict.Add(definition, (ScriptEngine.ScriptCallbackCB)Delegate.CreateDelegate(typeof(ScriptEngine.ScriptCallbackCB), method));
            }

            return returnDict;
        }
    }
}
