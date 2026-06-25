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

        // ── stack trace ───────────────────────────────────────────────────

        [Test]
        public void UncaughtThrowCarriesInnermostFrame()
        {
            var ex = Assert.Throws<JITException>(() =>
                Run("throw \"oops\";"));

            Assert.That(ex.ScriptStackTrace.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(ex.ScriptStackTrace[0].Line, Is.EqualTo(1));
        }

        [Test]
        public void StackTraceBuildsAcrossCallFrames()
        {
            // boom() is on line 1, the call is on line 2.
            var ex = Assert.Throws<JITException>(() =>
                Run("function boom() { throw \"x\"; }\nboom();"));

            // Innermost frame: inside boom (line 1).
            Assert.That(ex.ScriptStackTrace[0].Source, Is.EqualTo("boom"));
            Assert.That(ex.ScriptStackTrace[0].Line, Is.EqualTo(1));

            // Outer frame: <main> at the call site (line 2).
            Assert.That(ex.ScriptStackTrace[1].Source, Is.EqualTo("<main>"));
            Assert.That(ex.ScriptStackTrace[1].Line, Is.EqualTo(2));
        }

        [Test]
        public void StackTraceIncludesNestedCalls()
        {
            const string src =
                "function inner() { throw \"deep\"; }\n" +   // line 1
                "function outer() { inner(); }\n" +           // line 2
                "outer();";                                   // line 3

            var ex = Assert.Throws<JITException>(() => Run(src));

            Assert.That(ex.ScriptStackTrace.Count, Is.EqualTo(3));
            Assert.That(ex.ScriptStackTrace[0].Source, Is.EqualTo("inner"));
            Assert.That(ex.ScriptStackTrace[1].Source, Is.EqualTo("outer"));
            Assert.That(ex.ScriptStackTrace[2].Source, Is.EqualTo("<main>"));
        }

        [Test]
        public void ScriptExceptionCarriesStackTrace()
        {
            // Calling a non-function raises a catchable TypeError (JITException), which
            // still accumulates the script stack trace as it unwinds.
            var ex = Assert.Throws<JITException>(() =>
                Run("function call_nonfunction() { var x = 1; x(); }\ncall_nonfunction();"));

            Assert.That(ex.ScriptStackTrace.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(ex.ScriptStackTrace[0].Source, Is.EqualTo("call_nonfunction"));
        }

        [Test]
        public void CallingNonFunction_IsCatchableTypeError()
        {
            // The TypeError must be catchable by script-level try/catch (it used to be
            // an uncatchable host ScriptException that aborted execution).
            var r = Run("var caught = ''; try { var x = 1; x(); } catch (e) { caught = e.name; } result = caught;");
            Assert.That(r.GetParameter("result").String, Is.EqualTo("TypeError"));
        }

        [Test]
        public void ToStringIncludesFrames()
        {
            var ex = Assert.Throws<JITException>(() =>
                Run("function f() { throw \"err\"; }\nf();"));

            var text = ex.ToString();
            Assert.That(text, Does.Contain("at f"));
            Assert.That(text, Does.Contain("at <main>"));
        }

        // ── optional catch binding ────────────────────────────────────────────

        [Test]
        public void OptionalCatchBinding_CatchWithoutBinding_DoesNotThrow()
        {
            var src = "var r = 0; try { throw 1; } catch { r = 99; }";
            Assert.That(Run(src).GetParameter("r").Int, Is.EqualTo(99));
        }

        [Test]
        public void OptionalCatchBinding_EmptyParens_DoesNotThrow()
        {
            var src = "var r = 0; try { throw 1; } catch () { r = 42; }";
            Assert.That(Run(src).GetParameter("r").Int, Is.EqualTo(42));
        }

        [Test]
        public void OptionalCatchBinding_FinallyStillRuns()
        {
            var src = "var r = 0; try { throw 1; } catch { r = 1; } finally { r = r + 10; }";
            Assert.That(Run(src).GetParameter("r").Int, Is.EqualTo(11));
        }
    }
}
