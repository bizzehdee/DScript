/*
DScript performance benchmark.

This program uses ONLY the public ScriptEngine API (Execute / Root), which is
identical on the old tree-walking engine and the new bytecode engine. That lets
you run the exact same benchmark against either version and compare.

How to compare old vs new:
  1. New (bytecode) engine — on this branch:
         dotnet run --project DScript.Benchmark -c Release
  2. Old (tree-walking) engine — on master, grab just this folder and run it:
         git checkout master
         git checkout feature/bytecode-vm -- DScript.Benchmark
         dotnet run --project DScript.Benchmark/DScript.Benchmark.csproj -c Release
         git checkout -- DScript.Benchmark   # (or delete it) to tidy up afterwards

Always build in Release. Pass a scale factor to make the loop workloads bigger
or smaller, e.g. `dotnet run -c Release -- 0.25` if the old engine is too slow:
*/

using System;
using System.Diagnostics;
using System.Globalization;
using DScript;
using DScript.Extras;

internal static class Program
{
    private static void Main(string[] args)
    {
        var scale = 1.0;
        if (args.Length > 0 && double.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var s) && s > 0)
        {
            scale = s;
        }

        var loopN = (int)(500_000 * scale);
        var callN = (int)(200_000 * scale);
        var nested = (int)(700 * scale);
        var stringN = (int)(20_000 * scale);
        const int fibN = 27; // exponential — kept fixed regardless of scale

        var benchmarks = new (string name, string code)[]
        {
            ("tight loop (sum 0.." + loopN + ")",
                $"var s = 0; for (var i = 0; i < {loopN}; i = i + 1) {{ s = s + i; }} result = s;"),

            ("function calls (" + callN + ")",
                $"function add(a, b) {{ return a + b; }} var s = 0; for (var i = 0; i < {callN}; i = i + 1) {{ s = add(s, i); }} result = s;"),

            ("recursive fib(" + fibN + ")",
                $"function fib(n) {{ if (n < 2) {{ return n; }} return fib(n - 1) + fib(n - 2); }} result = fib({fibN});"),

            ("nested loops (" + nested + "x" + nested + ")",
                $"var c = 0; for (var i = 0; i < {nested}; i = i + 1) {{ for (var j = 0; j < {nested}; j = j + 1) {{ c = c + 1; }} }} result = c;"),

            ("string concat (" + stringN + ")",
                $"var str = \"\"; for (var i = 0; i < {stringN}; i = i + 1) {{ str = str + \"x\"; }} result = str.length;"),
        };

        Console.WriteLine($"DScript benchmark  (scale={scale.ToString(CultureInfo.InvariantCulture)}, .NET {System.Environment.Version})");
        Console.WriteLine(new string('-', 64));
        Console.WriteLine($"{"workload",-34}{"best ms",12}{"result",16}");
        Console.WriteLine(new string('-', 64));

        var total = 0.0;
        foreach (var (name, code) in benchmarks)
        {
            // warm up (JIT + caches), result discarded
            TimeExecute(code, out _);

            var best = double.MaxValue;
            string result = null;
            for (var run = 0; run < 3; run++)
            {
                var ms = TimeExecute(code, out result);
                if (ms < best) best = ms;
            }

            total += best;
            Console.WriteLine($"{name,-34}{best,12:F2}{result,16}");
        }

        Console.WriteLine(new string('-', 64));
        Console.WriteLine($"{"total (best of each)",-34}{total,12:F2}");
    }

    // Times ONLY script execution; engine construction + native registration is
    // outside the stopwatch so the measurement reflects interpretation cost.
    private static double TimeExecute(string code, out string result)
    {
        var engine = new ScriptEngine();
        var loader = new EngineFunctionLoader();
        loader.RegisterFunctions(engine);
        engine.Root.AddChild("result", new ScriptVar(0));

        var sw = Stopwatch.StartNew();
        engine.Execute(code);
        sw.Stop();

        result = engine.Root.GetParameter("result").String;
        return sw.Elapsed.TotalMilliseconds;
    }
}
