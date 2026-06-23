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
    public class ImportMetaTests
    {
        private static string RunStr(string source, string modulePath = "")
        {
            var engine = new ScriptEngine();
            engine.CurrentModulePath = modulePath;
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r").String;
        }

        private static ScriptVar RunVar(string source, string modulePath = "")
        {
            var engine = new ScriptEngine();
            engine.CurrentModulePath = modulePath;
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r");
        }

        // --- import.meta is an object ---

        [Test]
        public void ImportMeta_IsAnObject()
        {
            var r = RunVar("var r = typeof import.meta;");
            Assert.That(r.String, Is.EqualTo("object"));
        }

        // --- import.meta.url reflects CurrentModulePath ---

        [Test]
        public void ImportMeta_Url_ReturnsCurrentModulePath()
        {
            var r = RunStr("var r = import.meta.url;", "/app/main.js");
            Assert.That(r, Is.EqualTo("/app/main.js"));
        }

        [Test]
        public void ImportMeta_Filename_ReturnsCurrentModulePath()
        {
            var r = RunStr("var r = import.meta.filename;", "/app/main.js");
            Assert.That(r, Is.EqualTo("/app/main.js"));
        }

        // --- import.meta.dirname is the directory portion ---

        [Test]
        public void ImportMeta_Dirname_ReturnsDirectoryOfModulePath()
        {
            var r = RunStr("var r = import.meta.dirname;",
                System.IO.Path.Combine("/app", "main.js"));
            // Should be the directory part only
            Assert.That(r, Does.Contain("app"));
            Assert.That(r, Does.Not.Contain("main.js"));
        }

        // --- empty path when no module context ---

        [Test]
        public void ImportMeta_Url_IsEmptyWhenNoModuleContext()
        {
            var r = RunStr("var r = import.meta.url;");
            Assert.That(r, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ImportMeta_Dirname_IsEmptyWhenNoModuleContext()
        {
            var r = RunStr("var r = import.meta.dirname;");
            Assert.That(r, Is.EqualTo(string.Empty));
        }

        // --- import.meta inside a required module ---

        [Test]
        public void ImportMeta_InsideModule_ReflectsModulePath()
        {
            var engine = new ScriptEngine();
            engine.ModuleLoader = (path, _) =>
            {
                if (path == "mymod") return "export var url = import.meta.url;";
                return null;
            };
            var chunk = new DScriptCompiler().CompileProgram(
                "import { url } from 'mymod'; var r = url;");
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            var r = engine.Root.GetParameter("r").String;
            Assert.That(r, Is.EqualTo("mymod"));
        }

        // --- compile-time: import.meta without .meta throws ---

        [Test]
        public void ImportMeta_WithoutMetaProperty_ThrowsAtCompileTime()
        {
            Assert.Throws<JITException>(() =>
            {
                var _ = new DScriptCompiler().CompileProgram("var r = import.url;");
            });
        }
    }
}
