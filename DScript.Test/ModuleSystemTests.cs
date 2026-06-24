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
    /// <summary>
    /// Tests for the module system: <c>require()</c>, module caching, <c>export</c>
    /// declarations, <c>import</c> statements, module scope isolation, and circular requires.
    /// </summary>
    public class ModuleSystemTests
    {
        private static ScriptEngine RunWithModules(string mainSource, Dictionary<string, string> modules = null)
        {
            var engine = new ScriptEngine();
            if (modules != null)
                engine.ModuleLoader = (path, _, __) => modules.TryGetValue(path, out var src) ? src : null;
            var chunk = ScriptEngine.Compile(mainSource);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine;
        }

        private static int RunInt(string mainSource, Dictionary<string, string> modules = null)
            => RunWithModules(mainSource, modules).Root.GetParameter("r").Int;

        private static string RunStr(string mainSource, Dictionary<string, string> modules = null)
            => RunWithModules(mainSource, modules).Root.GetParameter("r").String;

        // ── require() ─────────────────────────────────────────────────────────

        [Test]
        public void Require_DirectExportsAssignment_ReturnsValue()
        {
            var modules = new Dictionary<string, string> { ["math"] = "__exports__.x = 42;" };
            Assert.That(RunInt("var r = require('math').x;", modules), Is.EqualTo(42));
        }

        [Test]
        public void Require_CachesModule_LoaderCalledOnce()
        {
            var callCount = 0;
            var engine = new ScriptEngine();
            engine.ModuleLoader = (path, _, __) => { callCount++; return "__exports__.x = 1;"; };

            var chunk = ScriptEngine.Compile("var a = require('m'); var b = require('m'); var r = (a === b) ? 1 : 0;");
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));

            Assert.That(callCount, Is.EqualTo(1), "module loader should be called exactly once");
            Assert.That(engine.Root.GetParameter("r").Int, Is.EqualTo(1));
        }

        [Test]
        public void Require_MissingModule_ThrowsScriptException()
        {
            var engine = new ScriptEngine();
            engine.ModuleLoader = (_, __, ___) => null;
            var chunk = ScriptEngine.Compile("require('missing');");
            Assert.Throws<ScriptException>(() =>
                new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null)));
        }

        [Test]
        public void Require_NoModuleLoader_ThrowsScriptException()
        {
            var engine = new ScriptEngine();
            var chunk = ScriptEngine.Compile("require('nope');");
            Assert.Throws<ScriptException>(() =>
                new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null)));
        }

        // ── export declarations ───────────────────────────────────────────────

        [Test]
        public void Export_Var_ExposesValueOnExportsObject()
        {
            var modules = new Dictionary<string, string> { ["vals"] = "export var PI = 314;" };
            Assert.That(RunInt("var r = require('vals').PI;", modules), Is.EqualTo(314));
        }

        [Test]
        public void Export_Const_ExposesValueOnExportsObject()
        {
            var modules = new Dictionary<string, string> { ["c"] = "export const ANSWER = 42;" };
            Assert.That(RunInt("var r = require('c').ANSWER;", modules), Is.EqualTo(42));
        }

        [Test]
        public void Export_Function_ExposesCallableOnExportsObject()
        {
            var modules = new Dictionary<string, string> { ["funcs"] = "export function add(a, b) { return a + b; }" };
            Assert.That(RunInt("var mod = require('funcs'); var r = mod.add(3, 4);", modules), Is.EqualTo(7));
        }

        [Test]
        public void Export_Default_ExposesDefaultProperty()
        {
            var modules = new Dictionary<string, string> { ["def"] = "export default 99;" };
            Assert.That(RunInt("var r = require('def').default;", modules), Is.EqualTo(99));
        }

        [Test]
        public void Export_DefaultExpression_ExposesComputedResult()
        {
            var modules = new Dictionary<string, string> { ["expr"] = "export default 2 + 3;" };
            Assert.That(RunInt("var r = require('expr').default;", modules), Is.EqualTo(5));
        }

        // ── import statement ──────────────────────────────────────────────────

        [Test]
        public void Import_NamedBindings_BindsLocals()
        {
            var modules = new Dictionary<string, string> { ["abc"] = "export var x = 10; export var y = 20;" };
            Assert.That(RunInt("import { x, y } from 'abc'; var r = x + y;", modules), Is.EqualTo(30));
        }

        [Test]
        public void Import_NamedBindingWithAlias_BindsAlias()
        {
            var modules = new Dictionary<string, string> { ["ab2"] = "export var foo = 7;" };
            Assert.That(RunInt("import { foo as bar } from 'ab2'; var r = bar;", modules), Is.EqualTo(7));
        }

        [Test]
        public void Import_StarAs_BindsNamespaceObject()
        {
            var modules = new Dictionary<string, string> { ["ns"] = "export var a = 5; export var b = 6;" };
            Assert.That(RunInt("import * as ns from 'ns'; var r = ns.a + ns.b;", modules), Is.EqualTo(11));
        }

        [Test]
        public void Import_Default_BindsDefaultExport()
        {
            var modules = new Dictionary<string, string> { ["dmod"] = "export default 77;" };
            Assert.That(RunInt("import dval from 'dmod'; var r = dval;", modules), Is.EqualTo(77));
        }

        // ── module isolation ──────────────────────────────────────────────────

        [Test]
        public void Module_LocalVars_DoNotLeakIntoCallerScope()
        {
            var modules = new Dictionary<string, string> { ["iso"] = "var secret = 'hidden'; __exports__.pub = 42;" };
            var engine = RunWithModules("require('iso'); var r = (typeof secret === 'undefined') ? 1 : 0;", modules);
            Assert.That(engine.Root.GetParameter("r").Int, Is.EqualTo(1));
        }

        // ── circular require ──────────────────────────────────────────────────

        [Test]
        public void Require_Circular_DoesNotInfiniteLoop()
        {
            var modules = new Dictionary<string, string>
            {
                ["circ_a"] = "__exports__.done = true; var b = require('circ_b');",
                ["circ_b"] = "var a = require('circ_a'); __exports__.ok = 1;"
            };
            Assert.DoesNotThrow(() =>
            {
                var engine = new ScriptEngine();
                engine.ModuleLoader = (path, _, __) => modules.TryGetValue(path, out var s) ? s : null;
                var chunk = ScriptEngine.Compile("var r = require('circ_b').ok;");
                new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
                Assert.That(engine.Root.GetParameter("r").Int, Is.EqualTo(1));
            });
        }

        // ── module object ──────────────────────────────────────────────────────

        [Test]
        public void Module_ExportsPropertyAliasesExports()
        {
            var modules = new Dictionary<string, string>
            {
                ["mexp"] = "module.exports.x = 99;"
            };
            Assert.That(RunInt("var r = require('mexp').x;", modules), Is.EqualTo(99));
        }

        [Test]
        public void Module_FilenameIsModulePath()
        {
            var modules = new Dictionary<string, string>
            {
                ["mymod"] = "exports.fn = __filename;"
            };
            Assert.That(RunStr("var r = require('mymod').fn;", modules), Is.EqualTo("mymod"));
        }

        [Test]
        public void Module_FilenamePropertyOnModuleObject()
        {
            var modules = new Dictionary<string, string>
            {
                ["lmod"] = "exports.fn = module.filename;"
            };
            Assert.That(RunStr("var r = require('lmod').fn;", modules), Is.EqualTo("lmod"));
        }

        [Test]
        public void Module_ExportsShorthandWorks()
        {
            var modules = new Dictionary<string, string>
            {
                ["sh"] = "exports.value = 42;"
            };
            Assert.That(RunInt("var r = require('sh').value;", modules), Is.EqualTo(42));
        }

        [Test]
        public void Module_ReplaceExports_ReturnsNewObject()
        {
            var modules = new Dictionary<string, string>
            {
                ["fn_mod"] = "module.exports = function() { return 77; };"
            };
            Assert.That(RunInt("var fn = require('fn_mod'); var r = fn();", modules), Is.EqualTo(77));
        }

        // ── __filename / __dirname ─────────────────────────────────────────────

        [Test]
        public void Filename_IsAvailableInsideModule()
        {
            var modules = new Dictionary<string, string>
            {
                ["dir/file.js"] = "exports.name = __filename;"
            };
            Assert.That(RunStr("var r = require('dir/file.js').name;", modules), Is.EqualTo("dir/file.js"));
        }

        [Test]
        public void Dirname_IsDirectoryPartOfModulePath()
        {
            var modules = new Dictionary<string, string>
            {
                ["dir/sub/file.js"] = "exports.dir = __dirname;"
            };
            Assert.That(RunStr("var r = require('dir/sub/file.js').dir;", modules), Is.EqualTo("dir/sub"));
        }

        [Test]
        public void Dirname_FallsBackToDotForPathWithoutSlash()
        {
            var modules = new Dictionary<string, string>
            {
                ["flat"] = "exports.dir = __dirname;"
            };
            Assert.That(RunStr("var r = require('flat').dir;", modules), Is.EqualTo("."));
        }
    }
}
