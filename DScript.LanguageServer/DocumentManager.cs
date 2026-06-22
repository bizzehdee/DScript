using System.Text.RegularExpressions;
using DScript;
using DScript.Vm;

namespace DScript.LanguageServer;

/// <summary>
/// Tracks open documents, recompiles them on change, and provides
/// symbol information for hover / go-to-definition / completion.
/// </summary>
sealed class DocumentManager
{
    private readonly Dictionary<string, OpenDocument> _docs = new(StringComparer.Ordinal);
    private readonly Stream _out;

    public DocumentManager(Stream @out) => _out = @out;

    public void Open(string uri, string text)
    {
        var doc = new OpenDocument(uri, text);
        _docs[uri] = doc;
        Recompile(doc);
        Publish(doc);
    }

    public void Change(string uri, string newText)
    {
        if (!_docs.TryGetValue(uri, out var doc))
        {
            doc = new OpenDocument(uri, newText);
            _docs[uri] = doc;
        }
        else
        {
            doc.Text = newText;
        }
        Recompile(doc);
        Publish(doc);
    }

    public void Close(string uri) => _docs.Remove(uri);

    public OpenDocument? Get(string uri) =>
        _docs.TryGetValue(uri, out var d) ? d : null;

    // ── Compilation ─────────────────────────────────────────────────────────

    private static void Recompile(OpenDocument doc)
    {
        doc.Diagnostics.Clear();
        doc.CompiledChunk = null;
        doc.Symbols.Clear();

        try
        {
            using var compiler = new DScript.Compiler.DScriptCompiler();
            doc.CompiledChunk = compiler.CompileProgram(doc.Text);
            doc.Symbols = ExtractSymbols(doc.Text, doc.CompiledChunk);
        }
        catch (ScriptException ex)
        {
            // The exception message often contains "at line N" information.
            var (line, col) = ParseErrorLocation(ex.Message);
            var range = MakeRange(line, col);
            doc.Diagnostics.Add(new Diagnostic(range, 1, ex.Message));
        }
        catch (Exception ex)
        {
            doc.Diagnostics.Add(new Diagnostic(MakeRange(0, 0), 1, ex.Message));
        }
    }

    private static (int line, int col) ParseErrorLocation(string message)
    {
        // Look for patterns like "(line 5)" or "line 5, col 10"
        var m = Regex.Match(message, @"[Ll]ine\s+(\d+)(?:[,\s]+[Cc]ol(?:umn)?\s+(\d+))?");
        if (m.Success)
        {
            var line = int.Parse(m.Groups[1].Value) - 1; // LSP is 0-based
            var col = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) - 1 : 0;
            return (Math.Max(0, line), Math.Max(0, col));
        }
        return (0, 0);
    }

    private static Range MakeRange(int line, int col)
    {
        var pos = new Position(line, col);
        return new Range(pos, new Position(line, col + 1));
    }

    // ── Symbol extraction ────────────────────────────────────────────────────

    /// <summary>
    /// Scan the source text with a simple regex pass to collect declaration
    /// sites. We also walk the chunk's Names table to add any names the lexer
    /// found that we might have missed (e.g. function parameters).
    /// </summary>
    private static List<SymbolInfo> ExtractSymbols(string source, Chunk? chunk)
    {
        var symbols = new List<SymbolInfo>();
        if (source == null) return symbols;

        var lines = source.Split('\n');

        // Regex-based scan for var / let / const / function declarations.
        var declPattern = new Regex(
            @"(?:^|[;\n{])\s*(?:var|let|const)\s+(\w+)|function\s+(\w+)\s*\(",
            RegexOptions.Multiline);

        foreach (Match m in declPattern.Matches(source))
        {
            string name;
            SymbolKind kind;
            if (m.Groups[1].Success)
            {
                name = m.Groups[1].Value;
                kind = SymbolKind.Variable;
            }
            else
            {
                name = m.Groups[2].Value;
                kind = SymbolKind.Function;
            }

            var charPos = m.Index + m.Value.Length - name.Length;
            GetLineCol(source, charPos, out var ln, out var col);
            symbols.Add(new SymbolInfo(name, ln, col, kind));
        }

        // Also pull any names from the chunk that aren't already listed.
        if (chunk != null)
        {
            var existing = new HashSet<string>(symbols.Select(s => s.Name), StringComparer.Ordinal);
            foreach (var name in chunk.Names)
            {
                if (!existing.Contains(name))
                {
                    symbols.Add(new SymbolInfo(name, 0, 0, SymbolKind.Variable));
                    existing.Add(name);
                }
            }

            // Recurse into function chunks for their parameters and names.
            foreach (var fn in chunk.Functions)
            {
                foreach (var param in fn.Parameters)
                {
                    if (!existing.Contains(param))
                    {
                        symbols.Add(new SymbolInfo(param, 0, 0, SymbolKind.Parameter));
                        existing.Add(param);
                    }
                }
                foreach (var name in fn.Names)
                {
                    if (!existing.Contains(name))
                    {
                        symbols.Add(new SymbolInfo(name, 0, 0, SymbolKind.Variable));
                        existing.Add(name);
                    }
                }
            }
        }

        return symbols;
    }

    private static void GetLineCol(string source, int charOffset, out int line, out int col)
    {
        line = 0;
        col = 0;
        for (var i = 0; i < charOffset && i < source.Length; i++)
        {
            if (source[i] == '\n') { line++; col = 0; }
            else col++;
        }
    }

    // ── Publish diagnostics ──────────────────────────────────────────────────

    private void Publish(OpenDocument doc)
    {
        JsonRpc.WriteNotification(_out, "textDocument/publishDiagnostics", new
        {
            uri = doc.Uri,
            diagnostics = doc.Diagnostics.Select(d => new
            {
                range = new
                {
                    start = new { line = d.Range.Start.Line, character = d.Range.Start.Character },
                    end   = new { line = d.Range.End.Line,   character = d.Range.End.Character },
                },
                severity = d.Severity,
                source   = d.Source,
                message  = d.Message,
            }).ToArray(),
        });
    }
}

/// <summary>State for a single open document.</summary>
sealed class OpenDocument
{
    public string Uri { get; }
    public string Text { get; set; }
    public Chunk? CompiledChunk { get; set; }
    public List<Diagnostic> Diagnostics { get; set; } = [];
    public List<SymbolInfo> Symbols { get; set; } = [];

    public OpenDocument(string uri, string text)
    {
        Uri = uri;
        Text = text;
    }
}
