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
    }
}
