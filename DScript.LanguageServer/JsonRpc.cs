using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DScript.LanguageServer;

/// <summary>Minimal JSON-RPC 2.0 message envelope.</summary>
record struct RpcMessage(string? Method, int? Id, JsonNode? Params, JsonNode? Result, JsonNode? Error);

static class JsonRpc
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Read one JSON-RPC message from <paramref name="stream"/> using the
    /// LSP framing: "Content-Length: N\r\n\r\n" followed by N UTF-8 bytes.
    /// Returns null when the stream ends.
    /// </summary>
    public static RpcMessage? Read(Stream stream)
    {
        // Read headers until the blank line.
        int contentLength = -1;
        while (true)
        {
            var line = ReadHeaderLine(stream);
            if (line == null) return null; // EOF
            if (line.Length == 0) break;   // blank line ends headers

            const string prefix = "Content-Length: ";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.Parse(line[prefix.Length..].Trim());
            }
        }

        if (contentLength <= 0) return null;

        var body = new byte[contentLength];
        var totalRead = 0;
        while (totalRead < contentLength)
        {
            var read = stream.Read(body, totalRead, contentLength - totalRead);
            if (read == 0) return null;
            totalRead += read;
        }

        var json = JsonNode.Parse(body);
        if (json == null) return null;

        var method = json["method"]?.GetValue<string>();
        int? id = null;
        if (json["id"] is JsonNode idNode)
        {
            try { id = idNode.GetValue<int>(); } catch { id = null; }
        }
        var @params = json["params"];
        var result = json["result"];
        var error = json["error"];

        return new RpcMessage(method, id, @params, result, error);
    }

    /// <summary>Send a JSON-RPC response (reply to a request).</summary>
    public static void Write(Stream stream, int id, object? result)
    {
        var envelope = new { jsonrpc = "2.0", id, result };
        SendJson(stream, envelope);
    }

    /// <summary>Send a JSON-RPC error response.</summary>
    public static void WriteError(Stream stream, int id, int code, string message)
    {
        var envelope = new { jsonrpc = "2.0", id, error = new { code, message } };
        SendJson(stream, envelope);
    }

    /// <summary>Send a JSON-RPC notification (no id, no reply expected).</summary>
    public static void WriteNotification(Stream stream, string method, object? @params)
    {
        var envelope = new { jsonrpc = "2.0", method, @params };
        SendJson(stream, envelope);
    }

    private static void SendJson(Stream stream, object envelope)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
        var header = $"Content-Length: {json.Length}\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        // Write header + body atomically so concurrent writes don't interleave.
        lock (stream)
        {
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(json, 0, json.Length);
            stream.Flush();
        }
    }

    /// <summary>
    /// Read one CR+LF-terminated header line from the stream.
    /// Returns null on EOF; returns empty string for the blank separator line.
    /// </summary>
    private static string? ReadHeaderLine(Stream stream)
    {
        var sb = new StringBuilder();
        while (true)
        {
            var b = stream.ReadByte();
            if (b == -1) return null;
            if (b == '\r')
            {
                var next = stream.ReadByte();
                if (next == '\n') return sb.ToString();
                if (next != -1) sb.Append((char)next);
                continue;
            }
            sb.Append((char)b);
        }
    }
}
