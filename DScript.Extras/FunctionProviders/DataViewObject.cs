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
using System.Buffers.Binary;

namespace DScript.Extras.FunctionProviders
{
    /// <summary>Native backing data for a DataView ScriptVar.</summary>
    public sealed class DataViewObject
    {
        public ArrayBufferObject Buffer { get; }
        public int ByteOffset { get; }
        public int ByteLength { get; }

        public DataViewObject(ArrayBufferObject buffer, int byteOffset, int byteLength)
        {
            Buffer = buffer;
            ByteOffset = byteOffset;
            ByteLength = byteLength;
        }

        private Span<byte> At(int offset, int size)
        {
            var abs = ByteOffset + offset;
            if (abs < 0 || abs + size > ByteOffset + ByteLength)
                throw new JITException(MakeRangeError($"Offset {offset} out of bounds"));
            return Buffer.Data.AsSpan(abs, size);
        }

        // ── readers ──────────────────────────────────────────────────────────

        public sbyte  GetInt8  (int offset)                      => (sbyte)Buffer.Data[ByteOffset + offset];
        public byte   GetUint8 (int offset)                      => Buffer.Data[ByteOffset + offset];
        public short  GetInt16 (int offset, bool le)             => ReadI16(At(offset, 2), le);
        public ushort GetUint16(int offset, bool le)             => ReadU16(At(offset, 2), le);
        public int    GetInt32 (int offset, bool le)             => ReadI32(At(offset, 4), le);
        public uint   GetUint32(int offset, bool le)             => ReadU32(At(offset, 4), le);
        public float  GetFloat32(int offset, bool le)            => ReadF32(At(offset, 4), le);
        public double GetFloat64(int offset, bool le)            => ReadF64(At(offset, 8), le);
        public long   GetBigInt64 (int offset, bool le)          => ReadI64(At(offset, 8), le);
        public ulong  GetBigUint64(int offset, bool le)          => ReadU64(At(offset, 8), le);

        // ── writers ──────────────────────────────────────────────────────────

        public void SetInt8  (int offset, sbyte  v)              => Buffer.Data[ByteOffset + offset] = (byte)v;
        public void SetUint8 (int offset, byte   v)              => Buffer.Data[ByteOffset + offset] = v;
        public void SetInt16 (int offset, short  v, bool le)     => WriteI16(At(offset, 2), v, le);
        public void SetUint16(int offset, ushort v, bool le)     => WriteU16(At(offset, 2), v, le);
        public void SetInt32 (int offset, int    v, bool le)     => WriteI32(At(offset, 4), v, le);
        public void SetUint32(int offset, uint   v, bool le)     => WriteU32(At(offset, 4), v, le);
        public void SetFloat32(int offset, float  v, bool le)    => WriteF32(At(offset, 4), v, le);
        public void SetFloat64(int offset, double v, bool le)    => WriteF64(At(offset, 8), v, le);
        public void SetBigInt64 (int offset, long  v, bool le)   => WriteI64(At(offset, 8), v, le);
        public void SetBigUint64(int offset, ulong v, bool le)   => WriteU64(At(offset, 8), v, le);

        // ── primitive read helpers ────────────────────────────────────────────

        private static short  ReadI16(Span<byte> s, bool le) => le ? BinaryPrimitives.ReadInt16LittleEndian(s)  : BinaryPrimitives.ReadInt16BigEndian(s);
        private static ushort ReadU16(Span<byte> s, bool le) => le ? BinaryPrimitives.ReadUInt16LittleEndian(s) : BinaryPrimitives.ReadUInt16BigEndian(s);
        private static int    ReadI32(Span<byte> s, bool le) => le ? BinaryPrimitives.ReadInt32LittleEndian(s)  : BinaryPrimitives.ReadInt32BigEndian(s);
        private static uint   ReadU32(Span<byte> s, bool le) => le ? BinaryPrimitives.ReadUInt32LittleEndian(s) : BinaryPrimitives.ReadUInt32BigEndian(s);
        private static long   ReadI64(Span<byte> s, bool le) => le ? BinaryPrimitives.ReadInt64LittleEndian(s)  : BinaryPrimitives.ReadInt64BigEndian(s);
        private static ulong  ReadU64(Span<byte> s, bool le) => le ? BinaryPrimitives.ReadUInt64LittleEndian(s) : BinaryPrimitives.ReadUInt64BigEndian(s);

        private static float  ReadF32(Span<byte> s, bool le)
        {
            var i = ReadI32(s, le);
            return BitConverter.Int32BitsToSingle(i);
        }
        private static double ReadF64(Span<byte> s, bool le)
        {
            var i = ReadI64(s, le);
            return BitConverter.Int64BitsToDouble(i);
        }

        // ── primitive write helpers ───────────────────────────────────────────

        private static void WriteI16(Span<byte> s, short  v, bool le) { if (le) BinaryPrimitives.WriteInt16LittleEndian(s, v);  else BinaryPrimitives.WriteInt16BigEndian(s, v); }
        private static void WriteU16(Span<byte> s, ushort v, bool le) { if (le) BinaryPrimitives.WriteUInt16LittleEndian(s, v); else BinaryPrimitives.WriteUInt16BigEndian(s, v); }
        private static void WriteI32(Span<byte> s, int    v, bool le) { if (le) BinaryPrimitives.WriteInt32LittleEndian(s, v);  else BinaryPrimitives.WriteInt32BigEndian(s, v); }
        private static void WriteU32(Span<byte> s, uint   v, bool le) { if (le) BinaryPrimitives.WriteUInt32LittleEndian(s, v); else BinaryPrimitives.WriteUInt32BigEndian(s, v); }
        private static void WriteI64(Span<byte> s, long   v, bool le) { if (le) BinaryPrimitives.WriteInt64LittleEndian(s, v);  else BinaryPrimitives.WriteInt64BigEndian(s, v); }
        private static void WriteU64(Span<byte> s, ulong  v, bool le) { if (le) BinaryPrimitives.WriteUInt64LittleEndian(s, v); else BinaryPrimitives.WriteUInt64BigEndian(s, v); }

        private static void WriteF32(Span<byte> s, float  v, bool le)
        {
            WriteI32(s, BitConverter.SingleToInt32Bits(v), le);
        }
        private static void WriteF64(Span<byte> s, double v, bool le)
        {
            WriteI64(s, BitConverter.DoubleToInt64Bits(v), le);
        }

        private static ScriptVar MakeRangeError(string message)
        {
            var err = ScriptVar.CreateObject();
            err.AddChild("name", ScriptVar.FromString("RangeError"));
            err.AddChild("message", ScriptVar.FromString(message));
            return err;
        }
    }
}
