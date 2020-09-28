using System;
using System.Collections.Generic;
using System.Text;

namespace DScript
{
    class JITException : Exception
    {
        public JITException()
        {

        }

        public JITException(string message) : 
            base(message)
        {

        }

        public JITException(ScriptVar varObj)
        {
            VarObj = varObj;
        }

        public ScriptVar VarObj { get; }
    }
}
