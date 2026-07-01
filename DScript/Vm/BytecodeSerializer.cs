/*
Copyright (c) 2014 - 2020 Darren Horrocks

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DScript.Vm
{
    /// <summary>
    /// Serializes a compiled <see cref="Chunk"/> program to/from a binary stream
    /// so it can be saved and re-run later. Native functions are NOT stored:
    /// bytecode references them by name and they are resolved at run time, so a
    /// loaded program simply needs the host to register the same natives before
    /// running it.
    /// </summary>
    public static class BytecodeSerializer
    {
        private const uint Magic = 0x44534243; // "DSBC"
        private const int Version = 1;

        public static void Save(Chunk chunk, Stream stream)
        {
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(Version);
            WriteChunk(writer, chunk);
        }

        public static byte[] Save(Chunk chunk)
        {
            using var ms = new MemoryStream();
            Save(chunk, ms);
            return ms.ToArray();
        }

        public static Chunk Load(Stream stream)
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            var magic = reader.ReadUInt32();
            if (magic != Magic)
            {
                throw new ScriptException("Not a DScript bytecode stream");
            }

            var version = reader.ReadInt32();
            if (version != Version)
            {
                throw new ScriptException($"Unsupported DScript bytecode version {version}");
            }

            return ReadChunk(reader);
        }

        public static Chunk Load(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            return Load(ms);
        }

        // ------------------------------------------------------------------ //
        // Source map support (.dsmap JSON sidecar)
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Save the compiled chunk as bytecode and also write a <c>.dsmap</c> JSON
        /// sidecar file alongside <paramref name="path"/> that maps bytecode offsets
        /// back to (source, line, col) triples.
        /// </summary>
        public static void SaveWithSourceMap(Chunk chunk, string path)
        {
            // Write main bytecode file
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                Save(chunk, fs);
            }

            // Write sidecar .dsmap file
            var mapPath = path + ".dsmap";
            var json = BuildSourceMapJson(chunk);
            File.WriteAllText(mapPath, json, Encoding.UTF8);
        }

        /// <summary>
        /// Load a chunk from <paramref name="path"/> and, if a <c>.dsmap</c> sidecar
        /// exists, restore column information from it into the chunk's <see cref="Chunk.Cols"/>
        /// list.
        /// </summary>
        public static Chunk LoadWithSourceMap(string path)
        {
            Chunk chunk;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                chunk = Load(fs);
            }

            var mapPath = path + ".dsmap";
            if (File.Exists(mapPath))
            {
                var json = File.ReadAllText(mapPath, Encoding.UTF8);
                ApplySourceMap(chunk, json);
            }

            return chunk;
        }

        /// <summary>
        /// Build a JSON source map string for the given chunk (and all nested function chunks).
        /// The format is:
        /// <code>
        /// { "version": 1, "sources": ["name"], "mappings": [ { "offset": N, "line": L, "col": C }, ... ] }
        /// </code>
        /// </summary>
        public static string BuildSourceMapJson(Chunk chunk)
        {
            var sb = new StringBuilder();
            sb.Append("{\"version\":1,\"sources\":[");
            sb.Append(JsonString(chunk.Name ?? "<main>"));
            sb.Append("],\"mappings\":[");
            AppendMappings(sb, chunk);
            sb.Append("]}");
            return sb.ToString();
        }

        private static void AppendMappings(StringBuilder sb, Chunk chunk)
        {
            var first = true;
            for (var i = 0; i < chunk.Lines.Count; i++)
            {
                var line = chunk.Lines[i];
                var col = i < chunk.Cols.Count ? chunk.Cols[i] : 0;
                if (line == 0 && col == 0) continue; // skip unknown

                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"offset\":");
                sb.Append(i);
                sb.Append(",\"line\":");
                sb.Append(line);
                sb.Append(",\"col\":");
                sb.Append(col);
                sb.Append('}');
            }
        }

        private static void ApplySourceMap(Chunk chunk, string json)
        {
            // Simple manual JSON parse — avoids taking a dependency on System.Text.Json
            // or Newtonsoft in the core library.
            var mappings = ParseMappings(json);
            foreach (var (offset, line, col) in mappings)
            {
                // Extend Cols list to cover this offset if needed
                while (chunk.Cols.Count <= offset)
                    chunk.Cols.Add(0);
                chunk.Cols[offset] = col;

                // Also update Lines if out of range (shouldn't happen normally)
                while (chunk.Lines.Count <= offset)
                    chunk.Lines.Add(0);
                chunk.Lines[offset] = line;
            }
        }

        private static List<(int offset, int line, int col)> ParseMappings(string json)
        {
            var result = new List<(int, int, int)>();
            // Find "mappings":[...]
            var arrStart = json.IndexOf("\"mappings\":", System.StringComparison.Ordinal);
            if (arrStart < 0) return result;
            arrStart = json.IndexOf('[', arrStart);
            if (arrStart < 0) return result;
            var arrEnd = json.IndexOf(']', arrStart);
            if (arrEnd < 0) return result;

            var arr = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
            // Each entry looks like: {"offset":N,"line":L,"col":C}
            var pos = 0;
            while (pos < arr.Length)
            {
                var objStart = arr.IndexOf('{', pos);
                if (objStart < 0) break;
                var objEnd = arr.IndexOf('}', objStart);
                if (objEnd < 0) break;
                var entry = arr.Substring(objStart + 1, objEnd - objStart - 1);
                var offset = ReadJsonInt(entry, "offset");
                var line = ReadJsonInt(entry, "line");
                var col = ReadJsonInt(entry, "col");
                if (offset >= 0) result.Add((offset, line, col));
                pos = objEnd + 1;
            }

            return result;
        }

        private static int ReadJsonInt(string obj, string key)
        {
            var search = "\"" + key + "\":";
            var idx = obj.IndexOf(search, System.StringComparison.Ordinal);
            if (idx < 0) return -1;
            idx += search.Length;
            var end = idx;
            while (end < obj.Length && (char.IsDigit(obj[end]) || obj[end] == '-')) end++;
            if (end == idx) return -1;
            return int.TryParse(obj.AsSpan(idx, end - idx), out var v) ? v : -1;
        }

        private static string JsonString(string s)
        {
            var sb = new StringBuilder();
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ //

        private static void WriteChunk(BinaryWriter writer, Chunk chunk)
        {
            writer.Write(chunk.Name ?? string.Empty);
            writer.Write(chunk.Source ?? string.Empty);

            var code = chunk.Code;
            writer.Write(code.Count);
            for (var i = 0; i < code.Count; i++)
            {
                writer.Write(code[i]);
            }

            writer.Write(chunk.Parameters.Count);
            foreach (var p in chunk.Parameters)
            {
                writer.Write(p);
            }

            writer.Write(chunk.Names.Count);
            foreach (var n in chunk.Names)
            {
                writer.Write(n);
            }

            writer.Write(chunk.Constants.Count);
            foreach (var c in chunk.Constants)
            {
                WriteConstant(writer, c);
            }

            writer.Write(chunk.Functions.Count);
            foreach (var fn in chunk.Functions)
            {
                WriteChunk(writer, fn);
            }
        }

        private static Chunk ReadChunk(BinaryReader reader)
        {
            var chunk = new Chunk
            {
                Name = reader.ReadString(),
                Source = reader.ReadString()
            };

            var codeLength = reader.ReadInt32();
            for (var i = 0; i < codeLength; i++)
            {
                chunk.Code.Add(reader.ReadByte());
            }

            var paramCount = reader.ReadInt32();
            for (var i = 0; i < paramCount; i++)
            {
                chunk.Parameters.Add(reader.ReadString());
            }

            var nameCount = reader.ReadInt32();
            for (var i = 0; i < nameCount; i++)
            {
                chunk.Names.Add(reader.ReadString());
            }

            var constCount = reader.ReadInt32();
            for (var i = 0; i < constCount; i++)
            {
                chunk.Constants.Add(ReadConstant(reader));
            }

            var funcCount = reader.ReadInt32();
            for (var i = 0; i < funcCount; i++)
            {
                chunk.Functions.Add(ReadChunk(reader));
            }

            // Lever A: the slot frame size / UsesSlots flag are not stored in the
            // stream (slots are an AOT/closure-build feature; the format is unchanged).
            // Recover them from the loaded code so slotted bytecode still runs: scan for
            // GetLocal/SetLocal and size the frame to the highest slot referenced.
            RecoverSlotMetadata(chunk);

            return chunk;
        }

        // Set UsesSlots/SlotCount from the presence of GetLocal/SetLocal in the loaded
        // bytecode (their operand is the slot index), so a frame is allocated on calls.
        // Also recover MakesClosure so RecyclableFrame stays false for closures — without
        // this, a deserialized outer function recycles its frame vars before the inner
        // function has a chance to read captured variables.
        private static void RecoverSlotMetadata(Chunk chunk)
        {
            var maxSlot = -1;
            for (var i = 0; i < chunk.Code.Count;)
            {
                var op = (OpCode)chunk.Code[i];
                if (op is OpCode.GetLocal or OpCode.SetLocal)
                {
                    var slot = chunk.ReadInt(i + 1);
                    if (slot > maxSlot) maxSlot = slot;
                }
                if (op is OpCode.MakeClosure)
                    chunk.MakesClosure = true;
                i += Chunk.InstructionSize(op);
            }
            if (maxSlot >= 0)
            {
                chunk.UsesSlots = true;
                if (chunk.SlotCount <= maxSlot) chunk.SlotCount = maxSlot + 1;
            }
        }

        private static void WriteConstant(BinaryWriter writer, ConstantValue value)
        {
            writer.Write((byte)value.Kind);
            switch (value.Kind)
            {
                case ConstantKind.Int:
                    writer.Write(value.IntValue);
                    break;
                case ConstantKind.Double:
                    writer.Write(value.DoubleValue);
                    break;
                case ConstantKind.BigInt:
                    writer.Write(value.BigIntValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                default: // String / Regex
                    writer.Write(value.StringValue ?? string.Empty);
                    break;
            }
        }

        private static ConstantValue ReadConstant(BinaryReader reader)
        {
            var kind = (ConstantKind)reader.ReadByte();
            switch (kind)
            {
                case ConstantKind.Int: return ConstantValue.Int(reader.ReadInt32());
                case ConstantKind.Double: return ConstantValue.Double(reader.ReadDouble());
                case ConstantKind.Regex: return ConstantValue.Regex(reader.ReadString());
                case ConstantKind.BigInt: return ConstantValue.BigInt(System.Numerics.BigInteger.Parse(reader.ReadString(), System.Globalization.CultureInfo.InvariantCulture));
                default: return ConstantValue.String(reader.ReadString());
            }
        }
    }
}
