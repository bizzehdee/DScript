using System;
using System.Collections.Generic;
using System.Text;

namespace DScript.Extras.FunctionProviders
{

    [ScriptClass("console")]
    public static class ConsoleFunctionProvider
    {


        [ScriptMethod("log", "val")]
        public static void ConsoleLogImpl(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val").GetParsableString();
            Console.WriteLine(val);
        }

        [ScriptMethod("error", "val")]
        public static void ConsoleErrorImpl(ScriptVar var, object userData)
        {
            var val = var.GetParameter("val").GetParsableString();
            Console.Error.WriteLine(val);
        }

        [ScriptMethod("clear")]
        public static void ConsoleClearImpl(ScriptVar var, object userData)
        {
            Console.Clear();
        }
    }
}
