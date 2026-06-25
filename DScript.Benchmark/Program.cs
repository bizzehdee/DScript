/*
DScript performance benchmark.

This program uses the public ScriptEngine API plus the DScriptCompiler
directly to compare optimised vs baseline (BinaryConst-fusion-only)
compilation. It builds and runs on both the tree-walking engine (master)
and the bytecode engine (this branch).

How to compare old vs new:
  1. New (bytecode) engine — on this branch:
         dotnet run --project DScript.Benchmark -c Release
  2. Old (tree-walking) engine — on master:
         git stash
         dotnet run --project DScript.Benchmark -c Release
         git stash pop

Always build in Release. Pass a scale factor, e.g. `-- 0.25`, if the
old engine is too slow for the full workload.

Sections
--------
  workloads               Execute() timings comparable across both engines.
  compile-once            Demonstrates compile-once / run-many advantage.
  optimizer impact        Optimised bytecode vs baseline (fusion only);
                          shows byte/instruction savings and speedup.
  optimisation candidates One workload per plan.md item (§1–§6); each
                          measured as average of 3 runs after 1 warm-up.
                          These are the baselines to beat once each
                          optimisation is implemented.
  bytecode showcase       Disassembly of key patterns before and after.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using DScript;
using DScript.Compiler;
using DScript.Extras;
using DScript.Jit;
using DScript.Vm;

internal static class Program
{
    private static void Main(string[] args)
    {
        var scale = 1.0;
        if (args.Length > 0 && double.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var s) && s > 0)
            scale = s;

        var loopN    = (int)(500_000 * scale);
        var callN    = (int)(200_000 * scale);
        var nested   = (int)(700    * scale);
        var stringN  = (int)(20_000 * scale);
        var arrayReps = (int)(400   * scale);
        var propN    = (int)(300_000 * scale);
        const int fibN = 27;

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

            ("property get/set (" + propN + ")",
                $"var o = {{ n: 0 }}; for (var i = 0; i < {propN}; i = i + 1) {{ o.n = o.n + 1; }} result = o.n;"),
        };

        Console.WriteLine($"DScript benchmark  (scale={scale.ToString(CultureInfo.InvariantCulture)}, .NET {System.Environment.Version})");
        Console.WriteLine(new string('-', 78));
        Console.WriteLine($"{"workload",-34}{"best ms",12}{"alloc MB",12}{"result",20}");
        Console.WriteLine(new string('-', 78));

        var total = 0.0;
        foreach (var (name, code) in benchmarks)
        {
            TimeExecute(code, out _, out _); // warm up

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
        OptimizerImpactSection(scale);
        OptimisationCandidatesSection(scale);
        JitSection(scale);
        BytecodeShowcase();
    }

    // -------------------------------------------------------------------------
    // helpers

    private static ScriptEngine NewEngine()
    {
        var engine = new ScriptEngine();
        new EngineFunctionLoader().RegisterFunctions(engine);
        engine.Root.AddChild("result", ScriptVar.FromInt(0));
        return engine;
    }

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

    // Return the best execution time over 5 repetitions of action().
    private static double BestOf5(Action action)
    {
        var best = double.MaxValue;
        for (var i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            var ms = sw.Elapsed.TotalMilliseconds;
            if (ms < best) best = ms;
        }
        return best;
    }

    // Total code bytes and instruction count across a chunk and all its nested
    // function bodies (uses the public Disassembler.OperandCount to walk sizes).
    private static (int bytes, int instructions) ChunkStats(Chunk chunk)
    {
        var bytes = chunk.Code.Count;
        var instr = 0;
        var offset = 0;
        while (offset < chunk.Code.Count)
        {
            var op = (OpCode)chunk.Code[offset];
            instr++;
            offset += 1 + 4 * Disassembler.OperandCount(op);
        }
        foreach (var fn in chunk.Functions)
        {
            var (fb, fi) = ChunkStats(fn);
            bytes += fb;
            instr += fi;
        }
        return (bytes, instr);
    }

    // Compile a program with the new optimizations disabled (baseline = BinaryConst
    // fusion only, matching pre-PR behaviour).
    private static Chunk CompileBaseline(string code)
    {
        using var c = new DScriptCompiler { EnableOptimizer = false };
        return c.CompileProgram(code);
    }

    // -------------------------------------------------------------------------
    // compile-once / run-many demo

    private static void CompileOnceDemo()
    {
        Console.WriteLine();
        Console.WriteLine("compile-once vs execute (same script run many times)");
        Console.WriteLine(new string('-', 64));

        var compile = typeof(ScriptEngine).GetMethod(
            "Compile", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
        var run = compile == null ? null : typeof(ScriptEngine).GetMethod("Run", new[] { compile.ReturnType });

        if (compile == null || run == null)
        {
            Console.WriteLine("  not available on this engine.");
            return;
        }

        const string code = "function sq(x) { return x * x; } var s = 0; for (var i = 0; i < 30; i = i + 1) { s = s + sq(i); } result = s;";
        const int m = 5000;

        var engineA = NewEngine();
        engineA.Execute(code);
        var swA = Stopwatch.StartNew();
        for (var i = 0; i < m; i++) engineA.Execute(code);
        swA.Stop();

        var engineB = NewEngine();
        var chunk = compile.Invoke(null, new object[] { code });
        run.Invoke(engineB, new[] { chunk });
        var swB = Stopwatch.StartNew();
        for (var i = 0; i < m; i++) run.Invoke(engineB, new[] { chunk });
        swB.Stop();

        var a = swA.Elapsed.TotalMilliseconds;
        var b = swB.Elapsed.TotalMilliseconds;

        Console.WriteLine($"  Execute x{m} (compiles each):  {a,9:F2} ms  ({a / m * 1000.0,7:F2} us/call)");
        Console.WriteLine($"  Compile once + Run x{m}:       {b,9:F2} ms  ({b / m * 1000.0,7:F2} us/call)");
        if (b > 0) Console.WriteLine($"  -> {a / b:F2}x faster reusing compiled bytecode");
        Console.WriteLine("  (Mode B includes per-call reflection overhead; real gain is larger.)");

        CompileThroughputDemo(compile);
    }

    private static void CompileThroughputDemo(MethodInfo compile)
    {
        if (compile == null) return;

        const int vars = 2000;
        var sb = new StringBuilder();
        for (var i = 0; i < vars; i++) sb.Append("var v").Append(i).Append(" = ").Append(i).Append("; ");
        sb.Append("var total = 0; ");
        for (var i = 0; i < vars; i++) sb.Append("total = total + v").Append(i).Append("; ");
        sb.Append("result = total;");
        var bigCode = sb.ToString();

        const int reps = 200;
        compile.Invoke(null, new object[] { bigCode });

        var best = double.MaxValue;
        for (var run = 0; run < 5; run++)
        {
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < reps; i++) compile.Invoke(null, new object[] { bigCode });
            sw.Stop();
            if (sw.Elapsed.TotalMilliseconds < best) best = sw.Elapsed.TotalMilliseconds;
        }

        Console.WriteLine();
        Console.WriteLine($"compile throughput ({vars} distinct identifiers, x{reps})");
        Console.WriteLine(new string('-', 64));
        Console.WriteLine($"  best total: {best,9:F2} ms  ({best / reps,7:F3} ms/compile)");
    }

    // -------------------------------------------------------------------------
    // optimizer impact section

    private static void OptimizerImpactSection(double scale)
    {
        // Scripts chosen to stress each of the four new optimisations:
        //   1. Constant folding:  inner-loop body uses pure constant arithmetic
        //   2. BinaryIntConst:    tight loop with integer literal in condition + step
        //   3. Jump-chain:        cascade of early-return ifs (classify function)
        //   4. Dead-code:         function bodies with code after return (same script)
        //
        // "baseline" = BinaryConst fusion only (pre-PR behaviour, EnableOptimizer=false)
        // "optimized" = all four passes enabled  (EnableOptimizer=true, the default)
        //
        // Both variants compile once; we time N repeated Run() calls so the timing
        // difference reflects only runtime cost of the bytecode, not compile time.

        var constLoopN = (int)(50_000 * scale);
        var intLoopN   = (int)(500_000 * scale);

        var constLoopReps = Math.Max(1, (int)(200 * scale));
        var intLoopReps   = Math.Max(1, (int)(5   * scale));
        var classifyReps  = Math.Max(1, (int)(5_000 * scale));

        var targets = new (string label, string code, int reps)[]
        {
            // constant folding: "3*7 + 2*5" collapses to a single Constant(31) so
            // each loop iteration saves 4 instruction dispatches vs the baseline.
            // BinaryIntConst also fires on the loop counter (i < N; i + 1).
            ($"constant in loop ({constLoopN}×{constLoopReps})",
                $"var s=0; for(var i=0;i<{constLoopN};i=i+1){{s=s+(3*7+2*5);}} result=s;",
                constLoopReps),

            // BinaryIntConst: i<N and i+1 embed the int value inline, skipping the
            // Constants[] array lookup + ConstantKind check on every iteration.
            ($"int tight loop ({intLoopN}×{intLoopReps})",
                $"var s=0; for(var i=0;i<{intLoopN};i=i+1){{s=s+i;}} result=s;",
                intLoopReps),

            // dead-code elimination: each early `return` in classify() leaves
            // unreachable bytes in the fused-only build; DCE removes them and the
            // trailing PushUndefined/Return after the final return.
            // BinaryIntConst fires on all the integer comparisons.
            ($"classify early-return (×{classifyReps})",
                "function classify(n){" +
                "if(n<0){return 0;}" +
                "if(n<10){return 1;}" +
                "if(n<100){return 2;}" +
                "return 3;}" +
                "var s=0;" +
                "for(var i=0;i<200;i=i+1){s=s+classify(i%100);}" +
                "result=s;",
                classifyReps),
        };

        Console.WriteLine();
        Console.WriteLine("peephole optimizer impact  (baseline = BinaryConst fusion only)");
        Console.WriteLine(new string('-', 82));
        Console.WriteLine($"{"workload",-34}  {"bytes",10}  {"instr",10}  {"base ms",9}  {"opt ms",9}  {"speedup",8}");
        Console.WriteLine($"{"",34}  {"base→opt",10}  {"base→opt",10}");
        Console.WriteLine(new string('-', 82));

        foreach (var (label, code, reps) in targets)
        {
            if (reps < 1) { Console.WriteLine($"{label,-34}  (skipped — scale too small)"); continue; }

            var baseChunk = CompileBaseline(code);
            var optChunk  = ScriptEngine.Compile(code);

            var (baseBytes, baseInstr) = ChunkStats(baseChunk);
            var (optBytes,  optInstr)  = ChunkStats(optChunk);

            var engineBase = NewEngine();
            engineBase.Run(baseChunk); // warm
            var baseMs = BestOf5(() => { for (var i = 0; i < reps; i++) engineBase.Run(baseChunk); });

            var engineOpt = NewEngine();
            engineOpt.Run(optChunk); // warm
            var optMs = BestOf5(() => { for (var i = 0; i < reps; i++) engineOpt.Run(optChunk); });

            var speedup = baseMs / Math.Max(optMs, 0.001);
            var byteCol  = $"{baseBytes}→{optBytes}";
            var instrCol = $"{baseInstr}→{optInstr}";

            Console.WriteLine($"{label,-34}  {byteCol,10}  {instrCol,10}  {baseMs,9:F1}  {optMs,9:F1}  {speedup,7:F2}x");
        }

        Console.WriteLine(new string('-', 82));
    }

    // -------------------------------------------------------------------------
    // JIT section — interpreter vs the two JIT back-ends on hot workloads.
    //
    // Each workload calls a small pure (or property-reading) function far past the
    // invocation threshold so the chunk is compiled and most calls run compiled.
    // We measure best-of-5 total Execute() time under: no JIT (interpreter), the
    // Reflection.Emit back-end, and the closure-threaded back-end — and report the
    // speedup of each over the interpreter. Results are compared so a mismatch
    // (a correctness bug) is flagged inline.

    private static void JitSection(double scale)
    {
        var intN  = (int)(2_000_000 * scale);
        var dblN  = (int)(2_000_000 * scale);
        var propN = (int)(2_000_000 * scale);

        var workloads = new (string label, string code)[]
        {
            // Speculative unboxed-int tier: pure integer arithmetic.
            ($"int poly (n={intN})",
                $"function poly(a,b){{return a*a + b*b - a*b;}} var s=0; " +
                $"for(var i=0;i<{intN};i=i+1){{s=poly(i, i+1);}} result=s;"),

            // Speculative unboxed-double tier: pure floating-point arithmetic.
            ($"double poly (n={dblN})",
                $"function poly(a,b){{return a*b + a/b - a;}} var s=0; " +
                $"for(var i=0;i<{dblN};i=i+1){{s=poly(i+0.5, 2.5);}} result=s;"),

            // Conservative tier + per-site inline cache: repeated property reads.
            ($"property read (n={propN})",
                $"function get(o){{return o.x + o.y;}} var o={{x:3,y:4}}; var s=0; " +
                $"for(var i=0;i<{propN};i=i+1){{s=get(o);}} result=s;"),

            // Control flow + monomorphic inlining: a JIT-compiled loop body calls a
            // pure-parameter leaf (sq), which is inlined (no per-call frame allocation).
            ($"inlined helper loop (n={propN})",
                $"function sq(x){{return x*x;}} " +
                $"function run(m){{ var s=0; var j=0; while(j<m){{ s=s+sq(j); j=j+1; }} return s; }} " +
                $"var t=0; for(var i=0;i<{propN};i=i+1){{ t=run(20); }} result=t;"),

            // Bimorphic property cache: one read site sees two objects each iteration;
            // the 2-way inline cache keeps both warm (a monomorphic cache would thrash).
            ($"bimorphic prop read (n={propN})",
                $"function get(o){{return o.x;}} var a={{x:3,y:9}}; var b={{x:4}}; var s=0; " +
                $"for(var i=0;i<{propN};i=i+1){{s=get(a)+get(b);}} result=s;"),

            // Control flow: a JIT-compiled function with branches + a loop + an
            // accumulator (if/else inside a while), exercising Phase 7/8 codegen.
            ($"control-flow loop (n={propN})",
                $"function classify(n){{ var s=0; var k=0; while(k<n){{ if(k<10){{s=s+1;}} else if(k<50){{s=s+2;}} else {{s=s+3;}} k=k+1; }} return s; }} " +
                $"var t=0; for(var i=0;i<{propN};i=i+1){{ t=classify(60); }} result=t;"),
        };

        Console.WriteLine();
        Console.WriteLine($"JIT back-ends vs interpreter  (best of 5, scale={scale.ToString(CultureInfo.InvariantCulture)})");
        Console.WriteLine(new string('-', 88));
        Console.WriteLine($"{"workload",-26}{"interp ms",11}{"ReflEmit ms",13}{"speedup",9}{"Closure ms",12}{"speedup",9}");
        Console.WriteLine(new string('-', 88));

        foreach (var (label, code) in workloads)
        {
            var (interpMs, interpRes) = BestExecute(code, null);
            var (remitMs,  remitRes)  = BestExecute(code, new ReflectionEmitJitCompiler());
            var (closeMs,  closeRes)  = BestExecute(code, new ClosureThreadedJitCompiler());

            var remitUp = interpMs / Math.Max(remitMs, 0.001);
            var closeUp = interpMs / Math.Max(closeMs, 0.001);

            Console.WriteLine($"{label,-26}{interpMs,11:F1}{remitMs,13:F1}{remitUp,8:F2}x{closeMs,12:F1}{closeUp,8:F2}x");

            if (remitRes != interpRes || closeRes != interpRes)
                Console.WriteLine($"  !! RESULT MISMATCH  interp={interpRes}  reflEmit={remitRes}  closure={closeRes}");
        }

        Console.WriteLine(new string('-', 88));
        Console.WriteLine("  (JIT is opt-in via JitRegistry; interpreter-only path is unchanged when no compiler is registered.)");
    }

    // Best-of-5 total Execute() time for code under the given JIT back-end (null =
    // interpreter). Includes one warm-up run (which also compiles the hot chunk).
    private static (double ms, string result) BestExecute(string code, IJitCompiler compiler)
    {
        if (compiler != null) JitRegistry.Register(compiler);
        else JitRegistry.Clear();
        try
        {
            TimeExecute(code, out _, out _); // warm up (also triggers JIT compilation)

            var best = double.MaxValue;
            string result = null;
            for (var run = 0; run < 5; run++)
            {
                var ms = TimeExecute(code, out result, out _);
                if (ms < best) best = ms;
            }
            return (best, result);
        }
        finally
        {
            JitRegistry.Clear();
        }
    }

    // -------------------------------------------------------------------------
    // bytecode showcase — show raw vs optimised disassembly for key examples

    private static void BytecodeShowcase()
    {
        var examples = new (string title, string code)[]
        {
            // 1. Pure constant arithmetic — the entire expression collapses to one push
            ("constant arithmetic: result = 2 * 10 + 3 * 4 - 1",
             "result = 2 * 10 + 3 * 4 - 1;"),

            // 2. Dead code after return — everything after `return x` is unreachable;
            //    DCE strips it entirely (dramatic byte reduction for real functions).
            ("dead code after return (identity function)",
             "function identity(x){" +
             "return x;" +
             "var garbage=0; var more=1; var extra=2;" +   // unreachable
             "return garbage+more+extra;" +                // unreachable
             "}" +
             "result=identity(42);"),

            // 3. BinaryIntConst in tight loop — comparisons and counter increment
            //    use integer literals; baseline uses pool-indexed BinaryConst instead.
            ("BinaryIntConst: classify early-return body",
             "function classify(n){" +
             "if(n<0){return 0;}" +
             "if(n<10){return 1;}" +
             "return 2;" +
             "}" +
             "result=classify(5);"),
        };

        Console.WriteLine();
        Console.WriteLine("bytecode showcase  (baseline vs optimized disassembly)");

        foreach (var (title, code) in examples)
        {
            Console.WriteLine();
            Console.WriteLine($"  {title}");
            Console.WriteLine(new string(' ', 4) + new string('-', 70));

            var baseChunk = CompileBaseline(code);
            var optChunk  = ScriptEngine.Compile(code);

            var (baseBytes, baseInstr) = ChunkStats(baseChunk);
            var (optBytes,  optInstr)  = ChunkStats(optChunk);

            Console.WriteLine($"  baseline   {baseBytes,4}B  {baseInstr,3} instr");
            PrintDisassembly("    ", baseChunk);
            Console.WriteLine($"  optimized  {optBytes,4}B  {optInstr,3} instr");
            PrintDisassembly("    ", optChunk);
        }
    }

    // Print a chunk's disassembly, indented by prefix. Recurses into Functions.
    private static void PrintDisassembly(string prefix, Chunk chunk)
    {
        var raw = Disassembler.Disassemble(chunk);
        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            if (trimmed.Length > 0)
                Console.WriteLine(prefix + trimmed);
        }
    }

    // -------------------------------------------------------------------------
    // Average-of-3 helper (1 warm-up run discarded)

    private static (double avgMs, double avgAllocMb, string result) AverageOf3(string code)
    {
        TimeExecute(code, out _, out _); // discard warm-up

        var totalMs    = 0.0;
        var totalAlloc = 0L;
        string result  = null;
        for (var run = 0; run < 3; run++)
        {
            var ms = TimeExecute(code, out result, out var alloc);
            totalMs    += ms;
            totalAlloc += alloc;
        }
        return (totalMs / 3.0, totalAlloc / 3.0 / (1024.0 * 1024.0), result);
    }

    // -------------------------------------------------------------------------
    // Optimisation candidates — one workload per plan.md §1–§6.
    // Measured as average of 3 runs after 1 warm-up so variance is visible.
    // Run this section before and after each optimisation is implemented to
    // verify improvement.

    private static void OptimisationCandidatesSection(double scale)
    {
        // Scale the inner repetition counts so each workload takes a
        // measurable but not excessive time at the default scale=1.
        var compoundN   = (int)(500_000 * scale);   // §1 compound assign
        var propStressN = (int)(200_000 * scale);   // §2 GetProp cache
        var spreadN     = (int)(Math.Max(10,  100 * scale));  // §3 spread array size
        var spreadReps  = (int)(Math.Max(1,   300 * scale));  // §3 spread reps
        var forOfN      = (int)(Math.Max(10, 1000 * scale));  // §4 for..of array size
        var forOfReps   = (int)(Math.Max(1,   500 * scale));  // §4 for..of reps
        var litReps     = (int)(Math.Max(1, 30_000 * scale)); // §5 literal spread reps
        var genN        = (int)(Math.Max(10,  300 * scale));  // §6 generator range size
        var genReps     = (int)(Math.Max(1,    50 * scale));  // §6 generator reps
        var methodN     = (int)(300_000 * scale);             // §7 named method calls

        var candidates = new (string label, string code)[]
        {
            // §1 — Compound-assignment BinaryIntConst peephole
            //      i++ and s+=i bypass TryUpgradeBinaryConstToInt today;
            //      every iteration pays a pool-lookup + kind-check overhead.
            ($"§1 compound assign i++ (n={compoundN})",
                $"var s=0; for(var i=0;i<{compoundN};i++){{s+=i;}} result=s;"),

            // §2 — GetProp cache hash improvement
            //      8 properties whose name-index low bytes may alias under
            //      the current & 0xFF mapping, causing repeated evictions.
            ($"§2 getprop cache 8-key stress (n={propStressN})",
                $"var o={{aa:1,bb:2,cc:3,dd:4,ee:5,ff:6,gg:7,hh:8}};" +
                $"var s=0;" +
                $"for(var i=0;i<{propStressN};i++){{" +
                $"  s=s+o.aa+o.bb+o.cc+o.dd+o.ee+o.ff+o.gg+o.hh;" +
                $"}}" +
                $"result=s;"),

            // §3 — Spread single-pass helper
            //      [...a,...a] walks the linked list O(n) times per element
            //      today, making the total cost O(n²); ExtractArrayElements
            //      reduces it to O(n).
            ($"§3 spread array n={spreadN} ×{spreadReps}",
                $"var a=[];" +
                $"for(var i=0;i<{spreadN};i++){{a[i]=i;}}" +
                $"var s=0;" +
                $"for(var r=0;r<{spreadReps};r++){{var b=[...a,...a];s=s+b.length;}}" +
                $"result=s;"),

            // §4 — for..of ForOfStep fused opcode
            //      Each iteration currently dispatches 5+ opcodes
            //      (GetVar/GetProp next/Call/GetProp done/GetProp value).
            ($"§4 for..of array n={forOfN} ×{forOfReps}",
                $"var arr=[];" +
                $"for(var i=0;i<{forOfN};i++){{arr[i]=i;}}" +
                $"var s=0;" +
                $"for(var r=0;r<{forOfReps};r++){{for(var x of arr){{s+=x;}}}}" +
                $"result=s;"),

            // §5 — Array literal spread: eliminate double-parse + per-element GetProp length
            //      Each array literal with a spread clones the lexer for a
            //      pre-scan today; runtime also re-reads .length per static
            //      element following a spread.
            ($"§5 array literal spread (×{litReps})",
                $"var a=[1,2,3,4,5];var b=[6,7,8,9,10];" +
                $"var s=0;" +
                $"for(var r=0;r<{litReps};r++){{" +
                $"  var c=[...a,11,12,...b,13,14];" +
                $"  s=s+c.length;" +
                $"}}" +
                $"result=s;"),

            // §6 — Stackless generator state machine
            //      Each invocation of range() spawns an OS thread + two
            //      SemaphoreSlims today; every yield costs a signal+wait pair.
            //      The stackless path eliminates all thread overhead.
            ($"§6 generator for..of n={genN} ×{genReps}",
                $"function* range(n){{var i=0;while(i<n){{yield i;i++;}}}}" +
                $"var s=0;" +
                $"for(var r=0;r<{genReps};r++){{" +
                $"  for(var v of range({genN})){{s+=v;}}" +
                $"}}" +
                $"result=s;"),

            // §7 — Named method call: GetPropMethod/GetPropCall0 superinstruction
            //      Previously emitted Dup+GetProp (2 opcodes) before every method
            //      call; GetPropMethod folds the Dup into the property lookup.
            //      GetPropCall0 further eliminates the CallMethod 0 opcode for
            //      zero-argument calls, saving 5 bytes + 1 dispatch each.
            ($"§7 named method calls (n={methodN})",
                $"var v=0;" +
                $"var o={{inc:function(){{v=v+1;}},get:function(){{return v;}}}};" +
                $"for(var i=0;i<{methodN};i++){{o.inc();}}" +
                $"result=o.get();"),
        };

        Console.WriteLine();
        Console.WriteLine($"optimisation candidates  (average of 3 runs, scale={scale.ToString(CultureInfo.InvariantCulture)})");
        Console.WriteLine(new string('-', 76));
        Console.WriteLine($"{"workload",-42}{"avg ms",10}{"alloc MB",10}{"result",14}");
        Console.WriteLine(new string('-', 76));

        var sectionTotal = 0.0;
        foreach (var (label, code) in candidates)
        {
            var (avgMs, avgAllocMb, result) = AverageOf3(code);
            sectionTotal += avgMs;
            Console.WriteLine($"{label,-42}{avgMs,10:F2}{avgAllocMb,10:F1}{result,14}");
        }

        Console.WriteLine(new string('-', 76));
        Console.WriteLine($"{"total",-42}{sectionTotal,10:F2}");
    }
}
