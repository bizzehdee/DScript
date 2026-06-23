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
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class BufferTests
    {
        private static ScriptEngine MakeEngine()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            return engine;
        }

        // --- Buffer.from ---

        [Test]
        public void Buffer_From_String_HasCorrectLength()
        {
            var engine = MakeEngine();
            engine.Execute("var b = Buffer.from('hello'); var result = b.length;");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(5));
        }

        [Test]
        public void Buffer_From_Array_HasCorrectLength()
        {
            var engine = MakeEngine();
            engine.Execute("var b = Buffer.from([65, 66, 67]); var result = b.length;");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(3));
        }

        [Test]
        public void Buffer_From_StringToString_RoundTrips()
        {
            var engine = MakeEngine();
            engine.Execute("var b = Buffer.from('hello'); var result = b.toString();");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("hello"));
        }

        // --- Buffer.alloc ---

        [Test]
        public void Buffer_Alloc_ZeroFilled_ByDefault()
        {
            var engine = MakeEngine();
            engine.Execute("var b = Buffer.alloc(4); var result = b.readUInt8(0);");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(0));
        }

        [Test]
        public void Buffer_Alloc_WithFill_FillsByte()
        {
            var engine = MakeEngine();
            engine.Execute("var b = Buffer.alloc(3, 255); var result = b.readUInt8(1);");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(255));
        }

        [Test]
        public void Buffer_Alloc_HasCorrectLength()
        {
            var engine = MakeEngine();
            engine.Execute("var b = Buffer.alloc(10); var result = b.length;");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(10));
        }

        // --- Buffer.allocUnsafe ---

        [Test]
        public void Buffer_AllocUnsafe_HasCorrectLength()
        {
            var engine = MakeEngine();
            engine.Execute("var b = Buffer.allocUnsafe(8); var result = b.length;");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(8));
        }

        // --- Buffer.isBuffer ---

        [Test]
        public void Buffer_IsBuffer_WithBuffer_ReturnsTrue()
        {
            var engine = MakeEngine();
            engine.Execute("var b = Buffer.alloc(1); var result = Buffer.isBuffer(b);");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.True);
        }

        [Test]
        public void Buffer_IsBuffer_WithNonBuffer_ReturnsFalse()
        {
            var engine = MakeEngine();
            engine.Execute("var result = Buffer.isBuffer('hello');");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.False);
        }

        // --- Buffer.concat ---

        [Test]
        public void Buffer_Concat_JoinsBuffers()
        {
            var engine = MakeEngine();
            engine.Execute(
                "var a = Buffer.from('hello'); " +
                "var b = Buffer.from(' world'); " +
                "var c = Buffer.concat([a, b]); " +
                "var result = c.toString();");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("hello world"));
        }

        // --- toString ---

        [Test]
        public void Buffer_ToString_HexEncoding()
        {
            var engine = MakeEngine();
            engine.Execute("var b = Buffer.from([0xff, 0x0a]); var result = b.toString('hex');");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("ff0a"));
        }

        [Test]
        public void Buffer_ToString_Base64Encoding()
        {
            var engine = MakeEngine();
            engine.Execute("var b = Buffer.from('hello'); var result = b.toString('base64');");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("aGVsbG8="));
        }

        // --- slice ---

        [Test]
        public void Buffer_Slice_ReturnsSubrange()
        {
            var engine = MakeEngine();
            engine.Execute("var b = Buffer.from('hello'); var s = b.slice(1, 3); var result = s.toString();");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("el"));
        }

        [Test]
        public void Buffer_Slice_NoEnd_SlicesToEnd()
        {
            var engine = MakeEngine();
            engine.Execute("var b = Buffer.from('hello'); var s = b.slice(3); var result = s.toString();");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("lo"));
        }

        // --- copy ---

        [Test]
        public void Buffer_Copy_CopiesBytesIntoTarget()
        {
            var engine = MakeEngine();
            engine.Execute(
                "var src = Buffer.from('hi'); " +
                "var dst = Buffer.alloc(5); " +
                "src.copy(dst, 1); " +
                "var result = dst.toString();");
            // dst[0] = 0, dst[1] = 'h' (104), dst[2] = 'i' (105), dst[3..4] = 0
            Assert.That(engine.Root.GetParameter("result").String[1], Is.EqualTo('h'));
        }

        // --- equals ---

        [Test]
        public void Buffer_Equals_SameContent_ReturnsTrue()
        {
            var engine = MakeEngine();
            engine.Execute("var a = Buffer.from('abc'); var b = Buffer.from('abc'); var result = a.equals(b);");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.True);
        }

        [Test]
        public void Buffer_Equals_DifferentContent_ReturnsFalse()
        {
            var engine = MakeEngine();
            engine.Execute("var a = Buffer.from('abc'); var b = Buffer.from('xyz'); var result = a.equals(b);");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.False);
        }

        // --- readUInt8 / writeUInt8 ---

        [Test]
        public void Buffer_WriteAndRead_UInt8_RoundTrips()
        {
            var engine = MakeEngine();
            engine.Execute("var b = Buffer.alloc(2); b.writeUInt8(42, 0); var result = b.readUInt8(0);");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(42));
        }
    }
}
