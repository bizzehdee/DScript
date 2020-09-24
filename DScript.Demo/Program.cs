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
            var testScript = File.ReadAllText("testscript.js");

            var lexer = new ScriptLex(testScript);
            do
            {
                Console.WriteLine("{0,-16} | {1}", ScriptLex.LexTypesToString(lexer.TokenType), lexer.TokenString);
                lexer.GetNextToken();
            } while (lexer.TokenType != ScriptLex.LexTypes.Eof);

            var engine = new ScriptEngine();
            var loader = new EngineFunctionLoader();
            loader.RegisterFunctions(engine);


            engine.Trace();

            engine.Execute(testScript);
        }
    }
}
