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

using System.Collections.Generic;
using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class DynamicImportTests
    {
        private static ScriptEngine RunWithModules(string mainSource, Dictionary<string, string> modules)
        {
            var engine = new ScriptEngine();
            engine.ModuleLoader = (path, _, __) => modules.TryGetValue(path, out var src) ? src : null;
            var chunk = new DScriptCompiler().CompileProgram(mainSource);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            ScriptEngine.DrainMicroTasks();
            return engine;
        }

        private static int RunInt(string mainSource, Dictionary<string, string> modules)
            => RunWithModules(mainSource, modules).Root.GetParameter("r").Int;

        private static string RunStr(string mainSource, Dictionary<string, string> modules)
            => RunWithModules(mainSource, modules).Root.GetParameter("r").String;

        // --- Happy path: import resolves with module exports ---

        [Test]
        public void DynamicImport_ResolvesWithExports()
        {
            var modules = new Dictionary<string, string>
            {
                ["math"] = "export var answer = 42;"
            };
            var r = RunInt(@"
var r = 0;
import('math').then(function(m) { r = m.answer; });
", modules);
            Assert.That(r, Is.EqualTo(42));
        }

        [Test]
        public void DynamicImport_CanBeAwaited()
        {
            var modules = new Dictionary<string, string>
            {
                ["util"] = "export var val = 7;"
            };
            var r = RunInt(@"
var r = 0;
async function load() {
    var m = await import('util');
    r = m.val;
}
load();
", modules);
            Assert.That(r, Is.EqualTo(7));
        }

        [Test]
        public void DynamicImport_WithDynamicSpecifier()
        {
            var modules = new Dictionary<string, string>
            {
                ["a"] = "export var x = 1;",
                ["b"] = "export var x = 2;"
            };
            var r = RunInt(@"
var r = 0;
var name = 'b';
import(name).then(function(m) { r = m.x; });
", modules);
            Assert.That(r, Is.EqualTo(2));
        }

        // --- Module caching: same module not loaded twice ---

        [Test]
        public void DynamicImport_UsesModuleCache()
        {
            var callCount = 0;
            var engine = new ScriptEngine();
            engine.ModuleLoader = (path, _, __) =>
            {
                callCount++;
                return "export var n = 1;";
            };
            var chunk = new DScriptCompiler().CompileProgram(@"
import('mod').then(function() {});
import('mod').then(function() {});
");
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            ScriptEngine.DrainMicroTasks();
            Assert.That(callCount, Is.EqualTo(1));
        }

        // --- Missing module rejects the Promise ---

        [Test]
        public void DynamicImport_MissingModule_RejectsPromise()
        {
            var engine = new ScriptEngine();
            engine.ModuleLoader = (_, __, ___) => null;  // always missing
            var chunk = new DScriptCompiler().CompileProgram(@"
var r = 0;
import('missing').catch(function(e) { r = 99; });
");
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            ScriptEngine.DrainMicroTasks();
            var r = engine.Root.GetParameter("r").Int;
            Assert.That(r, Is.EqualTo(99));
        }

        // --- import() returns a Promise ---

        [Test]
        public void DynamicImport_ReturnsPromise()
        {
            var modules = new Dictionary<string, string> { ["m"] = "" };
            var engine = new ScriptEngine();
            engine.ModuleLoader = (path, _, __) => modules.TryGetValue(path, out var src) ? src : null;
            var chunk = new DScriptCompiler().CompileProgram(@"
var p = import('m');
var r = (p !== null && p !== undefined) ? 1 : 0;
");
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            ScriptEngine.DrainMicroTasks();
            Assert.That(engine.Root.GetParameter("r").Int, Is.EqualTo(1));
        }
    }
}
