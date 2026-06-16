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

using System.IO;

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

            return chunk;
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
                default: return ConstantValue.String(reader.ReadString());
            }
        }
    }
}
