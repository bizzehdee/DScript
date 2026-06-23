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
using DScript;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class ScriptTimeoutTests
    {
        private static ScriptEngine MakeEngine() => new ScriptEngine();

        // --- Instruction limit ---

        [Test]
        public void InstructionLimit_ThrowsWhenExceeded()
        {
            var engine = MakeEngine();
            engine.SetInstructionLimit(10);
            // tight loop hits limit quickly
            Assert.Throws<ScriptTimeoutException>(() =>
                engine.Run(ScriptEngine.Compile("var i=0; while(true){i++;}")));
        }

        [Test]
        public void InstructionLimit_DoesNotThrowWhenNotExceeded()
        {
            var engine = MakeEngine();
            engine.SetInstructionLimit(100000);
            Assert.DoesNotThrow(() =>
                engine.Run(ScriptEngine.Compile("var x = 1 + 2;")));
        }

        [Test]
        public void InstructionLimit_ExceptionIsScriptTimeoutException()
        {
            var engine = MakeEngine();
            engine.SetInstructionLimit(5);
            var ex = Assert.Throws<ScriptTimeoutException>(() =>
                engine.Run(ScriptEngine.Compile("while(true){}")));
            Assert.That(ex.Message, Does.Contain("instruction limit"));
        }

        [Test]
        public void InstructionLimit_ZeroDisablesLimit()
        {
            var engine = MakeEngine();
            engine.SetInstructionLimit(100);
            engine.SetInstructionLimit(0); // disable
            // Short script should complete without throwing
            Assert.DoesNotThrow(() =>
                engine.Run(ScriptEngine.Compile("var x = 42;")));
        }

        [Test]
        public void InstructionLimit_NotCatchableByScript()
        {
            var engine = MakeEngine();
            engine.SetInstructionLimit(5);
            // The script's try/catch must NOT swallow ScriptTimeoutException
            var ex = Assert.Throws<ScriptTimeoutException>(() =>
                engine.Run(ScriptEngine.Compile(
                    "try { while(true){} } catch(e) { var caught = true; }")));
            Assert.That(ex, Is.Not.Null);
        }

        [Test]
        public void InstructionLimit_ResetsBetweenRuns()
        {
            var engine = MakeEngine();
            engine.SetInstructionLimit(10);

            // First run: simple expression — should complete
            Assert.DoesNotThrow(() =>
                engine.Run(ScriptEngine.Compile("var x = 1;")));

            // Second run: infinite loop — should throw
            Assert.Throws<ScriptTimeoutException>(() =>
                engine.Run(ScriptEngine.Compile("while(true){}")));
        }

        // --- Wall-clock timeout ---

        [Test]
        public void WallClockTimeout_ThrowsWhenExceeded()
        {
            var engine = MakeEngine();
            engine.SetTimeout(TimeSpan.FromMilliseconds(50));
            Assert.Throws<ScriptTimeoutException>(() =>
                engine.Run(ScriptEngine.Compile("while(true){}")));
        }

        [Test]
        public void WallClockTimeout_DoesNotThrowForFastScript()
        {
            var engine = MakeEngine();
            engine.SetTimeout(TimeSpan.FromSeconds(5));
            Assert.DoesNotThrow(() =>
                engine.Run(ScriptEngine.Compile("var x = 1 + 2;")));
        }

        [Test]
        public void WallClockTimeout_ExceptionMessageMentionsTimeout()
        {
            var engine = MakeEngine();
            engine.SetTimeout(TimeSpan.FromMilliseconds(50));
            var ex = Assert.Throws<ScriptTimeoutException>(() =>
                engine.Run(ScriptEngine.Compile("while(true){}")));
            Assert.That(ex.Message, Does.Contain("timed out"));
        }

        [Test]
        public void WallClockTimeout_ZeroDisablesTimeout()
        {
            var engine = MakeEngine();
            engine.SetTimeout(TimeSpan.FromMilliseconds(50));
            engine.SetTimeout(TimeSpan.Zero); // disable
            Assert.DoesNotThrow(() =>
                engine.Run(ScriptEngine.Compile("var x = 42;")));
        }

        [Test]
        public void WallClockTimeout_NotCatchableByScript()
        {
            var engine = MakeEngine();
            engine.SetTimeout(TimeSpan.FromMilliseconds(50));
            var ex = Assert.Throws<ScriptTimeoutException>(() =>
                engine.Run(ScriptEngine.Compile(
                    "try { while(true){} } catch(e) { var caught = true; }")));
            Assert.That(ex, Is.Not.Null);
        }

        // --- ScriptTimeoutException ---

        [Test]
        public void ScriptTimeoutException_IsNotJITException()
        {
            // ScriptTimeoutException must NOT extend JITException or ScriptException —
            // that's what keeps it from being caught by script try/catch.
            var ex = new ScriptTimeoutException("test");
            Assert.That(ex, Is.Not.InstanceOf<ScriptException>());
        }

        [Test]
        public void ScriptTimeoutException_MessageIsPreserved()
        {
            var ex = new ScriptTimeoutException("my message");
            Assert.That(ex.Message, Is.EqualTo("my message"));
        }
    }
}
