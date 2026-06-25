using System.Text;
using DScript;
using DScript.Extras;
using DScript.Extras.FunctionProviders;
using NUnit.Framework;

namespace DScript.Test
{
    // console.log (and error/warn/info/debug) accept multiple arguments and print
    // them space-separated — implemented via native rest parameters (`...args`).
    // Previously only the first argument was printed.
    [TestFixture]
    [NonParallelizable]
    public class ConsoleMultiArgTests
    {
        private static string Capture(string code)
        {
            var sb = new StringBuilder();
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            ConsoleFunctionProvider.SetOutput(s => sb.Append(s).Append('\n'), null);
            try { engine.Execute(code); }
            finally { ConsoleFunctionProvider.ResetOutput(); }
            return sb.ToString();
        }

        [Test]
        public void LogTwoArgs()
            => Assert.That(Capture("console.log('Checksum:', 12345);").TrimEnd(), Is.EqualTo("\"Checksum:\" 12345"));

        [Test]
        public void LogThreeArgs()
            => Assert.That(Capture("console.log(1, 2, 3);").TrimEnd(), Is.EqualTo("1 2 3"));

        [Test]
        public void LogSingleArg_Unchanged()
            => Assert.That(Capture("console.log('single');").TrimEnd(), Is.EqualTo("\"single\""));

        [Test]
        public void LogNoArgs_PrintsBlankLine()
            => Assert.That(Capture("console.log();").TrimEnd('\r', '\n'), Is.EqualTo(string.Empty));

        [Test]
        public void LogMixedTypes()
            => Assert.That(Capture("console.log('x', 1, 2.5);").TrimEnd(), Is.EqualTo("\"x\" 1 2.5"));

        [Test]
        public void InfoPrefixWithMultipleArgs()
            => Assert.That(Capture("console.info('a', 'b');").TrimEnd(), Is.EqualTo("[INFO] \"a\" \"b\""));

        private static string CaptureErr(string code)
        {
            var sb = new StringBuilder();
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            ConsoleFunctionProvider.SetOutput(null, s => sb.Append(s).Append('\n'));
            try { engine.Execute(code); }
            finally { ConsoleFunctionProvider.ResetOutput(); }
            return sb.ToString();
        }

        [Test]
        public void ErrorMultipleArgs()
            => Assert.That(CaptureErr("console.error('oops', 42);").TrimEnd(), Is.EqualTo("\"oops\" 42"));
    }
}
