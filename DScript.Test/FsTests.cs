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

using System.IO;
using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class FsTests
    {
        private string _tmpDir;

        [SetUp]
        public void SetUp()
        {
            _tmpDir = Path.Combine(Path.GetTempPath(), "DScriptFsTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmpDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tmpDir))
                Directory.Delete(_tmpDir, recursive: true);
        }

        private ScriptEngine MakeEngine()
        {
            var engine = new ScriptEngine();
            // Tests use a temp dir outside CWD, so FileSystemEscape is required.
            new EngineFunctionLoader().RegisterFunctions(engine, EnginePermissions.FileSystemUnsafe);
            return engine;
        }

        private string TmpPath(string name) => Path.Combine(_tmpDir, name).Replace('\\', '/');

        // --- readFileSync ---

        [Test]
        public void Fs_ReadFileSync_ReadsExistingFile()
        {
            var p = TmpPath("hello.txt");
            File.WriteAllText(p, "hello world");
            var engine = MakeEngine();
            engine.Execute($"var result = fs.readFileSync('{p}');");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("hello world"));
        }

        [Test]
        public void Fs_ReadFileSync_WithUtf8Enc_ReadsText()
        {
            var p = TmpPath("enc.txt");
            File.WriteAllText(p, "encoded");
            var engine = MakeEngine();
            engine.Execute($"var result = fs.readFileSync('{p}', 'utf8');");
            Assert.That(engine.Root.GetParameter("result").String, Is.EqualTo("encoded"));
        }

        [Test]
        public void Fs_ReadFileSync_BufferEnc_ReturnsArray()
        {
            var p = TmpPath("bytes.bin");
            File.WriteAllBytes(p, new byte[] { 65, 66, 67 });
            var engine = MakeEngine();
            engine.Execute($"var result = fs.readFileSync('{p}', 'buffer'); var len = result.length;");
            Assert.That(engine.Root.GetParameter("len").Int, Is.EqualTo(3));
        }

        // --- writeFileSync ---

        [Test]
        public void Fs_WriteFileSync_WritesText()
        {
            var p = TmpPath("write.txt");
            var engine = MakeEngine();
            engine.Execute($"fs.writeFileSync('{p}', 'test content');");
            Assert.That(File.ReadAllText(p), Is.EqualTo("test content"));
        }

        [Test]
        public void Fs_WriteFileSync_OverwritesExisting()
        {
            var p = TmpPath("over.txt");
            File.WriteAllText(p, "old");
            var engine = MakeEngine();
            engine.Execute($"fs.writeFileSync('{p}', 'new');");
            Assert.That(File.ReadAllText(p), Is.EqualTo("new"));
        }

        // --- appendFileSync ---

        [Test]
        public void Fs_AppendFileSync_AppendsToFile()
        {
            var p = TmpPath("append.txt");
            File.WriteAllText(p, "line1");
            var engine = MakeEngine();
            engine.Execute($"fs.appendFileSync('{p}', 'line2');");
            Assert.That(File.ReadAllText(p), Is.EqualTo("line1line2"));
        }

        [Test]
        public void Fs_AppendFileSync_CreatesFileIfMissing()
        {
            var p = TmpPath("newfile.txt");
            var engine = MakeEngine();
            engine.Execute($"fs.appendFileSync('{p}', 'data');");
            Assert.That(File.ReadAllText(p), Is.EqualTo("data"));
        }

        // --- existsSync ---

        [Test]
        public void Fs_ExistsSync_ExistingFile_ReturnsTrue()
        {
            var p = TmpPath("exists.txt");
            File.WriteAllText(p, "");
            var engine = MakeEngine();
            engine.Execute($"var result = fs.existsSync('{p}');");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.True);
        }

        [Test]
        public void Fs_ExistsSync_MissingFile_ReturnsFalse()
        {
            var p = TmpPath("nope.txt");
            var engine = MakeEngine();
            engine.Execute($"var result = fs.existsSync('{p}');");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.False);
        }

        [Test]
        public void Fs_ExistsSync_ExistingDirectory_ReturnsTrue()
        {
            var p = _tmpDir.Replace('\\', '/');
            var engine = MakeEngine();
            engine.Execute($"var result = fs.existsSync('{p}');");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.True);
        }

        // --- mkdirSync ---

        [Test]
        public void Fs_MkdirSync_CreatesDirectory()
        {
            var p = TmpPath("newdir");
            var engine = MakeEngine();
            engine.Execute($"fs.mkdirSync('{p}');");
            Assert.That(Directory.Exists(p), Is.True);
        }

        // --- rmdirSync ---

        [Test]
        public void Fs_RmdirSync_RemovesEmptyDirectory()
        {
            var p = TmpPath("rmdir");
            Directory.CreateDirectory(p);
            var engine = MakeEngine();
            engine.Execute($"fs.rmdirSync('{p}');");
            Assert.That(Directory.Exists(p), Is.False);
        }

        // --- unlinkSync ---

        [Test]
        public void Fs_UnlinkSync_DeletesFile()
        {
            var p = TmpPath("unlink.txt");
            File.WriteAllText(p, "bye");
            var engine = MakeEngine();
            engine.Execute($"fs.unlinkSync('{p}');");
            Assert.That(File.Exists(p), Is.False);
        }

        // --- readdirSync ---

        [Test]
        public void Fs_ReaddirSync_ListsEntries()
        {
            File.WriteAllText(Path.Combine(_tmpDir, "a.txt"), "");
            File.WriteAllText(Path.Combine(_tmpDir, "b.txt"), "");
            var p = _tmpDir.Replace('\\', '/');
            var engine = MakeEngine();
            engine.Execute($"var result = fs.readdirSync('{p}'); var len = result.length;");
            Assert.That(engine.Root.GetParameter("len").Int, Is.EqualTo(2));
        }

        // --- renameSync ---

        [Test]
        public void Fs_RenameSync_MovesFile()
        {
            var src = TmpPath("from.txt");
            var dst = TmpPath("to.txt");
            File.WriteAllText(src, "move me");
            var engine = MakeEngine();
            engine.Execute($"fs.renameSync('{src}', '{dst}');");
            Assert.That(File.Exists(src), Is.False);
            Assert.That(File.Exists(dst), Is.True);
            Assert.That(File.ReadAllText(dst), Is.EqualTo("move me"));
        }

        // --- statSync ---

        [Test]
        public void Fs_StatSync_File_HasSize()
        {
            var p = TmpPath("stat.txt");
            File.WriteAllText(p, "hello");
            var engine = MakeEngine();
            engine.Execute($"var s = fs.statSync('{p}'); var result = s.size;");
            Assert.That(engine.Root.GetParameter("result").Int, Is.GreaterThan(0));
        }

        [Test]
        public void Fs_StatSync_File_IsFileTrue()
        {
            var p = TmpPath("statf.txt");
            File.WriteAllText(p, "x");
            var engine = MakeEngine();
            engine.Execute($"var s = fs.statSync('{p}'); var result = s.isFile();");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.True);
        }

        [Test]
        public void Fs_StatSync_File_IsDirectoryFalse()
        {
            var p = TmpPath("statd.txt");
            File.WriteAllText(p, "x");
            var engine = MakeEngine();
            engine.Execute($"var s = fs.statSync('{p}'); var result = s.isDirectory();");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.False);
        }

        [Test]
        public void Fs_StatSync_Directory_IsDirectoryTrue()
        {
            var p = _tmpDir.Replace('\\', '/');
            var engine = MakeEngine();
            engine.Execute($"var s = fs.statSync('{p}'); var result = s.isDirectory();");
            Assert.That(engine.Root.GetParameter("result").Bool, Is.True);
        }

        // --- copyFileSync ---

        [Test]
        public void Fs_CopyFileSync_CopiesContent()
        {
            var src = TmpPath("copy_src.txt");
            var dst = TmpPath("copy_dst.txt");
            File.WriteAllText(src, "copy me");
            var engine = MakeEngine();
            engine.Execute($"fs.copyFileSync('{src}', '{dst}');");
            Assert.That(File.Exists(dst), Is.True);
            Assert.That(File.ReadAllText(dst), Is.EqualTo("copy me"));
        }

        [Test]
        public void Fs_CopyFileSync_OverwritesDestination()
        {
            var src = TmpPath("csrc.txt");
            var dst = TmpPath("cdst.txt");
            File.WriteAllText(src, "new");
            File.WriteAllText(dst, "old");
            var engine = MakeEngine();
            engine.Execute($"fs.copyFileSync('{src}', '{dst}');");
            Assert.That(File.ReadAllText(dst), Is.EqualTo("new"));
        }
    }
}
