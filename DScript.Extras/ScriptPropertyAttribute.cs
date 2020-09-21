using System;
using System.Collections.Generic;
using System.Text;

namespace DScript.Extras
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public class ScriptPropertyAttribute : Attribute
    {
        public string PropertyName { get; set; }
        public bool AppearAtRoot { get; set; } = false;

        public ScriptPropertyAttribute(string name)
        {
            PropertyName = name;
        }
    }
}
