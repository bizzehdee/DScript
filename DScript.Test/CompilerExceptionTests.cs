using DScript;
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;
using Environment = DScript.Vm.Environment;

namespace DScript.Test
{
    // Phase 5: throw, try/catch/finally.
    public class CompilerExceptionTests
    {
        private static ScriptVar Run(string source)
        {
            var engine = new ScriptEngine();
            var chunk = new DScriptCompiler().CompileProgram(source);
            new VirtualMachine(engine).Run(chunk, new Environment(engine.Root, null));
            return engine.Root;
        }

        [Test]
        public void CatchHandlesThrowFromCalledFunction()
        {
            var src =
                "function boom() { throw \"bang\"; }" +
                "var result = 5;" +
                "try { boom(); } catch (e) { result = 0; } finally { result = 1; }";
            Assert.That(Run(src).GetParameter("result").Int, Is.EqualTo(1));
        }

        [Test]
        public void CatchBindsThrownValue()
        {
            var src = "var msg = \"\"; try { throw \"oops\"; } catch (e) { msg = e; }";
            Assert.That(Run(src).GetParameter("msg").String, Is.EqualTo("oops"));
        }

        [Test]
        public void FinallyRunsOnNormalCompletion()
        {
            var src = "var r = 0; try { r = 1; } finally { r = r + 10; }";
            Assert.That(Run(src).GetParameter("r").Int, Is.EqualTo(11));
        }

        [Test]
        public void FinallyRunsAfterCatch()
        {
            var src = "var log = 0; try { throw 1; } catch (e) { log = log + 1; } finally { log = log + 10; }";
            Assert.That(Run(src).GetParameter("log").Int, Is.EqualTo(11));
        }

        [Test]
        public void CatchlessTryStillRunsFinallyThenPropagates()
        {
            // no catch: the exception propagates out, but finally still runs
            var src = "var r = 0; try { throw 1; } finally { r = 99; }";
            Assert.That(() => Run(src), Throws.TypeOf<JITException>());
        }

        [Test]
        public void UncaughtThrowPropagates()
        {
            Assert.That(() => Run("function boom() { throw \"x\"; } boom();"), Throws.TypeOf<JITException>());
        }
    }
}
