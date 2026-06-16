/*
DScript performance benchmark.

This program uses ONLY the public ScriptEngine API, so it builds and runs on
both the old tree-walking engine and the new bytecode engine, letting you run
the exact same workloads against either version and compare.

How to compare old vs new:
  1. New (bytecode) engine — on this branch:
         dotnet run --project DScript.Benchmark -c Release
  2. Old (tree-walking) engine — on master, grab just this folder and run it:
         git checkout master
         git checkout feature/bytecode-vm -- DScript.Benchmark
         dotnet run --project DScript.Benchmark/DScript.Benchmark.csproj -c Release
         git checkout -- DScript.Benchmark   # (or delete it) to tidy up afterwards

Always build in Release. Pass a scale factor to size the loop workloads, e.g.
`dotnet run -c Release -- 0.25` if the old engine is too slow.

There are two sections:
  * "workloads" — Execute() timings, comparable across both engines.
  * "compile-once vs execute" — demonstrates the bytecode engine's ability to
    compile once and run many times. This uses Compile()/Run(), which only
    exist on the new engine; it is reached via reflection so the program still
    builds on master, where it simply reports the feature as unavailable.
*/

using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
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
        var arrayReps = (int)(400 * scale);
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

            ("array build+sum (x" + arrayReps + ")",
                $"var s = 0; for (var r = 0; r < {arrayReps}; r = r + 1) {{ var a = []; for (var i = 0; i < 500; i = i + 1) {{ a[i] = i; }} for (var i = 0; i < 500; i = i + 1) {{ s = s + a[i]; }} }} result = s;"),
        };

        Console.WriteLine($"DScript benchmark  (scale={scale.ToString(CultureInfo.InvariantCulture)}, .NET {System.Environment.Version})");
        Console.WriteLine(new string('-', 78));
        Console.WriteLine($"{"workload",-34}{"best ms",12}{"alloc MB",12}{"result",20}");
        Console.WriteLine(new string('-', 78));

        var total = 0.0;
        foreach (var (name, code) in benchmarks)
        {
            TimeExecute(code, out _, out _); // warm up

            // Five runs (was three): best-of smooths timing jitter, and the extra
            // runs make the allocation figure — which is far more stable than wall
            // time — easy to read for spotting GC-pressure regressions.
            var best = double.MaxValue;
            var bestAlloc = long.MaxValue;
            string result = null;
            for (var run = 0; run < 5; run++)
            {
                var ms = TimeExecute(code, out result, out var allocated);
                if (ms < best) best = ms;
                if (allocated < bestAlloc) bestAlloc = allocated;
            }

            total += best;
            var allocMb = bestAlloc / (1024.0 * 1024.0);
            Console.WriteLine($"{name,-34}{best,12:F2}{allocMb,12:F1}{result,20}");
        }

        Console.WriteLine(new string('-', 78));
        Console.WriteLine($"{"total (best of each)",-34}{total,12:F2}");

        CompileOnceDemo();
    }

    private static ScriptEngine NewEngine()
    {
        var engine = new ScriptEngine();
        new EngineFunctionLoader().RegisterFunctions(engine);
        engine.Root.AddChild("result", new ScriptVar(0));
        return engine;
    }

    // Times ONLY script execution; engine construction + native registration is
    // outside the stopwatch so the measurement reflects interpretation cost.
    private static double TimeExecute(string code, out string result, out long allocatedBytes)
    {
        var engine = NewEngine();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        engine.Execute(code);
        sw.Stop();
        allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        result = engine.Root.GetParameter("result").String;
        return sw.Elapsed.TotalMilliseconds;
    }

    // Demonstrates the bytecode engine's compile-once / run-many advantage:
    // running the same script repeatedly via Execute() recompiles it every time,
    // whereas Compile() once + Run() many skips that cost.
    private static void CompileOnceDemo()
    {
        Console.WriteLine();
        Console.WriteLine("compile-once vs execute (same script run many times)");
        Console.WriteLine(new string('-', 64));

        // Reached via reflection so this file still builds on the old engine,
        // which has no Compile/Run split.
        var compile = typeof(ScriptEngine).GetMethod(
            "Compile", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
        var run = compile == null ? null : typeof(ScriptEngine).GetMethod("Run", new[] { compile.ReturnType });

        if (compile == null || run == null)
        {
            Console.WriteLine("  not available on this engine (the tree-walking engine re-parses on");
            Console.WriteLine("  every Execute; there is no compile/run split to exploit).");
            return;
        }

        const string code = "function sq(x) { return x * x; } var s = 0; for (var i = 0; i < 30; i = i + 1) { s = s + sq(i); } result = s;";
        const int m = 5000;

        // Mode A: Execute (re-compiles every call)
        var engineA = NewEngine();
        engineA.Execute(code); // warm
        var swA = Stopwatch.StartNew();
        for (var i = 0; i < m; i++) engineA.Execute(code);
        swA.Stop();

        // Mode B: compile once, then Run repeatedly
        var engineB = NewEngine();
        var chunk = compile.Invoke(null, new object[] { code });
        run.Invoke(engineB, new[] { chunk }); // warm
        var swB = Stopwatch.StartNew();
        for (var i = 0; i < m; i++) run.Invoke(engineB, new[] { chunk });
        swB.Stop();

        var a = swA.Elapsed.TotalMilliseconds;
        var b = swB.Elapsed.TotalMilliseconds;

        Console.WriteLine($"  Execute x{m} (compiles each):  {a,9:F2} ms  ({a / m * 1000.0,7:F2} us/call)");
        Console.WriteLine($"  Compile once + Run x{m}:       {b,9:F2} ms  ({b / m * 1000.0,7:F2} us/call)");
        if (b > 0)
        {
            Console.WriteLine($"  -> {a / b:F2}x faster reusing compiled bytecode");
        }
        Console.WriteLine("  (Mode B includes per-call reflection overhead, so the real gain is larger.)");

        CompileThroughputDemo(compile);
    }

    // Measures raw compile throughput on a large, identifier-heavy script. This
    // is the path that exercises name interning (Chunk.AddName): a script with
    // thousands of distinct variables used to make interning O(n^2). Reached via
    // the same reflected Compile() handle so the file still builds on the old
    // engine (where it is simply skipped).
    private static void CompileThroughputDemo(MethodInfo compile)
    {
        if (compile == null) return;

        // Build a script with many distinct identifiers: var v0..vN, then a sum.
        const int vars = 2000;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < vars; i++)
        {
            sb.Append("var v").Append(i).Append(" = ").Append(i).Append("; ");
        }
        sb.Append("var total = 0; ");
        for (var i = 0; i < vars; i++)
        {
            sb.Append("total = total + v").Append(i).Append("; ");
        }
        sb.Append("result = total;");
        var bigCode = sb.ToString();

        const int reps = 200;

        compile.Invoke(null, new object[] { bigCode }); // warm (JIT + first compile)

        var best = double.MaxValue;
        for (var run = 0; run < 5; run++)
        {
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < reps; i++) compile.Invoke(null, new object[] { bigCode });
            sw.Stop();
            var ms = sw.Elapsed.TotalMilliseconds;
            if (ms < best) best = ms;
        }

        Console.WriteLine();
        Console.WriteLine($"compile throughput ({vars} distinct identifiers, x{reps})");
        Console.WriteLine(new string('-', 64));
        Console.WriteLine($"  best total: {best,9:F2} ms  ({best / reps,7:F3} ms/compile)");
    }
}
