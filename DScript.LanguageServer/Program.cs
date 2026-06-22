using DScript.LanguageServer;

// Redirect stderr to a log file so it doesn't pollute the LSP stdio channel.
// (VS Code shows this in the "Output" panel for the extension.)
try
{
    var logDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DScript");
    Directory.CreateDirectory(logDir);
    var logPath = Path.Combine(logDir, "language-server.log");
    var logWriter = new StreamWriter(logPath, append: false) { AutoFlush = true };
    Console.SetError(logWriter);
}
catch
{
    // If we can't open the log file, just leave stderr as-is.
}

Console.Error.WriteLine($"[DScript LSP] starting at {DateTime.UtcNow:u}");

var server = new LspServer(Console.OpenStandardInput(), Console.OpenStandardOutput());
server.Run();
