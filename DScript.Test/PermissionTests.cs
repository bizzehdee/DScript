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

        // Produces a temp file path inside CWD so FileSystem (no Escape) can reach it.
        private static string TempFileInCwd()
            => Path.Combine(Directory.GetCurrentDirectory(), "perm_test_" + Path.GetRandomFileName());

        // Produces a path that is definitely outside CWD (system temp dir).
        private static string PathOutsideCwd(string filename = "perm_escape_test.txt")
            => Path.Combine(Path.GetTempPath(), filename);

        // ── EnginePermissionStore ────────────────────────────────────────────────

        [Test]
        public void PermissionStore_DefaultIsAll_WhenNotSet()
        {
            // An engine that was never passed through RegisterFunctions still gets
            // the "unknown engine" fallback of All (trust-by-default for host code
            // that pre-dates the permission system).
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

        // ── RegisterFunctions default ─────────────────────────────────────────────

        [Test]
        public void RegisterFunctions_DefaultPermissions_NoneGranted()
        {
            // Safe by default: calling RegisterFunctions with no permissions argument
            // must grant None, not All.
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            Assert.That(EnginePermissionStore.Get(engine), Is.EqualTo(EnginePermissions.None));
        }

        // ── FileSystem permission flags ───────────────────────────────────────────

        [Test]
        public void Fs_FileSystemWrite_ImpliesFileSystemRead()
        {
            // The Write flag embeds the Read bit so code only needs to check Read.
            Assert.That(
                (EnginePermissions.FileSystemWrite & EnginePermissions.FileSystemRead),
                Is.EqualTo(EnginePermissions.FileSystemRead));
        }

        [Test]
        public void Fs_FileSystem_DoesNotIncludeEscape()
        {
            Assert.That(
                (EnginePermissions.FileSystem & EnginePermissions.FileSystemEscape),
                Is.EqualTo(EnginePermissions.None));
        }

        [Test]
        public void Fs_FileSystemUnsafe_IncludesEscape()
        {
            Assert.That(
                (EnginePermissions.FileSystemUnsafe & EnginePermissions.FileSystemEscape),
                Is.EqualTo(EnginePermissions.FileSystemEscape));
        }

        // ── FileSystem read/write enforcement ────────────────────────────────────

        [Test]
        public void Fs_WithoutFileSystemPermission_ThrowsPermissionException()
        {
            var engine = MakeEngine(EnginePermissions.None);
            var path = TempFileInCwd().Replace('\\', '/');
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile($"fs.existsSync('{path}');")));
        }

        [Test]
        public void Fs_WithFileSystemPermission_DoesNotThrow()
        {
            // FileSystem = FileSystemRead | FileSystemWrite (no Escape), so the path
            // must be inside CWD to avoid the escape check.
            var engine = MakeEngine(EnginePermissions.FileSystem);
            var tmpFile = TempFileInCwd();
            try
            {
                File.WriteAllText(tmpFile, "");
                Assert.DoesNotThrow(() =>
                    engine.Execute($"fs.existsSync('{tmpFile.Replace('\\', '/')}');"));
            }
            finally
            {
                if (File.Exists(tmpFile)) File.Delete(tmpFile);
            }
        }

        [Test]
        public void Fs_ReadOnly_AllowsExistsSync()
        {
            var engine = MakeEngine(EnginePermissions.FileSystemRead);
            var tmpFile = TempFileInCwd();
            try
            {
                File.WriteAllText(tmpFile, "");
                Assert.DoesNotThrow(() =>
                    engine.Execute($"fs.existsSync('{tmpFile.Replace('\\', '/')}');"));
            }
            finally
            {
                if (File.Exists(tmpFile)) File.Delete(tmpFile);
            }
        }

        [Test]
        public void Fs_ReadOnly_BlocksWriteFileSync()
        {
            // FileSystemRead does NOT include FileSystemWrite.
            var engine = MakeEngine(EnginePermissions.FileSystemRead);
            var path = TempFileInCwd().Replace('\\', '/');
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile($"fs.writeFileSync('{path}', 'x');")));
        }

        [Test]
        public void Fs_WritePermission_AllowsReadOps()
        {
            // FileSystemWrite implies FileSystemRead, so existsSync should work.
            var engine = MakeEngine(EnginePermissions.FileSystemWrite);
            var tmpFile = TempFileInCwd();
            try
            {
                File.WriteAllText(tmpFile, "");
                Assert.DoesNotThrow(() =>
                    engine.Execute($"fs.existsSync('{tmpFile.Replace('\\', '/')}');"));
            }
            finally
            {
                if (File.Exists(tmpFile)) File.Delete(tmpFile);
            }
        }

        [Test]
        public void Fs_ReadFileSync_WithoutPermission_Throws()
        {
            var engine = MakeEngine(EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("fs.readFileSync('dummy.txt');")));
        }

        [Test]
        public void Fs_WriteFileSync_WithoutPermission_Throws()
        {
            var engine = MakeEngine(EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("fs.writeFileSync('dummy.txt', 'y');")));
        }

        [Test]
        public void Fs_AppendFileSync_WithoutPermission_Throws()
        {
            var engine = MakeEngine(EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("fs.appendFileSync('dummy.txt', 'y');")));
        }

        [Test]
        public void Fs_MkdirSync_WithoutPermission_Throws()
        {
            var engine = MakeEngine(EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("fs.mkdirSync('dummydir');")));
        }

        [Test]
        public void Fs_RmdirSync_WithoutPermission_Throws()
        {
            var engine = MakeEngine(EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("fs.rmdirSync('dummydir');")));
        }

        [Test]
        public void Fs_UnlinkSync_WithoutPermission_Throws()
        {
            var engine = MakeEngine(EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("fs.unlinkSync('dummy.txt');")));
        }

        [Test]
        public void Fs_ReaddirSync_WithoutPermission_Throws()
        {
            var engine = MakeEngine(EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("fs.readdirSync('.');")));
        }

        [Test]
        public void Fs_StatSync_WithoutPermission_Throws()
        {
            var engine = MakeEngine(EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("fs.statSync('dummy.txt');")));
        }

        // ── FileSystemEscape ────────────────────────────────────────────────────

        [Test]
        public void Fs_PathOutsideCwd_WithoutEscape_Throws()
        {
            // FileSystemRead is granted but the path escapes CWD — must throw.
            var engine = MakeEngine(EnginePermissions.FileSystemRead);
            var outsidePath = PathOutsideCwd("perm_escape_check.txt").Replace('\\', '/');
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile($"fs.existsSync('{outsidePath}');")));
        }

        [Test]
        public void Fs_PathOutsideCwd_WithEscape_DoesNotThrow()
        {
            // FileSystemUnsafe = Read | Write | Escape — the path check must pass.
            var engine = MakeEngine(EnginePermissions.FileSystemUnsafe);
            var outsidePath = PathOutsideCwd("perm_escape_check.txt").Replace('\\', '/');
            Assert.DoesNotThrow(() =>
                engine.Execute($"fs.existsSync('{outsidePath}');"));
        }

        [Test]
        public void Fs_PathInsideCwd_WithReadOnly_DoesNotEscapeCheck()
        {
            // A relative path resolved inside CWD must not trigger the escape guard
            // even without FileSystemEscape.
            var engine = MakeEngine(EnginePermissions.FileSystemRead);
            var tmpFile = TempFileInCwd();
            try
            {
                File.WriteAllText(tmpFile, "hello");
                Assert.DoesNotThrow(() =>
                    engine.Execute($"fs.existsSync('{tmpFile.Replace('\\', '/')}');"));
            }
            finally
            {
                if (File.Exists(tmpFile)) File.Delete(tmpFile);
            }
        }

        // ── Network permission ───────────────────────────────────────────────────

        [Test]
        public void Fetch_WithoutNetworkPermission_ThrowsPermissionException()
        {
            var engine = MakeEngine(EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("fetch('http://example.com');")));
        }

        // ── EnvironmentVariables split ───────────────────────────────────────────

        [Test]
        public void EnvVars_Write_ImpliesRead()
        {
            Assert.That(
                (EnginePermissions.EnvironmentVariablesWrite & EnginePermissions.EnvironmentVariablesRead),
                Is.EqualTo(EnginePermissions.EnvironmentVariablesRead));
        }

        [Test]
        public void EnvVars_EnvironmentVariables_IsAliasForRead()
        {
            Assert.That(
                EnginePermissions.EnvironmentVariables,
                Is.EqualTo(EnginePermissions.EnvironmentVariablesRead));
        }

        [Test]
        public void ProcessGetenv_WithoutEnvPermission_Throws()
        {
            var engine = MakeEngine(EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("process.getenv('PATH');")));
        }

        [Test]
        public void ProcessGetenv_WithEnvReadPermission_DoesNotThrow()
        {
            var engine = MakeEngine(EnginePermissions.EnvironmentVariablesRead);
            Assert.DoesNotThrow(() =>
                engine.Execute("process.getenv('PATH');"));
        }

        [Test]
        public void ProcessGetenv_WithEnvPermission_DoesNotThrow()
        {
            // EnvironmentVariables is the backwards-compat alias for Read.
            var engine = MakeEngine(EnginePermissions.EnvironmentVariables);
            Assert.DoesNotThrow(() =>
                engine.Execute("process.getenv('PATH');"));
        }

        [Test]
        public void ProcessSetenv_WithoutEnvWritePermission_Throws()
        {
            var engine = MakeEngine(EnginePermissions.EnvironmentVariablesRead);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("process.setenv('DSCRIPT_TEST', 'x');")));
        }

        [Test]
        public void ProcessSetenv_WithEnvWritePermission_SetsVariable()
        {
            const string varName = "DSCRIPT_PERM_TEST_SETENV";
            Environment.SetEnvironmentVariable(varName, null); // ensure clean
            try
            {
                var engine = MakeEngine(EnginePermissions.EnvironmentVariablesWrite);
                Assert.DoesNotThrow(() =>
                    engine.Execute($"process.setenv('{varName}', 'hello');"));
                Assert.That(Environment.GetEnvironmentVariable(varName), Is.EqualTo("hello"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(varName, null);
            }
        }

        [Test]
        public void ProcessSetenv_WithWritePermission_CanClearVariable()
        {
            const string varName = "DSCRIPT_PERM_TEST_CLEAR";
            Environment.SetEnvironmentVariable(varName, "initial");
            try
            {
                var engine = MakeEngine(EnginePermissions.EnvironmentVariablesWrite);
                engine.Execute($"process.setenv('{varName}', undefined);");
                Assert.That(Environment.GetEnvironmentVariable(varName), Is.Null);
            }
            finally
            {
                Environment.SetEnvironmentVariable(varName, null);
            }
        }

        // ── process.env getter ───────────────────────────────────────────────────

        [Test]
        public void ProcessEnv_WithoutReadPermission_Throws()
        {
            var engine = MakeEngine(EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("process.env;")));
        }

        [Test]
        public void ProcessEnv_WithReadPermission_DoesNotThrow()
        {
            var engine = MakeEngine(EnginePermissions.EnvironmentVariablesRead);
            Assert.DoesNotThrow(() =>
                engine.Execute("process.env;"));
        }

        [Test]
        public void ProcessEnv_WithReadPermission_ReturnsObjectWithEnvVars()
        {
            const string varName = "DSCRIPT_PERM_TEST_ENVREAD";
            Environment.SetEnvironmentVariable(varName, "testvalue");
            try
            {
                var engine = MakeEngine(EnginePermissions.EnvironmentVariablesRead);
                engine.Execute($"var envVal = process.env.{varName};");
                Assert.That(engine.Root.GetParameter("envVal").String, Is.EqualTo("testvalue"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(varName, null);
            }
        }

        // ── ProcessExit / ProcessSpawn default off ───────────────────────────────

        [Test]
        public void ProcessExit_WithoutPermission_ThrowsBeforeExiting()
        {
            // The permission check fires before Environment.Exit(), so the process
            // remains alive and the exception propagates back to the test.
            var engine = MakeEngine(EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("process.exit(0);")));
        }

        [Test]
        public void ProcessExit_DefaultNoArgPermissions_IsOff()
        {
            // RegisterFunctions() with no args must NOT grant ProcessExit.
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            Assert.That(
                (EnginePermissionStore.Get(engine) & EnginePermissions.ProcessExit),
                Is.EqualTo(EnginePermissions.None));
        }

        [Test]
        public void ProcessSpawn_DefaultNoArgPermissions_IsOff()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            Assert.That(
                (EnginePermissionStore.Get(engine) & EnginePermissions.ProcessSpawn),
                Is.EqualTo(EnginePermissions.None));
        }
    }
}
