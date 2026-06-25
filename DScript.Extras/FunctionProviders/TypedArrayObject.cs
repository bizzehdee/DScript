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
using System.Runtime.InteropServices;

namespace DScript.Extras.FunctionProviders
{
    /// <summary>Element type for a TypedArray view.</summary>
    public enum TypedArrayKind
    {
        Int8, Uint8, Uint8Clamped, Int16, Uint16,
        Int32, Uint32, Float32, Float64,
        BigInt64, BigUint64
    }

    /// <summary>
    /// Native backing data for a TypedArray ScriptVar.
    /// Stores the element kind, a reference to the <see cref="ArrayBufferObject"/>,
    /// and a byte offset + element count into that buffer.
    /// </summary>
    public sealed class TypedArrayObject : ITypedArrayAccess
    {
        public TypedArrayKind Kind { get; }
        public ArrayBufferObject Buffer { get; }
        public int ByteOffset { get; }
        public int Length { get; }   // element count
        public int BytesPerElement { get; }

        public TypedArrayObject(TypedArrayKind kind, ArrayBufferObject buffer, int byteOffset, int length)
        {
            Kind = kind;
            Buffer = buffer;
            ByteOffset = byteOffset;
            Length = length;
            BytesPerElement = GetBytesPerElement(kind);
        }

        public static int GetBytesPerElement(TypedArrayKind kind) => kind switch
        {
            TypedArrayKind.Int8        => 1,
            TypedArrayKind.Uint8       => 1,
            TypedArrayKind.Uint8Clamped => 1,
            TypedArrayKind.Int16       => 2,
            TypedArrayKind.Uint16      => 2,
            TypedArrayKind.Int32       => 4,
            TypedArrayKind.Uint32      => 4,
            TypedArrayKind.Float32     => 4,
            TypedArrayKind.Float64     => 8,
            TypedArrayKind.BigInt64    => 8,
            TypedArrayKind.BigUint64   => 8,
            _                          => 1
        };

        /// <summary>Read element at <paramref name="index"/> from the backing buffer.</summary>
        public ScriptVar GetElement(int index)
        {
            var byteIdx = ByteOffset + index * BytesPerElement;
            var data = Buffer.Data;
            return Kind switch
            {
                TypedArrayKind.Int8         => ScriptVar.FromInt((sbyte)data[byteIdx]),
                TypedArrayKind.Uint8        => ScriptVar.FromInt(data[byteIdx]),
                TypedArrayKind.Uint8Clamped => ScriptVar.FromInt(data[byteIdx]),
                TypedArrayKind.Int16        => ScriptVar.FromInt(MemoryMarshal.Read<short>(data.AsSpan(byteIdx))),
                TypedArrayKind.Uint16       => ScriptVar.FromInt(MemoryMarshal.Read<ushort>(data.AsSpan(byteIdx))),
                TypedArrayKind.Int32        => ScriptVar.FromInt(MemoryMarshal.Read<int>(data.AsSpan(byteIdx))),
                TypedArrayKind.Uint32       => ScriptVar.FromDouble((double)MemoryMarshal.Read<uint>(data.AsSpan(byteIdx))),
                TypedArrayKind.Float32      => ScriptVar.FromDouble(MemoryMarshal.Read<float>(data.AsSpan(byteIdx))),
                TypedArrayKind.Float64      => ScriptVar.FromDouble(MemoryMarshal.Read<double>(data.AsSpan(byteIdx))),
                TypedArrayKind.BigInt64     => ScriptVar.CreateBigInt(new System.Numerics.BigInteger(MemoryMarshal.Read<long>(data.AsSpan(byteIdx)))),
                TypedArrayKind.BigUint64    => ScriptVar.CreateBigInt(new System.Numerics.BigInteger(MemoryMarshal.Read<ulong>(data.AsSpan(byteIdx)))),
                _                           => ScriptVar.CreateUndefined()
            };
        }

        /// <summary>Write <paramref name="value"/> at element <paramref name="index"/> into the backing buffer.</summary>
        public void SetElement(int index, ScriptVar value)
        {
            var byteIdx = ByteOffset + index * BytesPerElement;
            var data = Buffer.Data;
            switch (Kind)
            {
                case TypedArrayKind.Int8:
                    data[byteIdx] = (byte)(sbyte)ToInt32(value);
                    break;
                case TypedArrayKind.Uint8:
                    data[byteIdx] = (byte)ToUint32(value);
                    break;
                case TypedArrayKind.Uint8Clamped:
                    var clamped = (int)Math.Round(value.Float);
                    data[byteIdx] = (byte)Math.Clamp(clamped, 0, 255);
                    break;
                case TypedArrayKind.Int16:
                    MemoryMarshal.Write(data.AsSpan(byteIdx), (short)ToInt32(value));
                    break;
                case TypedArrayKind.Uint16:
                    MemoryMarshal.Write(data.AsSpan(byteIdx), (ushort)ToUint32(value));
                    break;
                case TypedArrayKind.Int32:
                    MemoryMarshal.Write(data.AsSpan(byteIdx), ToInt32(value));
                    break;
                case TypedArrayKind.Uint32:
                    MemoryMarshal.Write(data.AsSpan(byteIdx), ToUint32(value));
                    break;
                case TypedArrayKind.Float32:
                    MemoryMarshal.Write(data.AsSpan(byteIdx), (float)value.Float);
                    break;
                case TypedArrayKind.Float64:
                    MemoryMarshal.Write(data.AsSpan(byteIdx), value.Float);
                    break;
                case TypedArrayKind.BigInt64:
                    MemoryMarshal.Write(data.AsSpan(byteIdx), value.IsBigInt ? (long)value.BigIntData : (long)value.Float);
                    break;
                case TypedArrayKind.BigUint64:
                    MemoryMarshal.Write(data.AsSpan(byteIdx), value.IsBigInt ? (ulong)value.BigIntData : (ulong)(long)value.Float);
                    break;
            }
        }

        // ES ToInt32: truncate to integer, mod 2^32, interpret as signed.
        private static int ToInt32(ScriptVar v)
        {
            if (v.IsInt) return v.Int;
            var d = v.Float;
            if (double.IsNaN(d) || double.IsInfinity(d) || d == 0) return 0;
            return (int)(uint)(long)Math.Truncate(d);
        }

        // ES ToUint32: truncate to integer, mod 2^32, interpret as unsigned.
        private static uint ToUint32(ScriptVar v)
        {
            if (v.IsInt) return (uint)v.Int;
            var d = v.Float;
            if (double.IsNaN(d) || double.IsInfinity(d) || d == 0) return 0;
            return (uint)(long)Math.Truncate(d);
        }
    }
}
