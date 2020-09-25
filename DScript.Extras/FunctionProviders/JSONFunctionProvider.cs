using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("JSON")]
    public static class JSONFunctionProvider
    {
        [ScriptMethod("parse", "str")]
        public static void EvalImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var script = var.GetParameter("str").String;
            var returnVal = engine.EvalComplex(script);
            var.ReturnVar = returnVal.Var;
        }


        [ScriptMethod("stringify", "obj", "replacer")]
        public static void JsonStringifyImpl(ScriptVar var, object userData)
        {
            var stream = new MemoryStream();
            var.GetParameter("obj").GetJSON(stream, "");

            stream.Seek(0, SeekOrigin.Begin);

            var streamReader = new StreamReader(stream);
            var json = streamReader.ReadToEnd();

            var.ReturnVar.String = json;
        }
    }
}
