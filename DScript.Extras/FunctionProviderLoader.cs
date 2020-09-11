using System;
using System.Reflection;
using DScript;
using static DScript.ScriptEngine;

namespace DScript.Extras
{
    public class FunctionProviderLoader
    {

        public void LoadAllIntoEngine(ScriptEngine engine)
        {
            var execAssembly = Assembly.GetExecutingAssembly();

            TestForAttribute(engine, execAssembly);

            var referencedAssemblies = execAssembly.GetReferencedAssemblies();
            foreach (AssemblyName assembly in referencedAssemblies)
            {
                Assembly asm = Assembly.Load(assembly);

                TestForAttribute(engine, asm);
            }
        }

        private void TestForAttribute(ScriptEngine engine, Assembly asm)
        {
            Type[] types = asm.GetTypes();
            foreach (Type type in types)
            {
                object[] scObjects = type.GetCustomAttributes(typeof(ScriptClassAttribute), false);
                if (scObjects.Length > 0)
                {
                    ProcessType(engine, type, scObjects[0] as ScriptClassAttribute);
                }
            }
        }

        private void ProcessType(ScriptEngine engine, Type type, ScriptClassAttribute attr)
        {
            engine.AddObject(attr.Namespace ?? new string[0], attr.ClassName ?? type.Name, new ScriptVar(null, ScriptVar.Flags.Object | ScriptVar.Flags.Native) { ClassType = type });

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            ProcessMethods(engine, methods, attr);

            methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
            ProcessMethods(engine, methods, attr);

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Static);
            ProcessProperties(engine, properties, attr, null);
        }

        private void ProcessMethods(ScriptEngine engine, MethodInfo[] methods, ScriptClassAttribute attr)
        {
            foreach (MethodInfo method in methods)
            {
                if (method.IsSpecialName) continue;

                var parameters = method.GetParameters();
                if (parameters.Length < 1) continue;

                var argNames = new string[parameters.Length - 1];
                for (int i = 0; i < parameters.Length - 1; i++)
                {
                    argNames[i] = parameters[i].Name;
                }

                var methodCopy = method;
                var ns = attr.Namespace ?? new string[0];

                if (attr.ClassName == null && ns.Length == 0)
                {
                    ns = null;
                }
                else
                {
                    Array.Resize(ref ns, ns.Length + 1);
                    ns[ns.Length - 1] = attr.ClassName;
                }

                engine.AddMethod(ns, method.Name, argNames, CreateScriptFunction(methodCopy, parameters), this);
            }
        }

        private void ProcessProperties(ScriptEngine engine, PropertyInfo[] properties, ScriptClassAttribute attr, object instance)
        {
            foreach (PropertyInfo property in properties)
            {
                if (property.IsSpecialName) continue;

                var propertyCopy = property;
                var ns = attr.Namespace ?? new string[0];

                if (attr.ClassName == null && ns.Length == 0)
                {
                    ns = null;
                }
                else
                {
                    Array.Resize(ref ns, ns.Length + 1);
                    ns[ns.Length - 1] = attr.ClassName;
                }

                if (propertyCopy.PropertyType == typeof(bool))
                {
                    engine.AddObject(ns, propertyCopy.Name, new ScriptVar((bool)propertyCopy.GetValue(instance, null)));
                }
                else if (propertyCopy.PropertyType == typeof(string))
                {
                    engine.AddObject(ns, propertyCopy.Name, new ScriptVar((string)propertyCopy.GetValue(instance, null)));
                }
                else if (propertyCopy.PropertyType == typeof(decimal) || propertyCopy.PropertyType == typeof(float) || propertyCopy.PropertyType == typeof(double))
                {
                    engine.AddObject(ns, propertyCopy.Name, new ScriptVar((double)propertyCopy.GetValue(instance, null)));
                }
                else if (propertyCopy.PropertyType == typeof(int))
                {
                    engine.AddObject(ns, propertyCopy.Name, new ScriptVar((int)propertyCopy.GetValue(instance, null)));
                }
            }
        }

        private ScriptCallbackCB CreateScriptFunction(MethodInfo method, ParameterInfo[] parameters)
        {
            return delegate (ScriptVar var, object userdata, ScriptVar parent)
            {
                var args = new object[parameters.Length];

                var i = 0;
                for (; i < parameters.Length - 1; i++)
                {
                    args[i] = var.GetParameter(parameters[i].Name).GetData();
                }

                args[i] = userdata;

                object returnVal;

                if (method.IsStatic)
                {
                    returnVal = method.Invoke(null, args);
                }
                else
                {
                    returnVal = method.Invoke(parent.ClassInstance, args);
                }

                if (method.ReturnType == typeof(int))
                {
                    var.SetReturnVar(new ScriptVar(Convert.ToInt32(returnVal), ScriptVar.Flags.Integer));
                }
                else if (method.ReturnType == typeof(bool))
                {
                    var.SetReturnVar(new ScriptVar(Convert.ToBoolean(returnVal) ? 1 : 0, ScriptVar.Flags.Integer));
                }
                else if (method.ReturnType == typeof(double) || method.ReturnType == typeof(float))
                {
                    var.SetReturnVar(new ScriptVar(Convert.ToDouble(returnVal), ScriptVar.Flags.Double));
                }
                else if (method.ReturnType == typeof(string))
                {
                    var.SetReturnVar(new ScriptVar(Convert.ToString(returnVal), ScriptVar.Flags.String));
                }
            };
        }
    }
}
