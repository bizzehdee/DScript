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

using System.IO;
using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class ReadlineTests
    {
        /// <summary>
        /// Build an engine with a __testReader__ global that holds the given StringReader
        /// so scripts can pass it as input to readline.createInterface.
        /// </summary>
        private static ScriptEngine MakeEngine(string inputLines = "")
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);

            // Inject a ScriptVar carrying the StringReader as native data.
            var readerVar = ScriptVar.CreateObject();
            readerVar.SetData(new StringReader(inputLines));
            engine.Root.AddChild("__testReader__", readerVar);

            // Inject a ScriptVar carrying a StringWriter for capturing output.
            var writerVar = ScriptVar.CreateObject();
            var sw = new StringWriter();
            writerVar.SetData(sw);
            engine.Root.AddChild("__testWriter__", writerVar);

            return engine;
        }

        private static StringWriter GetWriter(ScriptEngine engine)
        {
            var writerVar = engine.Root.FindChild("__testWriter__")?.Var;
            return writerVar?.GetData() as StringWriter;
        }

        // --- createInterface ---

        [Test]
        public void CreateInterface_ReturnsRlObject()
        {
            var engine = MakeEngine();
            engine.Execute("var rl = readline.createInterface({});");
            Assert.That(engine.Root.FindChild("rl"), Is.Not.Null);
        }

        [Test]
        public void CreateInterface_RlObjectHasQuestionMethod()
        {
            var engine = MakeEngine();
            engine.Execute("var rl = readline.createInterface({});");
            var rl = engine.Root.FindChild("rl")?.Var;
            Assert.That(rl?.FindChild("question"), Is.Not.Null);
        }

        [Test]
        public void CreateInterface_RlObjectHasCloseMethod()
        {
            var engine = MakeEngine();
            engine.Execute("var rl = readline.createInterface({});");
            var rl = engine.Root.FindChild("rl")?.Var;
            Assert.That(rl?.FindChild("close"), Is.Not.Null);
        }

        [Test]
        public void CreateInterface_RlObjectHasOnMethod()
        {
            var engine = MakeEngine();
            engine.Execute("var rl = readline.createInterface({});");
            var rl = engine.Root.FindChild("rl")?.Var;
            Assert.That(rl?.FindChild("on"), Is.Not.Null);
        }

        // --- question ---

        [Test]
        public void Question_CallsCallbackWithAnswer()
        {
            var engine = MakeEngine("hello world");
            engine.Execute(@"
                var rl = readline.createInterface({ input: __testReader__, output: __testWriter__ });
                var answer = null;
                rl.question('prompt> ', function(ans) { answer = ans; });
            ");
            var answer = engine.Root.FindChild("answer")?.Var?.String;
            Assert.That(answer, Is.EqualTo("hello world"));
        }

        [Test]
        public void Question_WritesPromptToOutput()
        {
            var engine = MakeEngine("answer");
            engine.Execute(@"
                var rl = readline.createInterface({ input: __testReader__, output: __testWriter__ });
                rl.question('What is your name? ', function(ans) {});
            ");
            var writer = GetWriter(engine);
            Assert.That(writer.ToString(), Does.Contain("What is your name?"));
        }

        [Test]
        public void Question_DoesNothingAfterClose()
        {
            var engine = MakeEngine("some input");
            engine.Execute(@"
                var rl = readline.createInterface({ input: __testReader__, output: __testWriter__ });
                var called = false;
                rl.close();
                rl.question('prompt> ', function(ans) { called = true; });
            ");
            Assert.That(engine.Root.FindChild("called")?.Var?.Bool, Is.False);
        }

        // --- close ---

        [Test]
        public void Close_FiresCloseHandlers()
        {
            var engine = MakeEngine();
            engine.Execute(@"
                var rl = readline.createInterface({});
                var closed = false;
                rl.on('close', function() { closed = true; });
                rl.close();
            ");
            Assert.That(engine.Root.FindChild("closed")?.Var?.Bool, Is.True);
        }

        [Test]
        public void Close_FiresMultipleCloseHandlers()
        {
            var engine = MakeEngine();
            engine.Execute(@"
                var rl = readline.createInterface({});
                var count = 0;
                rl.on('close', function() { count++; });
                rl.on('close', function() { count++; });
                rl.close();
            ");
            Assert.That(engine.Root.FindChild("count")?.Var?.Int, Is.EqualTo(2));
        }

        // --- on ---

        [Test]
        public void On_LineHandler_RegistrationDoesNotThrow()
        {
            var engine = MakeEngine();
            Assert.DoesNotThrow(() =>
                engine.Execute("var rl = readline.createInterface({}); rl.on('line', function(l) {});"));
        }

        [Test]
        public void On_UnknownEvent_IgnoredSilently()
        {
            var engine = MakeEngine();
            Assert.DoesNotThrow(() =>
                engine.Execute("var rl = readline.createInterface({}); rl.on('unknownEvent', function() {});"));
        }

        // --- edge cases ---

        [Test]
        public void Question_EmptyInput_CallsCallbackWithEmptyString()
        {
            var engine = MakeEngine("");
            engine.Execute(@"
                var rl = readline.createInterface({ input: __testReader__, output: __testWriter__ });
                var answer = 'unset';
                rl.question('p> ', function(ans) { answer = ans; });
            ");
            // StringReader("") → ReadLine() returns null which we coerce to ""
            var answer = engine.Root.FindChild("answer")?.Var?.String;
            Assert.That(answer, Is.EqualTo(""));
        }

        [Test]
        public void MultipleQuestions_ReadSuccessiveLinesFromInput()
        {
            var engine = MakeEngine("first\nsecond");
            engine.Execute(@"
                var rl = readline.createInterface({ input: __testReader__, output: __testWriter__ });
                var a1 = null, a2 = null;
                rl.question('1> ', function(ans) { a1 = ans; });
                rl.question('2> ', function(ans) { a2 = ans; });
            ");
            Assert.That(engine.Root.FindChild("a1")?.Var?.String, Is.EqualTo("first"));
            Assert.That(engine.Root.FindChild("a2")?.Var?.String, Is.EqualTo("second"));
        }
    }
}
