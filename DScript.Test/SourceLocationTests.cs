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

using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    /// <summary>Tests for per-bytecode line and column tracking in <see cref="Chunk"/>.</summary>
    public class SourceLocationTests
    {
        [Test]
        public void SetCurrentLine_WithColumn_StoresLineAndCol()
        {
            var chunk = new Chunk();
            chunk.SetCurrentLine(5, 12);
            chunk.Emit(OpCode.Halt);

            Assert.That(chunk.Lines[0], Is.EqualTo(5));
            Assert.That(chunk.Cols[0], Is.EqualTo(12));
        }

        [Test]
        public void SetCurrentLine_WithoutColumn_DefaultsColToZero()
        {
            var chunk = new Chunk();
            chunk.SetCurrentLine(3);
            chunk.Emit(OpCode.Halt);

            Assert.That(chunk.Lines[0], Is.EqualTo(3));
            Assert.That(chunk.Cols[0], Is.EqualTo(0));
        }

        [Test]
        public void SetCurrentLine_MultipleLocations_StoresEach()
        {
            var chunk = new Chunk();
            chunk.SetCurrentLine(1, 1);  chunk.Emit(OpCode.PushTrue);
            chunk.SetCurrentLine(2, 5);  chunk.Emit(OpCode.PushFalse);
            chunk.SetCurrentLine(3, 10); chunk.Emit(OpCode.Halt);

            Assert.Multiple(() =>
            {
                Assert.That(chunk.Lines[0], Is.EqualTo(1)); Assert.That(chunk.Cols[0], Is.EqualTo(1));
                Assert.That(chunk.Lines[1], Is.EqualTo(2)); Assert.That(chunk.Cols[1], Is.EqualTo(5));
                Assert.That(chunk.Lines[2], Is.EqualTo(3)); Assert.That(chunk.Cols[2], Is.EqualTo(10));
            });
        }

        [Test]
        public void GetLineAndColForOffset_ReturnsCorrectValues()
        {
            var chunk = new Chunk();
            chunk.SetCurrentLine(7, 3);  chunk.Emit(OpCode.PushTrue);
            chunk.SetCurrentLine(8, 15); chunk.Emit(OpCode.PushFalse);

            var (line0, col0) = chunk.GetLineAndColForOffset(0);
            var (line1, col1) = chunk.GetLineAndColForOffset(1);

            Assert.Multiple(() =>
            {
                Assert.That(line0, Is.EqualTo(7)); Assert.That(col0, Is.EqualTo(3));
                Assert.That(line1, Is.EqualTo(8)); Assert.That(col1, Is.EqualTo(15));
            });
        }

        [Test]
        public void GetLineAndColForOffset_OutOfRange_ReturnsZeros()
        {
            var chunk = new Chunk();
            chunk.SetCurrentLine(1, 1);
            chunk.Emit(OpCode.Halt);

            var (line, col) = chunk.GetLineAndColForOffset(9999);

            Assert.That(line, Is.EqualTo(0));
            Assert.That(col, Is.EqualTo(0));
        }

        [Test]
        public void CompiledCode_ColsListParallelToLines()
        {
            var chunk = new DScriptCompiler().CompileProgram("var a = 1;\nvar b = 2;\nvar c = a + b;");

            Assert.That(chunk.Cols.Count, Is.EqualTo(chunk.Lines.Count));
            Assert.That(chunk.Cols.Count, Is.EqualTo(chunk.Code.Count));
        }

        [Test]
        public void CompiledCode_SingleLine_HasNonZeroColumn()
        {
            var chunk = new DScriptCompiler().CompileProgram("var x = 42;");

            var hasNonZero = false;
            foreach (var c in chunk.Cols) if (c > 0) { hasNonZero = true; break; }

            Assert.That(hasNonZero, Is.True);
        }
    }
}
