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
using System.Collections.Generic;
using DScript.Extras;
using DScript.Extras.FunctionProviders;
using NUnit.Framework;

namespace DScript.Test
{
    /// <summary>
    /// Tests for DScript.Extras console.* methods.
    ///
    /// <see cref="ConsoleFunctionProvider.SetOutput"/> is used to inject capturing
    /// delegates so that stdout and stderr output can be verified without relying
    /// on Console stream redirection (which NUnit 4 intercepts for its own capture).
    ///
    /// The fixture is <see cref="NonParallelizableAttribute"/> because
    /// ConsoleFunctionProvider holds shared static state (_indentLevel, _timers,
    /// _counters, _stdout, _stderr).
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class ConsoleExtrasTests
    {
        private readonly List<string> _out = new List<string>();
        private readonly List<string> _err = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _out.Clear();
            _err.Clear();
            ConsoleFunctionProvider.SetOutput(
                stdout: line => _out.Add(line),
                stderr: line => _err.Add(line));
        }

        [TearDown]
        public void TearDown()
        {
            ConsoleFunctionProvider.ResetOutput();
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static ScriptEngine CreateEngine()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            return engine;
        }

        private void RunScript(string code) => CreateEngine().Execute(code);

        private string StdOut => string.Join(Environment.NewLine, _out);
        private string StdErr => string.Join(Environment.NewLine, _err);

        // ------------------------------------------------------------------
        // console.log
        // ------------------------------------------------------------------

        [Test]
        public void ConsoleLog_WritesValueToStdOut()
        {
            RunScript("console.log(\"hello\");");
            // GetParsableString preserves JS string quoting, so check for content.
            Assert.That(StdOut, Does.Contain("hello"));
        }

        [Test]
        public void ConsoleLog_NumericValue_WritesNumberToStdOut()
        {
            RunScript("console.log(42);");
            Assert.That(StdOut, Does.Contain("42"));
        }

        [Test]
        public void ConsoleLog_WritesToStdOutNotStdErr()
        {
            RunScript("console.log(\"logonly\");");
            Assert.That(_out, Has.Count.EqualTo(1));
            Assert.That(_err, Is.Empty);
        }

        [Test]
        public void ConsoleLog_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript("console.log(42);"));
        }

        // ------------------------------------------------------------------
        // console.error
        // ------------------------------------------------------------------

        [Test]
        public void ConsoleError_WritesToStdErr()
        {
            RunScript("console.error(\"oops\");");
            Assert.That(StdErr, Does.Contain("oops"));
        }

        [Test]
        public void ConsoleError_DoesNotWriteToStdOut()
        {
            RunScript("console.error(\"err-only\");");
            Assert.That(_out, Is.Empty);
        }

        [Test]
        public void ConsoleError_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript("console.error(\"err\");"));
        }

        // ------------------------------------------------------------------
        // console.warn
        // ------------------------------------------------------------------

        [Test]
        public void ConsoleWarn_WritesToStdErrWithWarnPrefix()
        {
            RunScript("console.warn(\"low disk\");");
            Assert.That(StdErr, Does.Contain("[WARN]"));
            Assert.That(StdErr, Does.Contain("low disk"));
        }

        [Test]
        public void ConsoleWarn_DoesNotWriteToStdOut()
        {
            RunScript("console.warn(\"warn-only\");");
            Assert.That(_out, Is.Empty);
        }

        [Test]
        public void ConsoleWarn_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript("console.warn(\"x\");"));
        }

        // ------------------------------------------------------------------
        // console.info
        // ------------------------------------------------------------------

        [Test]
        public void ConsoleInfo_WritesToStdOutWithInfoPrefix()
        {
            RunScript("console.info(\"started\");");
            Assert.That(StdOut, Does.Contain("[INFO]"));
            Assert.That(StdOut, Does.Contain("started"));
        }

        [Test]
        public void ConsoleInfo_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript("console.info(\"x\");"));
        }

        // ------------------------------------------------------------------
        // console.debug
        // ------------------------------------------------------------------

        [Test]
        public void ConsoleDebug_WritesToStdOutWithDebugPrefix()
        {
            RunScript("console.debug(\"breakpoint\");");
            Assert.That(StdOut, Does.Contain("[DEBUG]"));
            Assert.That(StdOut, Does.Contain("breakpoint"));
        }

        [Test]
        public void ConsoleDebug_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript("console.debug(\"x\");"));
        }

        // ------------------------------------------------------------------
        // console.assert
        // ------------------------------------------------------------------

        [Test]
        public void ConsoleAssert_PassingCondition_ProducesNoOutput()
        {
            RunScript("console.assert(true, \"should not appear\");");
            Assert.That(_err, Is.Empty);
        }

        [Test]
        public void ConsoleAssert_FailingCondition_WritesDefaultMessageToStdErr()
        {
            RunScript("console.assert(false);");
            Assert.That(StdErr, Does.Contain("[ASSERT]"));
            Assert.That(StdErr, Does.Contain("Assertion failed"));
        }

        [Test]
        public void ConsoleAssert_FailingConditionWithMessage_WritesCustomMessageToStdErr()
        {
            RunScript("console.assert(1 == 2, \"bad value\");");
            Assert.That(StdErr, Does.Contain("[ASSERT]"));
            Assert.That(StdErr, Does.Contain("bad value"));
        }

        [Test]
        public void ConsoleAssert_ZeroIsFalsy_TriggersAssertion()
        {
            RunScript("console.assert(0, \"zero is falsy\");");
            Assert.That(StdErr, Does.Contain("zero is falsy"));
        }

        [Test]
        public void ConsoleAssert_NonZeroIsTrue_ProducesNoOutput()
        {
            RunScript("console.assert(1, \"should not appear\");");
            Assert.That(_err, Is.Empty);
        }

        // ------------------------------------------------------------------
        // console.time / console.timeEnd
        // ------------------------------------------------------------------

        [Test]
        public void ConsoleTime_AndTimeEnd_DoNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript(
                "console.time(\"t\"); console.timeEnd(\"t\");"));
        }

        [Test]
        public void ConsoleTimeEnd_WritesLabelAndMsToStdOut()
        {
            RunScript("console.time(\"op\"); console.timeEnd(\"op\");");
            Assert.That(StdOut, Does.Contain("op"));
            Assert.That(StdOut, Does.Contain("ms"));
        }

        [Test]
        public void ConsoleTimeEnd_UnknownLabel_ProducesNoOutput()
        {
            // timeEnd for a label that was never started should produce no output.
            RunScript("console.timeEnd(\"neverStarted\");");
            Assert.That(_out, Is.Empty);
        }

        [Test]
        public void ConsoleTime_DefaultLabel_WorksWhenLabelOmitted()
        {
            // "default" label is used when no argument is passed.
            Assert.DoesNotThrow(() => RunScript(
                "console.time(); console.timeEnd();"));
        }

        // ------------------------------------------------------------------
        // console.count / console.countReset
        // ------------------------------------------------------------------

        [Test]
        public void ConsoleCount_IncrementsAndWritesToStdOut()
        {
            RunScript("console.count(\"hits\"); console.count(\"hits\");");
            Assert.That(_out, Has.Count.EqualTo(2));
            Assert.That(_out[0], Does.Contain("hits: 1"));
            Assert.That(_out[1], Does.Contain("hits: 2"));
        }

        [Test]
        public void ConsoleCountReset_ResetsCounterToZero()
        {
            RunScript("console.count(\"x\"); console.countReset(\"x\"); console.count(\"x\");");
            // After reset the counter restarts at 1 on the next call.
            Assert.That(_out[_out.Count - 1], Does.Contain("x: 1"));
        }

        [Test]
        public void ConsoleCount_DefaultLabel_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript(
                "console.count(); console.countReset();"));
        }

        // ------------------------------------------------------------------
        // console.group / console.groupEnd
        // ------------------------------------------------------------------

        [Test]
        public void ConsoleGroup_IncreasesIndentForSubsequentLog()
        {
            RunScript(
                "console.log(\"outer\"); " +
                "console.group(\"g\"); " +
                "console.log(\"inner\"); " +
                "console.groupEnd(); " +
                "console.log(\"back\");");

            // _out lines: "outer", "g", "  inner", "back"
            var outerLine = _out.Find(l => l.Contains("outer"));
            var innerLine = _out.Find(l => l.Contains("inner"));
            var backLine  = _out.Find(l => l.Contains("back"));

            Assert.That(outerLine, Is.Not.Null, "Expected an 'outer' line");
            Assert.That(innerLine, Is.Not.Null, "Expected an 'inner' line");
            Assert.That(backLine,  Is.Not.Null, "Expected a 'back' line");

            Assert.That(innerLine, Does.StartWith("  "), "Inner log must be indented by 2 spaces");
            Assert.That(outerLine, Does.Not.StartWith(" "), "Outer log must have no indent");
            Assert.That(backLine,  Does.Not.StartWith(" "), "Log after groupEnd must have no indent");
        }

        [Test]
        public void ConsoleGroupEnd_BelowZero_DoesNotThrow()
        {
            // groupEnd when indent is already 0 must not underflow or throw.
            Assert.DoesNotThrow(() => RunScript(
                "console.groupEnd(); console.groupEnd();"));
        }

        [Test]
        public void ConsoleGroup_WithoutLabel_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript(
                "console.group(); console.groupEnd();"));
        }

        [Test]
        public void ConsoleGroupEnd_RestoresIndentToZeroAfterGroup()
        {
            RunScript("console.group(\"g\"); console.groupEnd(); console.log(\"flat\");");
            var flatLine = _out.Find(l => l.Contains("flat"));
            Assert.That(flatLine, Is.Not.Null);
            Assert.That(flatLine, Does.Not.StartWith(" "));
        }

        // ------------------------------------------------------------------
        // console.dir
        // ------------------------------------------------------------------

        [Test]
        public void ConsoleDir_OnObject_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript("var o = {a:1,b:2}; console.dir(o);"));
        }

        [Test]
        public void ConsoleDir_OnPrimitive_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript("console.dir(42);"));
        }

        // ------------------------------------------------------------------
        // console.table
        // ------------------------------------------------------------------

        [Test]
        public void ConsoleTable_OnArrayOfObjects_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript(
                "var t = [{name:\"Alice\",age:30},{name:\"Bob\",age:25}]; console.table(t);"));
        }

        [Test]
        public void ConsoleTable_OnArrayOfObjects_WritesHeaderAndRows()
        {
            RunScript("var t = [{name:\"Alice\",age:30},{name:\"Bob\",age:25}]; console.table(t);");
            Assert.That(StdOut, Does.Contain("name"));
            Assert.That(StdOut, Does.Contain("Alice"));
            Assert.That(StdOut, Does.Contain("Bob"));
        }

        [Test]
        public void ConsoleTable_OnEmptyArray_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunScript("var t = []; console.table(t);"));
        }

        [Test]
        public void ConsoleTable_OnEmptyArray_ProducesNoOutput()
        {
            RunScript("var t = []; console.table(t);");
            Assert.That(_out, Is.Empty);
        }

        // ------------------------------------------------------------------
        // console.clear
        // ------------------------------------------------------------------

        [Test]
        public void ConsoleClear_DoesNotThrow()
        {
            // ConsoleClearImpl wraps Console.Clear() in a try/catch, so it should
            // never surface an exception from the engine even in headless environments.
            Assert.DoesNotThrow(() => RunScript("console.clear();"));
        }

        // ------------------------------------------------------------------
        // SetOutput routing
        // ------------------------------------------------------------------

        [Test]
        public void SetOutput_StdoutDelegate_ReceivesConsoleLogOutput()
        {
            // [SetUp] already installs capturing delegates.  Verify that
            // console.log output is delivered to the stdout delegate.
            RunScript("console.log(\"routed\");");

            Assert.That(StdOut, Does.Contain("routed"),
                "console.log output must be delivered to the stdout delegate.");
        }

        [Test]
        public void SetOutput_StderrDelegate_ReceivesConsoleErrorOutput()
        {
            RunScript("console.error(\"err-routed\");");

            Assert.That(StdErr, Does.Contain("err-routed"),
                "console.error output must be delivered to the stderr delegate.");
        }

        [Test]
        public void SetOutput_StderrDelegate_ReceivesConsoleWarnOutput()
        {
            RunScript("console.warn(\"warn-routed\");");

            Assert.That(StdErr, Does.Contain("warn-routed"),
                "console.warn output must be delivered to the stderr delegate.");
        }

        [Test]
        public void SetOutput_AfterResetOutput_DelegateNoLongerReceivesOutput()
        {
            // Install capture, then immediately restore defaults.
            // A subsequent run must NOT go to the old capture list.
            var captured = new List<string>();
            ConsoleFunctionProvider.SetOutput(
                stdout: line => captured.Add(line),
                stderr: line => captured.Add(line));

            RunScript("console.log(\"before-reset\");");

            ConsoleFunctionProvider.ResetOutput();

            // Re-install our test delegates so [TearDown] can call ResetOutput safely.
            ConsoleFunctionProvider.SetOutput(
                stdout: line => _out.Add(line),
                stderr: line => _err.Add(line));

            RunScript("console.log(\"after-reset\");");

            Assert.That(string.Join(",", captured), Does.Contain("before-reset"),
                "Output before ResetOutput must have been delivered to the delegate.");
            Assert.That(string.Join(",", captured), Does.Not.Contain("after-reset"),
                "Output after ResetOutput must NOT reach the old delegate.");
        }

        [Test]
        public void SetOutput_NullStdoutFallsBackToConsoleWriteLine()
        {
            // Passing null for stdout should not throw; the provider falls back to
            // Console.WriteLine.  We verify the call does not throw.
            ConsoleFunctionProvider.SetOutput(stdout: null, stderr: line => _err.Add(line));
            Assert.DoesNotThrow(() => RunScript("console.log(\"fallback\");"));
        }

        [Test]
        public void SetOutput_NullStderrFallsBackToConsoleErrorWriteLine()
        {
            // Passing null for stderr should not throw.
            ConsoleFunctionProvider.SetOutput(stdout: line => _out.Add(line), stderr: null);
            Assert.DoesNotThrow(() => RunScript("console.error(\"fallback-err\");"));
        }
    }
}
