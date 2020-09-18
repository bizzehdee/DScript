using DScript.Extras;
using DScript.Extras.FunctionProviders;
using System;
using System.IO;

namespace DScript.Demo
{
    class Program
    {
        static void Main(string[] args)
        {
            var engine = new ScriptEngine();
            var loader = new EngineFunctionLoader();
            loader.RegisterFunctions(engine);

            var testScript = File.ReadAllText("testscript.js");

            engine.Execute(testScript);
        }
    }
}
