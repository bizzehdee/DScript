using System;
using System.Collections.Generic;
using System.Text;

namespace DScript.Extras
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class ScriptClassAttribute : Attribute
    {
        public string ClassName { get; set; }

        public ScriptClassAttribute(string className)
        {
            ClassName = className;
        }
    }
}
