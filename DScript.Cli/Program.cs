/*
DScript CLI — run, profile, check, and REPL for .ds files.

Usage:
  dscript <script.ds>                     Run a script file
  dscript run <script.ds>                 Run a script file (explicit verb)
  dscript profile <script.ds>             Profile a script; write output.cpuprofile
  dscript profile -o out.cpuprofile <..>  Profile with a custom output path
  dscript check <script.ds>              Syntax-check only (no execution)
  dscript repl                            Start an interactive REPL
  dscript --help / -h                     Show this message
  dscript --version / -v                  Show the version
*/

using DScript;
using DScript.Compiler;
using DScript.Extras;
using DScript.Jit;
using DScript.Profiler;
using DScript.Vm;

const string Version = "0.1.0";

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    PrintHelp();
    return 0;
}

if (args[0] is "--version" or "-v")
{
    Console.WriteLine($"dscript {Version}");
    return 0;
}

return args[0] switch
{
    "run"     => RunScript(args[1..]),
    "profile" => ProfileScript(args[1..]),
    "check"   => CheckScript(args[1..]),
    "repl"    => RunRepl(),
    _         => RunScript(args),  // bare path: dscript script.ds [args...]
};

// ── verbs ─────────────────────────────────────────────────────────────────────

static int RunScript(string[] args)
{
    if (args.Length == 0) return Fail("run: no script file specified.");
    var path = args[0];
    if (!File.Exists(path)) return Fail($"run: file not found: {path}");

    var source = File.ReadAllText(path);
    var engine = MakeEngine(path, args[1..]);
    try
    {
        engine.Run(ScriptEngine.Compile(source));
        return 0;
    }
    catch (Exception ex)
    {
        return Fail(ex.Message);
    }
}

static int ProfileScript(string[] args)
{
    // Parse: [-o <outfile>] <script.ds>
    string? outPath = null;
    var rest = new List<string>(args);

    var oIdx = rest.IndexOf("-o");
    if (oIdx >= 0 && oIdx + 1 < rest.Count)
    {
        outPath = rest[oIdx + 1];
        rest.RemoveRange(oIdx, 2);
    }

    if (rest.Count == 0) return Fail("profile: no script file specified.");
    var path = rest[0];
    if (!File.Exists(path)) return Fail($"profile: file not found: {path}");

    outPath ??= Path.ChangeExtension(path, ".cpuprofile");

    var source = File.ReadAllText(path);
    var profiler = new CpuProfiler();
    var engine   = MakeEngine(path, rest.Count > 1 ? [.. rest[1..]] : []);
    engine.AttachProfiler(profiler);

    try
    {
        engine.Run(ScriptEngine.Compile(source));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        // Still write the profile — partial data is better than none.
    }

    var json = profiler.GetProfile();
    File.WriteAllText(outPath, json);
    Console.WriteLine($"Profile written to {outPath}");
    Console.WriteLine("Open in VS Code: Ctrl+Shift+P → Developer: Open CPU Profile");
    return 0;
}

static int CheckScript(string[] args)
{
    if (args.Length == 0) return Fail("check: no script file specified.");
    var path = args[0];
    if (!File.Exists(path)) return Fail($"check: file not found: {path}");

    var source = File.ReadAllText(path);
    try
    {
        using var compiler = new DScriptCompiler();
        compiler.CompileProgram(source);
        Console.WriteLine($"{path}: OK");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{path}: {ex.Message}");
        return 1;
    }
}

static int RunRepl()
{
    // Set both input and output to UTF-8, matching what Node.js does on Windows.
    // Without this, the OEM code page (e.g. 437) mangles emoji pasted into the REPL.
    Console.InputEncoding  = System.Text.Encoding.UTF8;
    Console.OutputEncoding = System.Text.Encoding.UTF8;

    Console.WriteLine($"DScript {Version} REPL  (type .exit to quit, .help for commands)");
    Console.WriteLine();

    var engine = MakeEngine("<repl>", []);

    // JIT is opt-in engine-wide; the REPL turns it on by default (reflection-emit
    // back-end) so hot functions tier up. Toggle via `.options jit ...`.
    JitRegistry.Register(new ReflectionEmitJitCompiler());

    while (true)
    {
        Console.Write("> ");
        var line = Console.ReadLine();

        if (line == null) break;   // Ctrl-D / EOF
        line = line.Trim();
        if (line.Length == 0) continue;

        switch (line)
        {
            case ".exit":
            case ".quit":
                goto done;
            case ".help":
                Console.WriteLine("  .exit / .quit          — quit the REPL");
                Console.WriteLine("  .help                  — show this message");
                Console.WriteLine("  .options                    — show current options");
                Console.WriteLine("  .options jit <value>        — on | off | reflection | closure  (default: reflection)");
                Console.WriteLine("  .options optimisation <v>   — on | off  (compiler optimiser; default: on)");
                Console.WriteLine("  Any DScript statement or expression is evaluated.");
                Console.WriteLine("  State persists across inputs.");
                continue;
        }

        if (line == ".options" || line.StartsWith(".options ", StringComparison.Ordinal))
        {
            HandleOptionsCommand(engine, line);
            continue;
        }

        var isDeclaration = line.StartsWith("var ",      StringComparison.Ordinal)
                         || line.StartsWith("let ",      StringComparison.Ordinal)
                         || line.StartsWith("const ",    StringComparison.Ordinal)
                         || line.StartsWith("function ", StringComparison.Ordinal)
                         || line.StartsWith("class ",    StringComparison.Ordinal)
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
    return 0;
}

// ── helpers ───────────────────────────────────────────────────────────────────

// `.options [jit <value>] [optimisation <value>]` — inspect or change REPL
// options. `jit` selects the JIT back-end (or off); `optimisation` toggles the
// compiler's bytecode optimiser. Both default on for the REPL.
static void HandleOptionsCommand(ScriptEngine engine, string line)
{
    var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    if (parts.Length == 1) // ".options" — show all
    {
        Console.WriteLine($"  jit          = {DescribeJit()}");
        Console.WriteLine($"  optimisation = {(engine.EnableOptimizer ? "on" : "off")}");
        return;
    }

    switch (parts[1])
    {
        case "jit":
            HandleJitOption(parts);
            break;
        case "optimisation":
        case "optimization":
            HandleOptimisationOption(engine, parts);
            break;
        default:
            Console.WriteLine($"Unknown option '{parts[1]}'. Known options: jit, optimisation");
            break;
    }
}

static void HandleJitOption(string[] parts)
{
    if (parts.Length < 3) // ".options jit" — show + usage
    {
        Console.WriteLine($"  jit = {DescribeJit()}");
        Console.WriteLine("  usage: .options jit <on|off|reflection|closure>");
        return;
    }

    switch (parts[2].ToLowerInvariant())
    {
        case "off":
        case "none":
            JitRegistry.Clear();
            Console.WriteLine("jit: off");
            break;
        case "on":          // default back-end
        case "reflection":
        case "reflect":
        case "il":
            JitRegistry.Register(new ReflectionEmitJitCompiler());
            Console.WriteLine("jit: on (reflection-emit back-end) — hot functions will tier up");
            break;
        case "closure":
        case "closurethreaded":
            JitRegistry.Register(new ClosureThreadedJitCompiler());
            Console.WriteLine("jit: on (closure-threaded back-end) — hot functions will tier up");
            break;
        default:
            Console.WriteLine($"Unknown jit value '{parts[2]}'. Use: on | off | reflection | closure");
            break;
    }
}

static void HandleOptimisationOption(ScriptEngine engine, string[] parts)
{
    if (parts.Length < 3)
    {
        Console.WriteLine($"  optimisation = {(engine.EnableOptimizer ? "on" : "off")}");
        Console.WriteLine("  usage: .options optimisation <on|off>");
        return;
    }

    switch (parts[2].ToLowerInvariant())
    {
        case "on":
        case "true":
            engine.EnableOptimizer = true;
            Console.WriteLine("optimisation: on");
            break;
        case "off":
        case "false":
        case "none":
            engine.EnableOptimizer = false;
            Console.WriteLine("optimisation: off");
            break;
        default:
            Console.WriteLine($"Unknown optimisation value '{parts[2]}'. Use: on | off");
            break;
    }
}

static string DescribeJit() => JitRegistry.Current switch
{
    null                        => "off",
    ReflectionEmitJitCompiler   => "on (reflection-emit back-end)",
    ClosureThreadedJitCompiler  => "on (closure-threaded back-end)",
    var c                       => "on (" + c.GetType().Name + ")",
};

static ScriptEngine MakeEngine(string scriptPath, string[] scriptArgs)
{
    var engine = new ScriptEngine();
    new EngineFunctionLoader().RegisterFunctions(engine);

    // Expose __filename and __dirname globals
    var absPath = Path.GetFullPath(scriptPath);
    engine.Root.FindChildOrCreate("__filename").ReplaceWith(ScriptVar.FromString(absPath));
    engine.Root.FindChildOrCreate("__dirname").ReplaceWith(
        ScriptVar.FromString(Path.GetDirectoryName(absPath) ?? "."));

    // Expose process.argv
    var argv = ScriptVar.CreateArray();
    argv.SetArrayIndex(0, ScriptVar.FromString("dscript"));
    argv.SetArrayIndex(1, ScriptVar.FromString(absPath));
    for (var i = 0; i < scriptArgs.Length; i++)
        argv.SetArrayIndex(i + 2, ScriptVar.FromString(scriptArgs[i]));
    var process = ScriptVar.CreateObject();
    process.AddChild("argv", argv);
    engine.Root.FindChildOrCreate("process").ReplaceWith(process);

    return engine;
}

static int Fail(string message)
{
    Console.Error.WriteLine($"dscript: {message}");
    Console.Error.WriteLine("Run 'dscript --help' for usage.");
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine($"dscript {Version} — DScript command-line runner");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dscript <script.ds> [args...]        Run a script");
    Console.WriteLine("  dscript run <script.ds> [args...]    Run a script (explicit verb)");
    Console.WriteLine("  dscript profile [-o out] <script.ds> Run and produce a CPU profile");
    Console.WriteLine("  dscript check <script.ds>            Syntax-check without running");
    Console.WriteLine("  dscript repl                         Start an interactive REPL");
    Console.WriteLine("  dscript --help / -h                  Show this message");
    Console.WriteLine("  dscript --version / -v               Show version");
    Console.WriteLine();
    Console.WriteLine("Profile output:");
    Console.WriteLine("  Writes a .cpuprofile file (V8 format). Open in VS Code with");
    Console.WriteLine("  Ctrl+Shift+P → Developer: Open CPU Profile, or load in Chrome DevTools.");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dscript hello.ds");
    Console.WriteLine("  dscript run server.ds --port 8080");
    Console.WriteLine("  dscript profile -o bench.cpuprofile fib.ds");
    Console.WriteLine("  dscript check *.ds");
    Console.WriteLine("  dscript repl");
}
