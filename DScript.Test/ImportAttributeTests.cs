using System.Collections.Generic;
using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class ImportAttributeTests
    {
        private static ScriptEngine RunWithLoader(
            string mainSource,
            System.Func<string, string, IReadOnlyDictionary<string, string>, string> loader)
        {
            var engine = new ScriptEngine();
            engine.ModuleLoader = loader;
            var chunk = new DScriptCompiler().CompileProgram(mainSource);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine;
        }

        // ── with clause is parsed without error ───────────────────────────────

        [Test]
        public void WithClause_DefaultExport_ParsedWithoutError()
        {
            string capturedType = null;
            var engine = RunWithLoader(
                "import data from 'data.json' with { type: 'json' }; var r = 1;",
                (path, _, attrs) =>
                {
                    capturedType = attrs.TryGetValue("type", out var t) ? t : null;
                    return "__exports__.default = 42;";
                });
            Assert.That(engine.Root.GetParameter("r").Int, Is.EqualTo(1));
        }

        [Test]
        public void WithClause_AttributesReachModuleLoader()
        {
            string capturedType = null;
            RunWithLoader(
                "import data from 'data.json' with { type: 'json' }; var r = 1;",
                (path, _, attrs) =>
                {
                    capturedType = attrs.TryGetValue("type", out var t) ? t : null;
                    return "__exports__.default = 42;";
                });
            Assert.That(capturedType, Is.EqualTo("json"));
        }

        [Test]
        public void WithClause_NamedImport_AttributesReachLoader()
        {
            string capturedType = null;
            RunWithLoader(
                "import { x } from 'mod' with { type: 'json' }; var r = 1;",
                (path, _, attrs) =>
                {
                    capturedType = attrs.TryGetValue("type", out var t) ? t : null;
                    return "__exports__.x = 1;";
                });
            Assert.That(capturedType, Is.EqualTo("json"));
        }

        [Test]
        public void WithClause_StarImport_AttributesReachLoader()
        {
            string capturedType = null;
            RunWithLoader(
                "import * as ns from 'mod' with { type: 'json' }; var r = 1;",
                (path, _, attrs) =>
                {
                    capturedType = attrs.TryGetValue("type", out var t) ? t : null;
                    return "__exports__.x = 1;";
                });
            Assert.That(capturedType, Is.EqualTo("json"));
        }

        [Test]
        public void WithoutClause_LoaderReceivesEmptyAttrs()
        {
            IReadOnlyDictionary<string, string> capturedAttrs = null;
            RunWithLoader(
                "import { x } from 'mod'; var r = 1;",
                (path, _, attrs) =>
                {
                    capturedAttrs = attrs;
                    return "__exports__.x = 1;";
                });
            Assert.That(capturedAttrs, Is.Not.Null);
            Assert.That(capturedAttrs.Count, Is.EqualTo(0));
        }
    }
}
