﻿/*
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
using System.Linq;
using System.Reflection;

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
