using System;
using System.Collections.Generic;
using System.Text;

namespace DScript.Extras
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class ScriptMethodAttribute : Attribute
    {
        public string MethodName { get; set; }
        public string[] MethodParameters { get; set; }
        public bool AppearAtRoot { get; set; } = false;

        public ScriptMethodAttribute(string methodName)
        {
            MethodName = methodName;
            MethodParameters = null;
        }

        public ScriptMethodAttribute(string methodName, params string[] parameters)
        {
            MethodName = methodName;
            MethodParameters = parameters;
        }
    }
}
