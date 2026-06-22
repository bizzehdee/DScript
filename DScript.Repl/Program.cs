/*
DScript REPL — a simple read-eval-print loop backed by a persistent ScriptEngine.
State (variables, functions, classes) persists across inputs within a session.

Usage:
  dotnet run --project DScript.Repl
  > var x = 10;
  > x + 5
  15
  > .exit  (or Ctrl-C / Ctrl-D to quit)
*/

using System;
using DScript;

Console.WriteLine("DScript REPL  (type .exit to quit, .help for commands)");
Console.WriteLine();

var engine = new ScriptEngine();

// Register a print(arg0) native so scripts can produce output.
engine.AddNative("function print(arg0)", (v, _) =>
{
    var arg = v.GetParameter("arg0");
    Console.WriteLine(arg != null ? arg.GetParsableString() : "undefined");
}, null);

while (true)
{
    Console.Write("> ");
    var line = Console.ReadLine();

    if (line == null)       // Ctrl-D / EOF
        break;

    line = line.Trim();

    if (line.Length == 0)
        continue;

    switch (line)
    {
        case ".exit":
        case ".quit":
            goto done;
        case ".help":
            Console.WriteLine("  .exit / .quit  — quit the REPL");
            Console.WriteLine("  .help          — show this message");
            Console.WriteLine("  Any DScript statement or expression is evaluated.");
            Console.WriteLine("  State persists across inputs.");
            continue;
    }

    // If the input looks like a bare expression (no trailing ';' and not a
    // declaration/block statement), evaluate and print the result.
    var isDeclaration = line.StartsWith("var ", StringComparison.Ordinal)
                     || line.StartsWith("let ", StringComparison.Ordinal)
                     || line.StartsWith("const ", StringComparison.Ordinal)
                     || line.StartsWith("function ", StringComparison.Ordinal)
                     || line.StartsWith("class ", StringComparison.Ordinal)
                     || line.EndsWith(';');

    try
    {
        if (!isDeclaration)
        {
            var result = engine.EvalComplex(line);
            if (result?.Var != null && !result.Var.IsUndefined)
                Console.WriteLine(result.Var.GetParsableString());
        }
        else
        {
            engine.Execute(line);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
    }
}

done:
Console.WriteLine("bye.");
