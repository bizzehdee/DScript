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
    /// <summary>Tests for Phase 9: module system (require / export / import).</summary>
    public class Phase9Tests
    {
        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static ScriptEngine RunWithModules(string mainSource,
            Dictionary<string, string> modules = null)
        {
            var engine = new ScriptEngine();
            if (modules != null)
            {
                engine.ModuleLoader = (path, _) =>
                    modules.TryGetValue(path, out var src) ? src : null;
            }
            var chunk = ScriptEngine.Compile(mainSource);
            var vm = new VirtualMachine(engine);
            vm.Run(chunk, new Vm.Environment(engine.Root, null));
            return engine;
        }

        private static int RunInt(string mainSource, Dictionary<string, string> modules = null)
            => RunWithModules(mainSource, modules).Root.GetParameter("r").Int;

        private static string RunStr(string mainSource, Dictionary<string, string> modules = null)
            => RunWithModules(mainSource, modules).Root.GetParameter("r").String;

        private static bool RunBool(string mainSource, Dictionary<string, string> modules = null)
            => RunWithModules(mainSource, modules).Root.GetParameter("r").Bool;

        // ------------------------------------------------------------------
        // 1. Basic require()
        // ------------------------------------------------------------------

        [Test]
        public void Require_DirectExportsAssignment_ReturnsValue()
        {
            var modules = new Dictionary<string, string>
            {
                ["math"] = "__exports__.x = 42;"
            };
            Assert.That(RunInt("var r = require('math').x;", modules), Is.EqualTo(42));
        }

        [Test]
        public void Require_CachesModule_LoaderCalledOnce()
        {
            var callCount = 0;
            var engine = new ScriptEngine();
            engine.ModuleLoader = (path, _) => { callCount++; return "__exports__.x = 1;"; };

            var chunk = ScriptEngine.Compile("var a = require('m'); var b = require('m'); var r = (a === b) ? 1 : 0;");
            var vm = new VirtualMachine(engine);
            vm.Run(chunk, new Vm.Environment(engine.Root, null));

            Assert.That(callCount, Is.EqualTo(1), "Module loader should be called exactly once");
            Assert.That(engine.Root.GetParameter("r").Int, Is.EqualTo(1));
        }

        [Test]
        public void Require_MissingModule_ThrowsScriptException()
        {
            var engine = new ScriptEngine();
            engine.ModuleLoader = (_, __) => null;
            var chunk = ScriptEngine.Compile("require('missing');");
            var vm = new VirtualMachine(engine);
            Assert.Throws<ScriptException>(() =>
                vm.Run(chunk, new Vm.Environment(engine.Root, null)));
        }

        [Test]
        public void Require_NoModuleLoader_ThrowsScriptException()
        {
            var engine = new ScriptEngine();
            // ModuleLoader is null by default
            var chunk = ScriptEngine.Compile("require('nope');");
            var vm = new VirtualMachine(engine);
            Assert.Throws<ScriptException>(() =>
                vm.Run(chunk, new Vm.Environment(engine.Root, null)));
        }

        // ------------------------------------------------------------------
        // 2. export var / function / default
        // ------------------------------------------------------------------

        [Test]
        public void Export_Var_ExposesValueOnExportsObject()
        {
            var modules = new Dictionary<string, string>
            {
                ["vals"] = "export var PI = 314;"
            };
            Assert.That(RunInt("var r = require('vals').PI;", modules), Is.EqualTo(314));
        }

        [Test]
        public void Export_Const_ExposesValueOnExportsObject()
        {
            var modules = new Dictionary<string, string>
            {
                ["c"] = "export const ANSWER = 42;"
            };
            Assert.That(RunInt("var r = require('c').ANSWER;", modules), Is.EqualTo(42));
        }

        [Test]
        public void Export_Function_ExposesCallableOnExportsObject()
        {
            var modules = new Dictionary<string, string>
            {
                ["funcs"] = "export function add(a, b) { return a + b; }"
            };
            Assert.That(RunInt("var mod = require('funcs'); var r = mod.add(3, 4);", modules),
                Is.EqualTo(7));
        }

        [Test]
        public void Export_Default_ExposesDefaultProperty()
        {
            var modules = new Dictionary<string, string>
            {
                ["def"] = "export default 99;"
            };
            Assert.That(RunInt("var r = require('def').default;", modules), Is.EqualTo(99));
        }

        [Test]
        public void Export_DefaultExpression_ExposesResult()
        {
            var modules = new Dictionary<string, string>
            {
                ["expr"] = "export default 2 + 3;"
            };
            Assert.That(RunInt("var r = require('expr').default;", modules), Is.EqualTo(5));
        }

        // ------------------------------------------------------------------
        // 3. import statement
        // ------------------------------------------------------------------

        [Test]
        public void Import_NamedBindings_BindsLocals()
        {
            var modules = new Dictionary<string, string>
            {
                ["abc"] = "export var x = 10; export var y = 20;"
            };
            Assert.That(RunInt("import { x, y } from 'abc'; var r = x + y;", modules),
                Is.EqualTo(30));
        }

        [Test]
        public void Import_NamedBindingWithAlias_BindsAlias()
        {
            var modules = new Dictionary<string, string>
            {
                ["ab2"] = "export var foo = 7;"
            };
            Assert.That(RunInt("import { foo as bar } from 'ab2'; var r = bar;", modules),
                Is.EqualTo(7));
        }

        [Test]
        public void Import_StarAs_BindsNamespaceObject()
        {
            var modules = new Dictionary<string, string>
            {
                ["ns"] = "export var a = 5; export var b = 6;"
            };
            Assert.That(RunInt("import * as ns from 'ns'; var r = ns.a + ns.b;", modules),
                Is.EqualTo(11));
        }

        [Test]
        public void Import_Default_BindsDefaultExport()
        {
            var modules = new Dictionary<string, string>
            {
                ["dmod"] = "export default 77;"
            };
            Assert.That(RunInt("import dval from 'dmod'; var r = dval;", modules), Is.EqualTo(77));
        }

        // ------------------------------------------------------------------
        // 4. Module isolation
        // ------------------------------------------------------------------

        [Test]
        public void Module_LocalVars_DoNotLeakIntoCallerScope()
        {
            var modules = new Dictionary<string, string>
            {
                ["iso"] = "var secret = 'hidden'; __exports__.pub = 42;"
            };
            var engine = RunWithModules("require('iso'); var r = (typeof secret === 'undefined') ? 1 : 0;",
                modules);
            Assert.That(engine.Root.GetParameter("r").Int, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------
        // 5. Circular require
        // ------------------------------------------------------------------

        [Test]
        public void Require_Circular_DoesNotInfiniteLoop()
        {
            // circ_a exports done=true, then requires circ_b.
            // circ_b requires circ_a (gets the pre-seeded, partially-filled exports) and
            // then sets its own ok=1.  Neither module should loop forever.
            var modules = new Dictionary<string, string>
            {
                ["circ_a"] = "__exports__.done = true; var b = require('circ_b');",
                ["circ_b"] = "var a = require('circ_a'); __exports__.ok = 1;"
            };
            Assert.DoesNotThrow(() =>
            {
                var engine = new ScriptEngine();
                engine.ModuleLoader = (path, _) => modules.TryGetValue(path, out var s) ? s : null;
                var chunk = ScriptEngine.Compile("var r = require('circ_b').ok;");
                var vm = new VirtualMachine(engine);
                vm.Run(chunk, new Vm.Environment(engine.Root, null));
                Assert.That(engine.Root.GetParameter("r").Int, Is.EqualTo(1));
            });
        }
    }
}
