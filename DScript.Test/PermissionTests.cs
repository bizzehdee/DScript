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
    public class PermissionTests
    {
        private static ScriptEngine MakeEngine(EnginePermissions permissions)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine, permissions);
            return engine;
        }

        // --- EnginePermissionStore ---

        [Test]
        public void PermissionStore_DefaultIsAll_WhenNotSet()
        {
            var engine = new ScriptEngine();
            Assert.That(EnginePermissionStore.Get(engine), Is.EqualTo(EnginePermissions.All));
        }

        [Test]
        public void PermissionStore_StoresValue()
        {
            var engine = new ScriptEngine();
            EnginePermissionStore.Set(engine, EnginePermissions.FileSystem);
            Assert.That(EnginePermissionStore.Get(engine), Is.EqualTo(EnginePermissions.FileSystem));
        }

        [Test]
        public void PermissionStore_Require_ThrowsWhenMissing()
        {
            var engine = new ScriptEngine();
            EnginePermissionStore.Set(engine, EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                EnginePermissionStore.Require(engine, EnginePermissions.FileSystem));
        }

        [Test]
        public void PermissionStore_Require_DoesNotThrowWhenGranted()
        {
            var engine = new ScriptEngine();
            EnginePermissionStore.Set(engine, EnginePermissions.FileSystem);
            Assert.DoesNotThrow(() =>
                EnginePermissionStore.Require(engine, EnginePermissions.FileSystem));
        }

        [Test]
        public void PermissionException_HasRequiredField()
        {
            var ex = new PermissionException(EnginePermissions.Network);
            Assert.That(ex.Required, Is.EqualTo(EnginePermissions.Network));
        }

        // --- FileSystem permission ---

        [Test]
        public void Fs_WithoutFileSystemPermission_ThrowsPermissionException()
        {
            var engine = MakeEngine(EnginePermissions.None);
            var tmpFile = Path.GetTempFileName();
            try
            {
                Assert.Throws<PermissionException>(() =>
                    engine.Run(ScriptEngine.Compile($"fs.existsSync('{tmpFile.Replace('\\', '/')}');")));
            }
            finally { File.Delete(tmpFile); }
        }

        [Test]
        public void Fs_WithFileSystemPermission_DoesNotThrow()
        {
            var engine = MakeEngine(EnginePermissions.FileSystem);
            var tmpFile = Path.GetTempFileName();
            try
            {
                Assert.DoesNotThrow(() =>
                    engine.Execute($"fs.existsSync('{tmpFile.Replace('\\', '/')}');"));
            }
            finally { File.Delete(tmpFile); }
        }

        [Test]
        public void Fs_ReadFileSync_WithoutPermission_Throws()
        {
            var engine = MakeEngine(EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("fs.readFileSync('/tmp/x');")));
        }

        [Test]
        public void Fs_WriteFileSync_WithoutPermission_Throws()
        {
            var engine = MakeEngine(EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("fs.writeFileSync('/tmp/x', 'y');")));
        }

        [Test]
        public void Fs_AppendFileSync_WithoutPermission_Throws()
        {
            var engine = MakeEngine(EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("fs.appendFileSync('/tmp/x', 'y');")));
        }

        // --- Network permission ---

        [Test]
        public void Fetch_WithoutNetworkPermission_ThrowsPermissionException()
        {
            var engine = MakeEngine(EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("fetch('http://example.com');")));
        }

        // --- EnvironmentVariables permission ---

        [Test]
        public void ProcessGetenv_WithoutEnvPermission_Throws()
        {
            var engine = MakeEngine(EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("process.getenv('PATH');")));
        }

        [Test]
        public void ProcessGetenv_WithEnvPermission_DoesNotThrow()
        {
            var engine = MakeEngine(EnginePermissions.EnvironmentVariables);
            Assert.DoesNotThrow(() =>
                engine.Execute("process.getenv('PATH');"));
        }

        // --- All permissions (default) ---

        [Test]
        public void RegisterFunctions_DefaultPermissions_AllGranted()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            Assert.That(EnginePermissionStore.Get(engine), Is.EqualTo(EnginePermissions.All));
        }
    }
}
