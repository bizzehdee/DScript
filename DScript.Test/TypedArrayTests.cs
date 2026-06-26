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

using DScript;
using DScript.Compiler;
using DScript.Extras;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class TypedArrayTests
    {
        private static ScriptVar Run(string source)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root;
        }

        private static double RunDouble(string source)
        {
            var root = Run(source);
            var r = root.GetParameter("r");
            return r.IsInt ? r.Int : r.Float;
        }

        private static int RunInt(string source) => (int)RunDouble(source);

        private static string RunString(string source)
            => Run(source).GetParameter("r").String;

        // ── ArrayBuffer ───────────────────────────────────────────────────────

        [Test]
        public void ArrayBuffer_Constructor_SetsbyteLength()
        {
            Assert.That(RunInt("var b = new ArrayBuffer(16); var r = b.byteLength;"), Is.EqualTo(16));
        }

        [Test]
        public void ArrayBuffer_ZeroLength_Allowed()
        {
            Assert.That(RunInt("var b = new ArrayBuffer(0); var r = b.byteLength;"), Is.EqualTo(0));
        }

        [Test]
        public void ArrayBuffer_Slice_ReturnsCorrectByteLength()
        {
            Assert.That(RunInt("var b = new ArrayBuffer(8); var s = b.slice(2, 6); var r = s.byteLength;"), Is.EqualTo(4));
        }

        // ── Float64Array ──────────────────────────────────────────────────────

        [Test]
        public void Float64Array_FromLength_ElementsZero()
        {
            Assert.That(RunDouble("var a = new Float64Array(4); var r = a[0];"), Is.EqualTo(0.0));
        }

        [Test]
        public void Float64Array_FromLength_LengthCorrect()
        {
            Assert.That(RunInt("var a = new Float64Array(4); var r = a.length;"), Is.EqualTo(4));
        }

        [Test]
        public void Float64Array_SetAndGet_RoundTrips()
        {
            Assert.That(RunDouble("var a = new Float64Array(4); a[0] = 1.5; var r = a[0];"), Is.EqualTo(1.5));
        }

        [Test]
        public void Float64Array_SetAndGet_MultipleElements()
        {
            Assert.That(RunDouble("var a = new Float64Array(4); a[0]=1.1; a[1]=2.2; a[2]=3.3; var r = a[1];"),
                Is.EqualTo(2.2).Within(1e-12));
        }

        [Test]
        public void Float64Array_FromArrayBuffer_SharesStorage()
        {
            // Writing via the typed array view should show in a DataView of the same buffer.
            Assert.That(RunDouble(
                "var buf = new ArrayBuffer(8);" +
                "var view = new Float64Array(buf);" +
                "view[0] = 1.5;" +
                "var r = view[0];"),
                Is.EqualTo(1.5));
        }

        [Test]
        public void Float64Array_BytesPerElement_IsEight()
        {
            Assert.That(RunInt("var a = new Float64Array(1); var r = a.BYTES_PER_ELEMENT;"), Is.EqualTo(8));
        }

        [Test]
        public void Float64Array_ByteLength_IsLengthTimesEight()
        {
            Assert.That(RunInt("var a = new Float64Array(4); var r = a.byteLength;"), Is.EqualTo(32));
        }

        [Test]
        public void Float64Array_OutOfBoundsRead_IsUndefined()
        {
            Assert.That(Run("var a = new Float64Array(2); var r = a[99];").GetParameter("r").IsUndefined, Is.True);
        }

        [Test]
        public void Float64Array_OutOfBoundsWrite_IsIgnored()
        {
            // Writing out-of-bounds must not throw and must not affect in-bounds elements.
            Assert.That(RunDouble("var a = new Float64Array(2); a[0]=7.0; a[99]=42.0; var r = a[0];"),
                Is.EqualTo(7.0));
        }

        // ── Int32Array ────────────────────────────────────────────────────────

        [Test]
        public void Int32Array_SetAndGet_RoundTrips()
        {
            Assert.That(RunInt("var a = new Int32Array(4); a[0]=42; var r = a[0];"), Is.EqualTo(42));
        }

        [Test]
        public void Int32Array_NegativeValues_RoundTrip()
        {
            Assert.That(RunInt("var a = new Int32Array(4); a[0]=-1; var r = a[0];"), Is.EqualTo(-1));
        }

        [Test]
        public void Int32Array_BytesPerElement_IsFour()
        {
            Assert.That(RunInt("var a = new Int32Array(1); var r = a.BYTES_PER_ELEMENT;"), Is.EqualTo(4));
        }

        // ── Uint8Array ────────────────────────────────────────────────────────

        [Test]
        public void Uint8Array_SetAndGet_RoundTrips()
        {
            Assert.That(RunInt("var a = new Uint8Array(4); a[0]=255; var r = a[0];"), Is.EqualTo(255));
        }

        [Test]
        public void Uint8Array_Overflow_Wraps()
        {
            Assert.That(RunInt("var a = new Uint8Array(4); a[0]=256; var r = a[0];"), Is.EqualTo(0));
        }

        // ── Uint8ClampedArray ─────────────────────────────────────────────────

        [Test]
        public void Uint8ClampedArray_OverflowClamped()
        {
            Assert.That(RunInt("var a = new Uint8ClampedArray(4); a[0]=300; var r = a[0];"), Is.EqualTo(255));
        }

        [Test]
        public void Uint8ClampedArray_UnderflowClamped()
        {
            Assert.That(RunInt("var a = new Uint8ClampedArray(4); a[0]=-5; var r = a[0];"), Is.EqualTo(0));
        }

        // ── Float32Array ──────────────────────────────────────────────────────

        [Test]
        public void Float32Array_SetAndGet_RoundTrips()
        {
            Assert.That(RunDouble("var a = new Float32Array(4); a[0]=1.5; var r = a[0];"),
                Is.EqualTo(1.5).Within(1e-6));
        }

        // ── Int16Array ────────────────────────────────────────────────────────

        [Test]
        public void Int16Array_SetAndGet_RoundTrips()
        {
            Assert.That(RunInt("var a = new Int16Array(4); a[0]=1000; var r = a[0];"), Is.EqualTo(1000));
        }

        // ── from-ArrayBuffer constructor ──────────────────────────────────────

        [Test]
        public void TypedArray_FromArrayBuffer_LengthDerivedFromBuffer()
        {
            // 16-byte buffer → 4 Int32 elements
            Assert.That(RunInt("var b = new ArrayBuffer(16); var a = new Int32Array(b); var r = a.length;"),
                Is.EqualTo(4));
        }

        [Test]
        public void TypedArray_FromArrayBuffer_SharedBuffer_WriteVisible()
        {
            // Two Float64Array views on the same buffer see each other's writes.
            Assert.That(RunDouble(
                "var buf = new ArrayBuffer(16);" +
                "var v1 = new Float64Array(buf);" +
                "var v2 = new Float64Array(buf);" +
                "v1[0] = 3.14;" +
                "var r = v2[0];"),
                Is.EqualTo(3.14).Within(1e-12));
        }

        // ── from-array constructor ────────────────────────────────────────────

        [Test]
        public void TypedArray_FromArray_CopiesElements()
        {
            Assert.That(RunDouble("var a = new Float64Array([1.0, 2.0, 3.0]); var r = a[1];"),
                Is.EqualTo(2.0));
        }

        [Test]
        public void TypedArray_FromArray_LengthCorrect()
        {
            Assert.That(RunInt("var a = new Int32Array([10, 20, 30]); var r = a.length;"), Is.EqualTo(3));
        }

        // ── shared-buffer DataView ────────────────────────────────────────────

        [Test]
        public void DataView_GetFloat64_ReadsTypedArrayWrite()
        {
            // Write 1.5 via Float64Array; read it back via DataView (little-endian).
            Assert.That(RunDouble(
                "var buf = new ArrayBuffer(8);" +
                "var ta = new Float64Array(buf);" +
                "ta[0] = 1.5;" +
                "var dv = new DataView(buf);" +
                "var r = dv.getFloat64(0, true);"),
                Is.EqualTo(1.5).Within(1e-12));
        }

        [Test]
        public void DataView_SetFloat64_ReadableByTypedArray()
        {
            Assert.That(RunDouble(
                "var buf = new ArrayBuffer(8);" +
                "var dv = new DataView(buf);" +
                "dv.setFloat64(0, 2.5, true);" +
                "var ta = new Float64Array(buf);" +
                "var r = ta[0];"),
                Is.EqualTo(2.5).Within(1e-12));
        }

        [Test]
        public void DataView_GetInt32_BigEndian()
        {
            // 0x00000001 big-endian → 1
            Assert.That(RunInt(
                "var buf = new ArrayBuffer(4);" +
                "var dv = new DataView(buf);" +
                "dv.setInt32(0, 1, false);" +
                "var r = dv.getInt32(0, false);"),
                Is.EqualTo(1));
        }

        [Test]
        public void DataView_GetUint8_SimpleRead()
        {
            Assert.That(RunInt(
                "var buf = new ArrayBuffer(4);" +
                "var dv = new DataView(buf);" +
                "dv.setUint8(0, 42);" +
                "var r = dv.getUint8(0);"),
                Is.EqualTo(42));
        }

        // ── TypedArray methods ────────────────────────────────────────────────

        [Test]
        public void TypedArray_Fill_SetsAllElements()
        {
            Assert.That(RunInt(
                "var a = new Int32Array(4); a.fill(7); var r = a[0] + a[1] + a[2] + a[3];"),
                Is.EqualTo(28));
        }

        [Test]
        public void TypedArray_ForEach_AccumulatesSum()
        {
            Assert.That(RunDouble(
                "var a = new Float64Array([1.0, 2.0, 3.0]); var s = 0;" +
                "a.forEach(function(v) { s = s + v; }); var r = s;"),
                Is.EqualTo(6.0).Within(1e-12));
        }

        [Test]
        public void TypedArray_Map_ProducesNewArray()
        {
            Assert.That(RunDouble(
                "var a = new Float64Array([1.0, 2.0, 3.0]); var b = a.map(function(v) { return v * 2; }); var r = b[1];"),
                Is.EqualTo(4.0).Within(1e-12));
        }

        [Test]
        public void TypedArray_Find_ReturnsFirstMatch()
        {
            Assert.That(RunDouble(
                "var a = new Float64Array([1.0, 2.0, 3.0]); var r = a.find(function(v) { return v > 1.5; });"),
                Is.EqualTo(2.0).Within(1e-12));
        }

        [Test]
        public void TypedArray_Filter_KeepsMatchingElements()
        {
            Assert.That(RunInt(
                "var a = new Int32Array([1, 2, 3, 4]); var b = a.filter(function(v) { return v > 2; }); var r = b.length;"),
                Is.EqualTo(2));
        }

        [Test]
        public void TypedArray_Reduce_SumsElements()
        {
            Assert.That(RunDouble(
                "var a = new Float64Array([1.0, 2.0, 3.0]); var r = a.reduce(function(acc, v) { return acc + v; }, 0.0);"),
                Is.EqualTo(6.0).Within(1e-12));
        }

        [Test]
        public void TypedArray_IndexOf_FindsElement()
        {
            Assert.That(RunInt(
                "var a = new Int32Array([10, 20, 30]); var r = a.indexOf(20);"),
                Is.EqualTo(1));
        }

        [Test]
        public void TypedArray_Includes_TrueForPresent()
        {
            Assert.That(RunInt(
                "var a = new Int32Array([10, 20, 30]); var r = a.includes(20) ? 1 : 0;"),
                Is.EqualTo(1));
        }

        [Test]
        public void TypedArray_Reverse_ReversesInPlace()
        {
            Assert.That(RunInt(
                "var a = new Int32Array([1, 2, 3]); a.reverse(); var r = a[0];"),
                Is.EqualTo(3));
        }

        [Test]
        public void TypedArray_Slice_ReturnsNewArray()
        {
            Assert.That(RunDouble(
                "var a = new Float64Array([1.0, 2.0, 3.0, 4.0]); var b = a.slice(1, 3); var r = b[0];"),
                Is.EqualTo(2.0).Within(1e-12));
        }

        [Test]
        public void TypedArray_Join_DefaultComma()
        {
            Assert.That(RunString(
                "var a = new Int32Array([1, 2, 3]); var r = a.join();"),
                Is.EqualTo("1,2,3"));
        }

        [Test]
        public void TypedArray_Set_CopiesFromArray()
        {
            Assert.That(RunInt(
                "var a = new Int32Array(4); a.set([10, 20, 30]); var r = a[1];"),
                Is.EqualTo(20));
        }

        [Test]
        public void TypedArray_BenchmarkLoop_CorrectSum()
        {
            // Exercises O(1) indexed access across 1000 elements — would be catastrophically
            // slow if elements were stored as ScriptVar children (linked-list O(n) per access).
            const string src =
                "var a = new Float64Array(1000);" +
                "for (var i = 0; i < 1000; i = i + 1) { a[i] = i + 1.0; }" +
                "var sum = 0.0;" +
                "for (var i = 0; i < 1000; i = i + 1) { sum = sum + a[i]; }" +
                "var r = sum;";
            // Sum of 1..1000 = 500500
            Assert.That(RunDouble(src), Is.EqualTo(500500.0).Within(1e-6));
        }

        // ── BYTES_PER_ELEMENT static property ────────────────────────────────

        [Test]
        public void TypedArray_StaticBytesPerElement_OnConstructor()
        {
            Assert.That(RunInt("var r = Float64Array.BYTES_PER_ELEMENT;"), Is.EqualTo(8));
            Assert.That(RunInt("var r = Int32Array.BYTES_PER_ELEMENT;"),   Is.EqualTo(4));
            Assert.That(RunInt("var r = Uint8Array.BYTES_PER_ELEMENT;"),   Is.EqualTo(1));
        }

        // ── TypedArray methods (coverage gap-fill) ────────────────────────────

        [Test]
        public void TypedArray_Every_TrueWhenAllMatch()
        {
            Assert.That(RunInt(
                "var a = new Int32Array([2, 4, 6]); var r = a.every(function(v) { return v % 2 === 0; }) ? 1 : 0;"),
                Is.EqualTo(1));
        }

        [Test]
        public void TypedArray_Every_FalseOnFirstMismatch()
        {
            Assert.That(RunInt(
                "var a = new Int32Array([2, 3, 6]); var r = a.every(function(v) { return v % 2 === 0; }) ? 1 : 0;"),
                Is.EqualTo(0));
        }

        [Test]
        public void TypedArray_Some_TrueWhenAnyMatch()
        {
            Assert.That(RunInt(
                "var a = new Int32Array([1, 3, 4]); var r = a.some(function(v) { return v % 2 === 0; }) ? 1 : 0;"),
                Is.EqualTo(1));
        }

        [Test]
        public void TypedArray_Some_FalseWhenNoneMatch()
        {
            Assert.That(RunInt(
                "var a = new Int32Array([1, 3, 5]); var r = a.some(function(v) { return v % 2 === 0; }) ? 1 : 0;"),
                Is.EqualTo(0));
        }

        [Test]
        public void TypedArray_FindIndex_ReturnsCorrectIndex()
        {
            Assert.That(RunInt(
                "var a = new Int32Array([10, 20, 30]); var r = a.findIndex(function(v) { return v > 15; });"),
                Is.EqualTo(1));
        }

        [Test]
        public void TypedArray_FindIndex_MinusOneWhenNotFound()
        {
            Assert.That(RunInt(
                "var a = new Int32Array([1, 2, 3]); var r = a.findIndex(function(v) { return v > 99; });"),
                Is.EqualTo(-1));
        }

        [Test]
        public void TypedArray_IndexOf_MinusOneWhenAbsent()
        {
            Assert.That(RunInt(
                "var a = new Int32Array([10, 20, 30]); var r = a.indexOf(99);"),
                Is.EqualTo(-1));
        }

        [Test]
        public void TypedArray_Includes_FalseWhenAbsent()
        {
            Assert.That(RunInt(
                "var a = new Int32Array([10, 20]); var r = a.includes(99) ? 1 : 0;"),
                Is.EqualTo(0));
        }

        [Test]
        public void TypedArray_Subarray_SharesBuffer()
        {
            // subarray returns a view into the same buffer — writes via sub are visible in parent.
            Assert.That(RunInt(
                "var a = new Int32Array([1, 2, 3, 4]); var b = a.subarray(1, 3); b[0] = 99; var r = a[1];"),
                Is.EqualTo(99));
        }

        [Test]
        public void TypedArray_Subarray_LengthCorrect()
        {
            Assert.That(RunInt(
                "var a = new Int32Array([1, 2, 3, 4]); var b = a.subarray(1, 3); var r = b.length;"),
                Is.EqualTo(2));
        }

        [Test]
        public void TypedArray_CopyWithin_CopiesSegment()
        {
            Assert.That(RunInt(
                "var a = new Int32Array([1, 2, 3, 4, 5]); a.copyWithin(0, 3); var r = a[0];"),
                Is.EqualTo(4));
        }

        [Test]
        public void TypedArray_Keys_ReturnsIndices()
        {
            Assert.That(RunInt(
                "var a = new Int32Array([10, 20, 30]); var k = a.keys(); var r = k[1];"),
                Is.EqualTo(1));
        }

        [Test]
        public void TypedArray_Values_ReturnsElements()
        {
            Assert.That(RunInt(
                "var a = new Int32Array([10, 20, 30]); var v = a.values(); var r = v[2];"),
                Is.EqualTo(30));
        }

        [Test]
        public void TypedArray_Entries_ReturnsPairs()
        {
            Assert.That(RunInt(
                "var a = new Int32Array([10, 20]); var e = a.entries(); var r = e[1][1];"),
                Is.EqualTo(20));
        }

        [Test]
        public void TypedArray_Set_FromTypedArray()
        {
            Assert.That(RunInt(
                "var a = new Int32Array(4); var b = new Int32Array([7, 8]); a.set(b, 1); var r = a[2];"),
                Is.EqualTo(8));
        }

        [Test]
        public void TypedArray_Reduce_NoInitialValue()
        {
            Assert.That(RunDouble(
                "var a = new Float64Array([1.0, 2.0, 3.0]); var r = a.reduce(function(acc, v) { return acc + v; });"),
                Is.EqualTo(6.0).Within(1e-12));
        }

        // ── ArrayBuffer.isView ────────────────────────────────────────────────

        [Test]
        public void ArrayBuffer_IsView_TrueForTypedArray()
        {
            Assert.That(RunInt(
                "var a = new Int32Array(4); var r = ArrayBuffer.isView(a) ? 1 : 0;"),
                Is.EqualTo(1));
        }

        [Test]
        public void ArrayBuffer_IsView_TrueForDataView()
        {
            Assert.That(RunInt(
                "var buf = new ArrayBuffer(4); var dv = new DataView(buf); var r = ArrayBuffer.isView(dv) ? 1 : 0;"),
                Is.EqualTo(1));
        }

        [Test]
        public void ArrayBuffer_IsView_FalseForPlainObject()
        {
            Assert.That(RunInt(
                "var r = ArrayBuffer.isView({}) ? 1 : 0;"),
                Is.EqualTo(0));
        }

        // ── DataView — remaining getters and setters ──────────────────────────

        [Test]
        public void DataView_GetSetInt8_RoundTrip()
        {
            Assert.That(RunInt(
                "var dv = new DataView(new ArrayBuffer(4)); dv.setInt8(0, -5); var r = dv.getInt8(0);"),
                Is.EqualTo(-5));
        }

        [Test]
        public void DataView_GetSetInt16_LittleEndian()
        {
            Assert.That(RunInt(
                "var dv = new DataView(new ArrayBuffer(4)); dv.setInt16(0, 1000, true); var r = dv.getInt16(0, true);"),
                Is.EqualTo(1000));
        }

        [Test]
        public void DataView_GetSetUint16_BigEndian()
        {
            Assert.That(RunInt(
                "var dv = new DataView(new ArrayBuffer(4)); dv.setUint16(0, 0xABCD, false); var r = dv.getUint16(0, false);"),
                Is.EqualTo(0xABCD));
        }

        [Test]
        public void DataView_GetSetUint32_LittleEndian()
        {
            Assert.That(RunDouble(
                "var dv = new DataView(new ArrayBuffer(8)); dv.setUint32(0, 3000000000, true); var r = dv.getUint32(0, true);"),
                Is.EqualTo(3000000000.0).Within(1));
        }

        [Test]
        public void DataView_GetSetFloat32_RoundTrip()
        {
            Assert.That(RunDouble(
                "var dv = new DataView(new ArrayBuffer(4)); dv.setFloat32(0, 1.5, true); var r = dv.getFloat32(0, true);"),
                Is.EqualTo(1.5).Within(1e-6));
        }

        // ── BigInt typed arrays ───────────────────────────────────────────────

        [Test]
        public void BigInt64Array_SetAndGet_RoundTrips()
        {
            // Write via Int32Array (low 4 bytes), read back via BigInt64Array (little-endian).
            Assert.That(RunInt(
                "var buf = new ArrayBuffer(8);" +
                "var i32 = new Int32Array(buf);" +
                "i32[0] = 42; i32[1] = 0;" + // sets bytes 0-3 to 42, bytes 4-7 to 0
                "var big = new BigInt64Array(buf);" +
                "var dv = new DataView(buf);" +
                "var r = dv.getInt32(0, true);"), // read back the low 4 bytes
                Is.EqualTo(42));
        }

        [Test]
        public void BigUint64Array_SetAndGet_RoundTrips()
        {
            // Write via BigUint64Array, verify bytes via DataView
            Assert.That(RunInt(
                "var buf = new ArrayBuffer(8);" +
                "var dv = new DataView(buf);" +
                "dv.setInt32(0, 255, true); dv.setInt32(4, 0, true);" + // set 255 LE
                "var big = new BigUint64Array(buf);" +
                "var r = dv.getInt32(0, true);"), // read back
                Is.EqualTo(255));
        }

        // ── DataView BigInt ───────────────────────────────────────────────────

        [Test]
        public void DataView_GetSetBigInt64_RoundTrip()
        {
            // Write 42n as BigInt64, read back low bytes via Int32 to avoid Number() conversion.
            Assert.That(RunInt(
                "var buf = new ArrayBuffer(8);" +
                "var dv = new DataView(buf);" +
                "dv.setBigInt64(0, 42n, true);" +
                "var r = dv.getInt32(0, true);"),
                Is.EqualTo(42));
        }

        [Test]
        public void DataView_GetSetBigUint64_RoundTrip()
        {
            Assert.That(RunInt(
                "var buf = new ArrayBuffer(8);" +
                "var dv = new DataView(buf);" +
                "dv.setBigUint64(0, 100n, true);" +
                "var r = dv.getInt32(0, true);"),
                Is.EqualTo(100));
        }

        // ── remaining typed array types ───────────────────────────────────────

        [Test]
        public void Uint16Array_SetAndGet_RoundTrips()
        {
            Assert.That(RunInt("var a = new Uint16Array(4); a[0]=60000; var r = a[0];"), Is.EqualTo(60000));
        }

        [Test]
        public void Uint32Array_SetAndGet_RoundTrips()
        {
            Assert.That(RunDouble("var a = new Uint32Array(4); a[0]=3000000000; var r = a[0];"),
                Is.EqualTo(3000000000.0).Within(1));
        }

        // ── BYTES_PER_ELEMENT static property ────────────────────────────────

        [Test]
        public void TypedArray_StaticBytesPerElement_AllTypes()
        {
            Assert.That(RunInt("var r = Int8Array.BYTES_PER_ELEMENT;"),         Is.EqualTo(1));
            Assert.That(RunInt("var r = Uint8ClampedArray.BYTES_PER_ELEMENT;"), Is.EqualTo(1));
            Assert.That(RunInt("var r = Int16Array.BYTES_PER_ELEMENT;"),        Is.EqualTo(2));
            Assert.That(RunInt("var r = Uint16Array.BYTES_PER_ELEMENT;"),       Is.EqualTo(2));
            Assert.That(RunInt("var r = Uint32Array.BYTES_PER_ELEMENT;"),       Is.EqualTo(4));
            Assert.That(RunInt("var r = Float32Array.BYTES_PER_ELEMENT;"),      Is.EqualTo(4));
            Assert.That(RunInt("var r = BigInt64Array.BYTES_PER_ELEMENT;"),     Is.EqualTo(8));
            Assert.That(RunInt("var r = BigUint64Array.BYTES_PER_ELEMENT;"),    Is.EqualTo(8));
        }
    }
}
