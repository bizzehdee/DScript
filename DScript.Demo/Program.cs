using DScript.Extras;
using System;
using System.IO;

namespace DScript.Demo
{
    class Program
    {
        static void Main(string[] args)
        {
            var engine = new ScriptEngine();
            var loader = new FunctionProviderLoader();
            loader.LoadAllIntoEngine(engine);

            var testScript = File.ReadAllText("testscript.js");

            engine.Execute(testScript);
        }
    }
}
