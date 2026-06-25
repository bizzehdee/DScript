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

namespace DScript.Extras.FunctionProviders
{
    /// <summary>Backing store for an ArrayBuffer — a plain byte array.</summary>
    public sealed class ArrayBufferObject
    {
        public byte[] Data { get; }
        public int ByteLength => Data.Length;

        public ArrayBufferObject(int byteLength)
        {
            Data = new byte[byteLength]; // zero-initialised by CLR
        }

        /// <summary>Wrap an existing byte array without copying (used internally).</summary>
        internal ArrayBufferObject(byte[] data)
        {
            Data = data;
        }

        /// <summary>Return a new ArrayBufferObject containing a copy of <paramref name="start"/>..<paramref name="end"/>.</summary>
        public ArrayBufferObject Slice(int start, int end)
        {
            if (start < 0) start = System.Math.Max(0, ByteLength + start);
            if (end < 0) end = System.Math.Max(0, ByteLength + end);
            start = System.Math.Min(System.Math.Max(start, 0), ByteLength);
            end   = System.Math.Min(System.Math.Max(end,   0), ByteLength);
            var len = System.Math.Max(0, end - start);
            var result = new byte[len];
            if (len > 0) System.Array.Copy(Data, start, result, 0, len);
            return new ArrayBufferObject(result);
        }
    }
}
