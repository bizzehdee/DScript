using System;
using System.Collections.Generic;
using System.Text;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("Object")]
    public static class ObjectFunctionProvider
    {

        [ScriptMethod("dump")]
        public static void ObjectDumpImpl(ScriptVar var, object userData)
        {
            var.GetParameter("this").Trace(0, null);
        }

        [ScriptMethod("clone")]
        public static void ObjectCloneImpl(ScriptVar var, object userData)
        {
            var obj = var.GetParameter("this");
            var.ReturnVar.CopyValue(obj);
        }
    }
}
