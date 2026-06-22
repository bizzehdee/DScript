using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DScript.LanguageServer;

/// <summary>
/// Main LSP server loop. Reads JSON-RPC messages from stdin, dispatches to
/// handlers, and sends responses back on stdout.
/// </summary>
sealed class LspServer
{
    private readonly Stream _in;
    private readonly Stream _out;
    private readonly DocumentManager _docs;
    public LspServer(Stream @in, Stream @out)
    {
        _in = @in;
        _out = @out;
        _docs = new DocumentManager(@out);
    }

    public void Run()
    {
        while (true)
        {
            RpcMessage? msg;
            try { msg = JsonRpc.Read(_in); }
            catch { break; }

            if (msg == null) break;
            HandleMessage(msg.Value);
        }
    }

    // ── Dispatcher ───────────────────────────────────────────────────────────

    private void HandleMessage(RpcMessage msg)
    {
        try
        {
            switch (msg.Method)
            {
                case "initialize":           HandleInitialize(msg);       break;
                case "initialized":                                        break;
                case "shutdown":             HandleShutdown(msg);         break;
                case "exit":                 Environment.Exit(0);         break;
                case "textDocument/didOpen":   HandleDidOpen(msg);        break;
                case "textDocument/didChange": HandleDidChange(msg);      break;
                case "textDocument/didClose":  HandleDidClose(msg);       break;
                case "textDocument/hover":       HandleHover(msg);        break;
                case "textDocument/definition":  HandleDefinition(msg);   break;
                case "textDocument/completion":  HandleCompletion(msg);   break;
                case "textDocument/signatureHelp": HandleSignatureHelp(msg); break;
                default:
                    // Unknown request — return null result so client doesn't hang.
                    if (msg.Id.HasValue)
                        JsonRpc.Write(_out, msg.Id.Value, null);
                    break;
            }
        }
        catch (Exception ex)
        {
            if (msg.Id.HasValue)
                JsonRpc.WriteError(_out, msg.Id.Value, -32603, ex.Message);
        }
    }

    // ── initialize ───────────────────────────────────────────────────────────

    private void HandleInitialize(RpcMessage msg)
    {
        JsonRpc.Write(_out, msg.Id!.Value, new
        {
            capabilities = new
            {
                textDocumentSync = 1, // Full sync
                hoverProvider = true,
                definitionProvider = true,
                completionProvider = new
                {
                    triggerCharacters = new[] { "." },
                    resolveProvider = false,
                },
                signatureHelpProvider = new
                {
                    triggerCharacters = new[] { "(", "," },
                },
            },
            serverInfo = new { name = "DScript Language Server", version = "0.1.0" },
        });
    }

    // ── shutdown ─────────────────────────────────────────────────────────────

    private void HandleShutdown(RpcMessage msg)
    {
        if (msg.Id.HasValue)
            JsonRpc.Write(_out, msg.Id.Value, null);
    }

    // ── document sync ────────────────────────────────────────────────────────

    private void HandleDidOpen(RpcMessage msg)
    {
        var p = msg.Params;
        var uri  = p?["textDocument"]?["uri"]?.GetValue<string>() ?? "";
        var text = p?["textDocument"]?["text"]?.GetValue<string>() ?? "";
        _docs.Open(uri, text);
    }

    private void HandleDidChange(RpcMessage msg)
    {
        var p = msg.Params;
        var uri = p?["textDocument"]?["uri"]?.GetValue<string>() ?? "";

        // LSP sends an array of content changes; we use Full sync (mode 1)
        // so there is always exactly one entry with the complete new text.
        var changes = p?["contentChanges"]?.AsArray();
        if (changes != null && changes.Count > 0)
        {
            var text = changes[changes.Count - 1]?["text"]?.GetValue<string>() ?? "";
            _docs.Change(uri, text);
        }
    }

    private void HandleDidClose(RpcMessage msg)
    {
        var uri = msg.Params?["textDocument"]?["uri"]?.GetValue<string>() ?? "";
        _docs.Close(uri);
    }

    // ── hover ────────────────────────────────────────────────────────────────

    private void HandleHover(RpcMessage msg)
    {
        var p = msg.Params;
        var uri  = p?["textDocument"]?["uri"]?.GetValue<string>() ?? "";
        var line = p?["position"]?["line"]?.GetValue<int>() ?? 0;
        var ch   = p?["position"]?["character"]?.GetValue<int>() ?? 0;

        var doc = _docs.Get(uri);
        if (doc == null) { JsonRpc.Write(_out, msg.Id!.Value, null); return; }

        var word = GetWordAt(doc.Text, line, ch);
        if (string.IsNullOrEmpty(word)) { JsonRpc.Write(_out, msg.Id!.Value, null); return; }

        // Find the best symbol info for this word.
        var sym = doc.Symbols.FirstOrDefault(s => s.Name == word);
        string hoverText;
        if (sym != null)
        {
            hoverText = sym.Kind switch
            {
                SymbolKind.Function  => $"function {word}",
                SymbolKind.Parameter => $"param {word}",
                SymbolKind.Constant  => $"const {word}",
                _                   => $"var {word}",
            };
        }
        else
        {
            hoverText = word;
        }

        JsonRpc.Write(_out, msg.Id!.Value, new
        {
            contents = new { kind = "plaintext", value = hoverText },
        });
    }

    // ── go-to-definition ─────────────────────────────────────────────────────

    private void HandleDefinition(RpcMessage msg)
    {
        var p = msg.Params;
        var uri  = p?["textDocument"]?["uri"]?.GetValue<string>() ?? "";
        var line = p?["position"]?["line"]?.GetValue<int>() ?? 0;
        var ch   = p?["position"]?["character"]?.GetValue<int>() ?? 0;

        var doc = _docs.Get(uri);
        if (doc == null) { JsonRpc.Write(_out, msg.Id!.Value, null); return; }

        var word = GetWordAt(doc.Text, line, ch);
        if (string.IsNullOrEmpty(word)) { JsonRpc.Write(_out, msg.Id!.Value, null); return; }

        // Find the declaration site (first symbol entry with this name that has
        // a real line number from the source scan).
        var sym = doc.Symbols
            .Where(s => s.Name == word && s.Line > 0)
            .OrderBy(s => s.Line)
            .FirstOrDefault();

        if (sym == null) { JsonRpc.Write(_out, msg.Id!.Value, null); return; }

        var defLine = sym.Line - 1; // Symbols use 1-based lines; LSP uses 0-based
        JsonRpc.Write(_out, msg.Id!.Value, new
        {
            uri,
            range = new
            {
                start = new { line = Math.Max(0, defLine), character = Math.Max(0, sym.Col - 1) },
                end   = new { line = Math.Max(0, defLine), character = Math.Max(0, sym.Col - 1 + word.Length) },
            },
        });
    }

    // ── completion ───────────────────────────────────────────────────────────

    private void HandleCompletion(RpcMessage msg)
    {
        var p = msg.Params;
        var uri  = p?["textDocument"]?["uri"]?.GetValue<string>() ?? "";
        var line = p?["position"]?["line"]?.GetValue<int>() ?? 0;
        var ch   = p?["position"]?["character"]?.GetValue<int>() ?? 0;

        var doc = _docs.Get(uri);
        if (doc == null) { JsonRpc.Write(_out, msg.Id!.Value, new object[0]); return; }

        var prefix = GetWordPrefix(doc.Text, line, ch);
        var isDot  = IsDotCompletion(doc.Text, line, ch);

        IEnumerable<SymbolInfo> candidates = doc.Symbols;
        if (!string.IsNullOrEmpty(prefix))
            candidates = candidates.Where(s => s.Name.StartsWith(prefix, StringComparison.Ordinal));

        // Remove duplicates, keeping the first (declaration site) entry.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = candidates
            .Where(s => seen.Add(s.Name))
            .Select(s => new
            {
                label = s.Name,
                kind  = s.Kind switch
                {
                    SymbolKind.Function  => 3,  // Function
                    SymbolKind.Parameter => 6,  // Variable
                    SymbolKind.Constant  => 21, // Constant
                    _                   => 6,  // Variable
                },
                detail = s.Kind.ToString().ToLowerInvariant(),
            })
            .ToArray();

        JsonRpc.Write(_out, msg.Id!.Value, new { isIncomplete = false, items });
    }

    // ── signature help ────────────────────────────────────────────────────────

    private void HandleSignatureHelp(RpcMessage msg)
    {
        var p = msg.Params;
        var uri  = p?["textDocument"]?["uri"]?.GetValue<string>() ?? "";
        var line = p?["position"]?["line"]?.GetValue<int>() ?? 0;
        var ch   = p?["position"]?["character"]?.GetValue<int>() ?? 0;

        var doc = _docs.Get(uri);
        if (doc == null) { JsonRpc.Write(_out, msg.Id!.Value, null); return; }

        // Find the function name before the current open paren.
        var (funcName, paramIndex) = FindFunctionCall(doc.Text, line, ch);
        if (string.IsNullOrEmpty(funcName)) { JsonRpc.Write(_out, msg.Id!.Value, null); return; }

        // Find parameters for that function in the chunk.
        var parameters = FindFunctionParameters(doc.CompiledChunk, funcName);
        if (parameters.Count == 0) { JsonRpc.Write(_out, msg.Id!.Value, null); return; }

        var paramInfos = parameters.Select(pm => new
        {
            label = pm,
            documentation = (string?)null,
        }).ToArray();

        var label = $"{funcName}({string.Join(", ", parameters)})";
        JsonRpc.Write(_out, msg.Id!.Value, new
        {
            signatures = new[]
            {
                new
                {
                    label,
                    parameters = paramInfos,
                    activeParameter = paramIndex,
                },
            },
            activeSignature = 0,
            activeParameter = paramIndex,
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Return the identifier word at (line, character) in <paramref name="source"/>.</summary>
    private static string? GetWordAt(string source, int line, int character)
    {
        var lines = source.Split('\n');
        if (line >= lines.Length) return null;
        var ln = lines[line];
        if (character > ln.Length) character = ln.Length;

        // Expand left and right while IsWordChar.
        var start = character;
        var end = character;
        while (start > 0 && IsWordChar(ln[start - 1])) start--;
        while (end < ln.Length && IsWordChar(ln[end])) end++;

        return end > start ? ln[start..end] : null;
    }

    /// <summary>Return the incomplete word before the cursor (for completion prefix filtering).</summary>
    private static string GetWordPrefix(string source, int line, int ch)
    {
        var lines = source.Split('\n');
        if (line >= lines.Length) return "";
        var ln = lines[line];
        var pos = Math.Min(ch, ln.Length);
        var start = pos;
        while (start > 0 && IsWordChar(ln[start - 1])) start--;
        return ln[start..pos];
    }

    private static bool IsDotCompletion(string source, int line, int ch)
    {
        var lines = source.Split('\n');
        if (line >= lines.Length) return false;
        var ln = lines[line];
        // Skip backwards over the word prefix.
        var pos = Math.Min(ch, ln.Length);
        while (pos > 0 && IsWordChar(ln[pos - 1])) pos--;
        return pos > 0 && ln[pos - 1] == '.';
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

    /// <summary>
    /// Walk backwards from (line, ch) to find the function name before
    /// the nearest unclosed '(', and count commas to determine param index.
    /// </summary>
    private static (string name, int paramIndex) FindFunctionCall(string source, int line, int ch)
    {
        var lines = source.Split('\n');
        if (line >= lines.Length) return ("", 0);

        // Flatten position to a single offset.
        var offset = 0;
        for (var i = 0; i < line; i++)
            offset += lines[i].Length + 1; // +1 for '\n'
        offset += Math.Min(ch, lines[line].Length);

        // Walk backwards looking for unclosed '('.
        var depth = 0;
        var paramIdx = 0;
        for (var i = offset - 1; i >= 0; i--)
        {
            var c = source[i];
            if (c == ')') { depth++; continue; }
            if (c == '(')
            {
                if (depth > 0) { depth--; continue; }
                // Found the matching open paren — get the identifier before it.
                var j = i - 1;
                while (j >= 0 && source[j] == ' ') j--;
                var end = j + 1;
                while (j >= 0 && IsWordChar(source[j])) j--;
                if (end > j + 1)
                    return (source[(j + 1)..end], paramIdx);
                return ("", paramIdx);
            }
            if (c == ',' && depth == 0) paramIdx++;
        }
        return ("", 0);
    }

    /// <summary>Find the parameter list for a named function in the chunk tree.</summary>
    private static List<string> FindFunctionParameters(DScript.Vm.Chunk? root, string funcName)
    {
        if (root == null) return [];

        // Look in Names — if the chunk has a function with a matching name, use it.
        foreach (var fn in root.Functions)
        {
            if (fn.Name == funcName)
                return fn.Parameters;
        }

        // Recurse.
        foreach (var fn in root.Functions)
        {
            var result = FindFunctionParameters(fn, funcName);
            if (result.Count > 0) return result;
        }
        return [];
    }
}
