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

        private void ProcessTypes(Type t, ScriptClassAttribute attribute, ScriptEngine engine)
        {
            var publicStaticMethods = t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
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
