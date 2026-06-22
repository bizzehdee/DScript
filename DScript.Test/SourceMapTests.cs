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
using System.IO;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    /// <summary>Tests for <see cref="BytecodeSerializer"/> source-map sidecar generation and loading.</summary>
    public class SourceMapTests
    {
        [Test]
        public void SaveWithSourceMap_And_LoadWithSourceMap_RoundTrips()
        {
            var chunk = new DScriptCompiler().CompileProgram("var x = 1;\nvar y = 2;\nvar z = x + y;");

            var tempPath = Path.Combine(Path.GetTempPath(), "srcmap_" + Guid.NewGuid().ToString("N") + ".dsbc");
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
        public void BuildSourceMapJson_ContainsRequiredFields()
        {
            var chunk = new Chunk();
            chunk.Name = "test.ds";
            chunk.SetCurrentLine(1, 1); chunk.Emit(OpCode.PushTrue);
            chunk.SetCurrentLine(2, 5); chunk.Emit(OpCode.PushFalse);

            var json = BytecodeSerializer.BuildSourceMapJson(chunk);

            Assert.Multiple(() =>
            {
                Assert.That(json, Does.Contain("\"version\":1"));
                Assert.That(json, Does.Contain("test.ds"));
                Assert.That(json, Does.Contain("\"mappings\":["));
                Assert.That(json, Does.Contain("\"offset\":0"));
                Assert.That(json, Does.Contain("\"line\":1"));
                Assert.That(json, Does.Contain("\"col\":1"));
            });
        }

        [Test]
        public void LoadWithSourceMap_NoSidecar_LoadsChunkNormally()
        {
            var chunk = new DScriptCompiler().CompileProgram("var x = 10;");

            var tempPath = Path.Combine(Path.GetTempPath(), "nosmap_" + Guid.NewGuid().ToString("N") + ".dsbc");
            try
            {
                File.WriteAllBytes(tempPath, BytecodeSerializer.Save(chunk));

                var loaded = BytecodeSerializer.LoadWithSourceMap(tempPath);
                Assert.That(loaded, Is.Not.Null);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
    }
}
