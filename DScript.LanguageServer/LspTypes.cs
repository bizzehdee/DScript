namespace DScript.LanguageServer;

// ── Core LSP types ───────────────────────────────────────────────────────────

record Position(int Line, int Character);

record Range(Position Start, Position End);

record Location(string Uri, Range Range);

// Severity: 1=Error, 2=Warning, 3=Information, 4=Hint
record Diagnostic(Range Range, int Severity, string Message, string Source = "dscript");

record TextDocumentIdentifier(string Uri);

record VersionedTextDocumentIdentifier(string Uri, int Version);

record TextDocumentItem(string Uri, string LanguageId, int Version, string Text);

// CompletionItemKind values (subset used here)
// 1=Text, 2=Method, 3=Function, 4=Constructor, 5=Field, 6=Variable, 9=Module, 12=Value
record CompletionItem(
    string Label,
    int Kind,
    string? Detail = null,
    string? Documentation = null);

// ── Symbol info ──────────────────────────────────────────────────────────────

enum SymbolKind { Variable, Function, Parameter, Constant }

record SymbolInfo(string Name, int Line, int Col, SymbolKind Kind);

// ── Incremental text change ───────────────────────────────────────────────────

record TextDocumentContentChangeEvent(string Text, Range? Range = null);
