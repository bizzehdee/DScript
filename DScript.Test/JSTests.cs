using DScript.Extras;
using DScript.Extras.FunctionProviders;
using NUnit.Framework;
using System.IO;
using System.Linq;

namespace DScript.Test
{
    public class JSTests
    {

        [Test, TestCaseSource("GetTestCases")]
        public void TestFile(string filename)
        {
            var file = File.ReadAllText(filename);
            var engine = new ScriptEngine();
            var loader = new EngineFunctionLoader();
            loader.RegisterFunctions(engine);

            engine.Root.AddChild("result", new ScriptVar(0));

            ScriptException ex = null;

            try
            {
                engine.Execute(file);
            }
            catch(ScriptException e)
            {
                ex = e;
            }

            engine.Root.Trace(0, "root");

            var result = engine.Root.GetParameter("result");
            var resultAsBool = result.GetBool();
            Assert.IsTrue(resultAsBool, ex != null ? ex.Message : string.Empty);
        }

        private static string[] GetTestCases()
        {
            var files = Directory.EnumerateFiles("Tests", "*.js");
            return files.ToArray();
        }
    }
}