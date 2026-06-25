using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;
using Environment = DScript.Vm.Environment;

namespace DScript.Test
{
    // NaN and Infinity are global numeric constants (typeof "number"), not undefined.
    public class GlobalConstantsTests
    {
        private static ScriptVar Run(string code, string varName)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(code);
            new VirtualMachine(engine).Run(chunk, new Environment(engine.Root, null));
            return engine.Root.GetParameter(varName);
        }

        [Test]
        public void NaN_IsNumberTyped()
        {
            Assert.That(Run("var r = typeof NaN;", "r").String, Is.EqualTo("number"));
        }

        [Test]
        public void Infinity_IsNumberTyped()
        {
            Assert.That(Run("var r = typeof Infinity;", "r").String, Is.EqualTo("number"));
        }

        [Test]
        public void NaN_IsActuallyNaN()
        {
            Assert.That(double.IsNaN(Run("var r = NaN;", "r").Float), Is.True);
        }

        [Test]
        public void Infinity_IsPositiveInfinity()
        {
            Assert.That(Run("var r = Infinity;", "r").Float, Is.EqualTo(double.PositiveInfinity));
        }

        [Test]
        public void NegativeInfinity_ViaNegation()
        {
            Assert.That(Run("var r = -Infinity;", "r").Float, Is.EqualTo(double.NegativeInfinity));
        }

        [Test]
        public void Infinity_ComparesGreaterThanLargeDouble()
        {
            Assert.That(Run("var r = Infinity > 1e308;", "r").Bool, Is.True);
        }
    }
}
