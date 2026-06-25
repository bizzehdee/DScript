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

namespace DScript
{
    /// <summary>
    /// Implemented by TypedArray native data objects (Int8Array, Float64Array, …)
    /// stored in a <see cref="ScriptVar"/>'s data field.  The VM uses this interface
    /// to route numeric-index reads and writes directly to the typed array's byte
    /// buffer without creating ScriptVar child nodes for every element.
    /// </summary>
    public interface ITypedArrayAccess
    {
        /// <summary>Number of elements in the typed array view.</summary>
        int Length { get; }

        /// <summary>Read element at <paramref name="index"/> from the backing buffer.</summary>
        ScriptVar GetElement(int index);

        /// <summary>Write <paramref name="value"/> at element <paramref name="index"/> into the backing buffer.</summary>
        void SetElement(int index, ScriptVar value);
    }
}
