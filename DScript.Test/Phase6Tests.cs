using System.IO;
using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    /// <summary>Tests for Phase 6: source maps (column info, .dsmap sidecar).</summary>
    public class Phase6Tests
    {
        private static Chunk Compile(string source)
            => new DScriptCompiler().CompileProgram(source);

        // ---- 1. SetCurrentLine with column overload -------------------------

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
            chunk.SetCurrentLine(1, 1);
            chunk.Emit(OpCode.PushTrue);
            chunk.SetCurrentLine(2, 5);
            chunk.Emit(OpCode.PushFalse);
            chunk.SetCurrentLine(3, 10);
            chunk.Emit(OpCode.Halt);

            Assert.That(chunk.Lines[0], Is.EqualTo(1));
            Assert.That(chunk.Cols[0], Is.EqualTo(1));
            Assert.That(chunk.Lines[1], Is.EqualTo(2));
            Assert.That(chunk.Cols[1], Is.EqualTo(5));
            Assert.That(chunk.Lines[2], Is.EqualTo(3));
            Assert.That(chunk.Cols[2], Is.EqualTo(10));
        }

        // ---- 2. GetLineAndColForOffset --------------------------------------

        [Test]
        public void GetLineAndColForOffset_ReturnsCorrectValues()
        {
            var chunk = new Chunk();
            chunk.SetCurrentLine(7, 3);
            chunk.Emit(OpCode.PushTrue);
            chunk.SetCurrentLine(8, 15);
            chunk.Emit(OpCode.PushFalse);

            var (line0, col0) = chunk.GetLineAndColForOffset(0);
            var (line1, col1) = chunk.GetLineAndColForOffset(1);

            Assert.That(line0, Is.EqualTo(7));
            Assert.That(col0, Is.EqualTo(3));
            Assert.That(line1, Is.EqualTo(8));
            Assert.That(col1, Is.EqualTo(15));
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

        // ---- 3. BytecodeSerializer source map round-trip -------------------

        [Test]
        public void SaveWithSourceMap_And_LoadWithSourceMap_RoundTrips()
        {
            var source = "var x = 1;\nvar y = 2;\nvar z = x + y;";
            var chunk = Compile(source);

            var tempPath = Path.Combine(Path.GetTempPath(), "phase6_test_" + System.Guid.NewGuid().ToString("N") + ".dsbc");
            try
            {
                BytecodeSerializer.SaveWithSourceMap(chunk, tempPath);

                Assert.That(File.Exists(tempPath), Is.True, "bytecode file should exist");
                Assert.That(File.Exists(tempPath + ".dsmap"), Is.True, ".dsmap sidecar should exist");

                var loaded = BytecodeSerializer.LoadWithSourceMap(tempPath);
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded.Lines.Count, Is.EqualTo(chunk.Lines.Count));
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                if (File.Exists(tempPath + ".dsmap")) File.Delete(tempPath + ".dsmap");
            }
        }

        [Test]
        public void BuildSourceMapJson_ContainsExpectedFields()
        {
            var chunk = new Chunk();
            chunk.Name = "test.ds";
            chunk.SetCurrentLine(1, 1);
            chunk.Emit(OpCode.PushTrue);
            chunk.SetCurrentLine(2, 5);
            chunk.Emit(OpCode.PushFalse);

            var json = BytecodeSerializer.BuildSourceMapJson(chunk);

            Assert.That(json, Does.Contain("\"version\":1"));
            Assert.That(json, Does.Contain("test.ds"));
            Assert.That(json, Does.Contain("\"mappings\":["));
            Assert.That(json, Does.Contain("\"offset\":0"));
            Assert.That(json, Does.Contain("\"line\":1"));
            Assert.That(json, Does.Contain("\"col\":1"));
        }

        // ---- 4. Compiled code produces line+col info -----------------------

        [Test]
        public void CompiledCode_MultiLine_ColsListParallelToLines()
        {
            // A multi-line program; after compilation Lines and Cols must be
            // the same length as Code.
            var source = "var a = 1;\nvar b = 2;\nvar c = a + b;";
            var chunk = Compile(source);

            Assert.That(chunk.Cols.Count, Is.EqualTo(chunk.Lines.Count),
                "Cols list must be the same length as Lines list");
            Assert.That(chunk.Cols.Count, Is.EqualTo(chunk.Code.Count),
                "Cols list must be the same length as Code list");
        }

        [Test]
        public void CompiledCode_SingleLine_NonZeroColumnPresent()
        {
            // Even a single-line script should have at least one non-zero column.
            var source = "var x = 42;";
            var chunk = Compile(source);

            var hasNonZero = false;
            foreach (var c in chunk.Cols)
            {
                if (c > 0) { hasNonZero = true; break; }
            }

            Assert.That(hasNonZero, Is.True, "At least one byte should carry a non-zero column number");
        }

        [Test]
        public void LoadWithSourceMap_NoSidecar_LoadsChunkNormally()
        {
            // Save without a source map (plain Save), then load with LoadWithSourceMap —
            // it should succeed and the Cols list will be empty/default.
            var source = "var x = 10;";
            var chunk = Compile(source);

            var tempPath = Path.Combine(Path.GetTempPath(), "phase6_nosmap_" + System.Guid.NewGuid().ToString("N") + ".dsbc");
            try
            {
                var bytes = BytecodeSerializer.Save(chunk);
                File.WriteAllBytes(tempPath, bytes);

                var loaded = BytecodeSerializer.LoadWithSourceMap(tempPath);
                Assert.That(loaded, Is.Not.Null);
                // No .dsmap means Cols stays at whatever the binary serializer set
                // (currently not persisted in the binary format, so it will be empty)
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
    }
}
