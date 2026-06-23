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
using System.Runtime.InteropServices;
using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class ChildProcessTests
    {
        private static ScriptEngine MakeEngine()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            return engine;
        }

        // Platform-agnostic echo command
        private static string EchoCmd(string text) =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? $"echo {text}"
                : $"echo '{text}'";

        // --- execSync ---

        [Test]
        public void ExecSync_ReturnsStdout()
        {
            var engine = MakeEngine();
            engine.Execute($"var out = child_process.execSync({Quote(EchoCmd("hello"))});");
            var result = engine.Root.FindChild("out")?.Var?.String?.Trim();
            Assert.That(result, Does.Contain("hello"));
        }

        [Test]
        public void ExecSync_ThrowsOnNonZeroExit()
        {
            var engine = MakeEngine();
            var cmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "exit 1"
                : "false";
            Assert.Throws<ScriptException>(() =>
                engine.Run(ScriptEngine.Compile($"child_process.execSync({Quote(cmd)});")));
        }

        [Test]
        public void ExecSync_WithoutPermission_ThrowsPermissionException()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine, EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("child_process.execSync('echo hi');")));
        }

        // --- spawnSync ---

        [Test]
        public void SpawnSync_ReturnsResultObject()
        {
            var engine = MakeEngine();
            var file = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "echo";
            var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "['/c', 'echo', 'hi']" : "['hi']";
            engine.Execute($"var r = child_process.spawnSync({Quote(file)}, {args});");
            var root = engine.Root;
            Assert.That(root.FindChild("r"), Is.Not.Null);
            var rVar = root.FindChild("r").Var;
            Assert.That(rVar.FindChild("status"), Is.Not.Null);
            Assert.That(rVar.FindChild("stdout"), Is.Not.Null);
            Assert.That(rVar.FindChild("stderr"), Is.Not.Null);
        }

        [Test]
        public void SpawnSync_StatusZeroOnSuccess()
        {
            var engine = MakeEngine();
            var file = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "true";
            var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "['/c', 'exit', '0']" : "[]";
            engine.Execute($"var r = child_process.spawnSync({Quote(file)}, {args});");
            Assert.That(engine.Root.FindChild("r").Var.FindChild("status").Var.Int, Is.EqualTo(0));
        }

        [Test]
        public void SpawnSync_WithoutPermission_ThrowsPermissionException()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine, EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("child_process.spawnSync('echo', ['hi']);")));
        }

        // --- exec ---

        [Test]
        public void Exec_CallsCallbackWithStdout()
        {
            var engine = MakeEngine();
            engine.Execute($@"
                var result = null;
                child_process.exec({Quote(EchoCmd("hello"))}, function(err, stdout, stderr) {{
                    result = stdout;
                }});
            ");
            var result = engine.Root.FindChild("result")?.Var?.String?.Trim();
            Assert.That(result, Does.Contain("hello"));
        }

        [Test]
        public void Exec_CallsCallbackWithNullErrOnSuccess()
        {
            var engine = MakeEngine();
            engine.Execute($@"
                var errVal = 'not-called';
                child_process.exec({Quote(EchoCmd("hi"))}, function(err, stdout, stderr) {{
                    errVal = err;
                }});
            ");
            var errVar = engine.Root.FindChild("errVal")?.Var;
            Assert.That(errVar, Is.Not.Null);
            Assert.That(errVar.IsNull, Is.True);
        }

        [Test]
        public void Exec_WithoutPermission_ThrowsPermissionException()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine, EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("child_process.exec('echo hi', function(){});")));
        }

        // --- spawn ---

        [Test]
        public void Spawn_ReturnsPidProperty()
        {
            var engine = MakeEngine();
            var file = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "echo";
            var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "['/c', 'echo', 'hi']" : "['hi']";
            engine.Execute($"var p = child_process.spawn({Quote(file)}, {args});");
            var pidVar = engine.Root.FindChild("p")?.Var?.FindChild("pid")?.Var;
            Assert.That(pidVar, Is.Not.Null);
            Assert.That(pidVar.Int, Is.GreaterThan(0));
        }

        [Test]
        public void Spawn_OnExitCallbackReceivesExitCode()
        {
            var engine = MakeEngine();
            var file = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "true";
            var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "['/c', 'exit', '0']" : "[]";
            engine.Execute($@"
                var exitCode = -99;
                var p = child_process.spawn({Quote(file)}, {args});
                p.on('exit', function(code) {{ exitCode = code; }});
            ");
            Assert.That(engine.Root.FindChild("exitCode")?.Var?.Int, Is.EqualTo(0));
        }

        [Test]
        public void Spawn_WithoutPermission_ThrowsPermissionException()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine, EnginePermissions.None);
            Assert.Throws<PermissionException>(() =>
                engine.Run(ScriptEngine.Compile("child_process.spawn('echo', ['hi']);")));
        }

        private static string Quote(string s) => $"'{s.Replace("'", "\\'")}'";
    }
}
