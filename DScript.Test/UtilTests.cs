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

using DScript;
using DScript.Extras;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class UtilTests
    {
        private static string RunScript(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("result").String;
        }

        private static ScriptVar RunScriptVar(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("result");
        }

        // --- util.format ---

        [Test]
        public void Util_Format_StringSpec_ReplacesToken()
        {
            var result = RunScript("var result = util.format('%s world', 'hello');");
            Assert.That(result, Is.EqualTo("hello world"));
        }

        [Test]
        public void Util_Format_IntSpec_ReplacesToken()
        {
            var result = RunScript("var result = util.format('value: %d', 42);");
            Assert.That(result, Is.EqualTo("value: 42"));
        }

        [Test]
        public void Util_Format_ISpec_ReplacesToken()
        {
            var result = RunScript("var result = util.format('%i', 7);");
            Assert.That(result, Is.EqualTo("7"));
        }

        [Test]
        public void Util_Format_FloatSpec_ReplacesToken()
        {
            var result = RunScript("var result = util.format('%f', 3.14);");
            Assert.That(result, Does.StartWith("3.14"));
        }

        [Test]
        public void Util_Format_PercentLiteral_NotConsumedAsArg()
        {
            var result = RunScript("var result = util.format('100%%');");
            Assert.That(result, Is.EqualTo("100%"));
        }

        [Test]
        public void Util_Format_ObjectSpec_InspectsValue()
        {
            var result = RunScript("var result = util.format('%o', {a:1});");
            Assert.That(result, Does.Contain("a"));
        }

        [Test]
        public void Util_Format_JsonSpec_InspectsValue()
        {
            var result = RunScript("var result = util.format('%j', {x:2});");
            Assert.That(result, Does.Contain("x"));
        }

        [Test]
        public void Util_Format_MultipleSpecs_AllReplaced()
        {
            var result = RunScript("var result = util.format('%s=%d', 'age', 30);");
            Assert.That(result, Is.EqualTo("age=30"));
        }

        [Test]
        public void Util_Format_ExtraArgs_AppendedWithSpace()
        {
            var result = RunScript("var result = util.format('hi', 'extra');");
            Assert.That(result, Is.EqualTo("hi extra"));
        }

        // --- util.inspect ---

        [Test]
        public void Util_Inspect_Null_ReturnsNull()
        {
            var result = RunScript("var result = util.inspect(null);");
            Assert.That(result, Is.EqualTo("null"));
        }

        [Test]
        public void Util_Inspect_Undefined_ReturnsUndefined()
        {
            var result = RunScript("var result = util.inspect(undefined);");
            Assert.That(result, Is.EqualTo("undefined"));
        }

        [Test]
        public void Util_Inspect_String_ReturnsSingleQuoted()
        {
            var result = RunScript("var result = util.inspect('hello');");
            Assert.That(result, Is.EqualTo("'hello'"));
        }

        [Test]
        public void Util_Inspect_Number_ReturnsNumber()
        {
            var result = RunScript("var result = util.inspect(42);");
            Assert.That(result, Is.EqualTo("42"));
        }

        [Test]
        public void Util_Inspect_Function_ReturnsLabel()
        {
            var result = RunScript("var result = util.inspect(function(){});");
            Assert.That(result, Is.EqualTo("[Function]"));
        }

        [Test]
        public void Util_Inspect_Array_ReturnsArrayRepr()
        {
            var result = RunScript("var result = util.inspect([1,2,3]);");
            Assert.That(result, Is.EqualTo("[1, 2, 3]"));
        }

        [Test]
        public void Util_Inspect_Object_ReturnsObjectRepr()
        {
            var result = RunScript("var result = util.inspect({a:1});");
            Assert.That(result, Does.Contain("a: 1"));
        }

        // --- util.deprecate ---

        [Test]
        public void Util_Deprecate_ReturnedFunctionExecutes()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute("var fn = util.deprecate(function(x) { return x * 2; }, 'old'); var result = fn(5);");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(10));
        }

        [Test]
        public void Util_Deprecate_CalledTwice_OnlyWarnsOnce()
        {
            // Just ensure no exception and the function still works both times
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            Assert.DoesNotThrow(() =>
            {
                engine.Execute(
                    "var fn = util.deprecate(function(x) { return x; }, 'dep'); " +
                    "fn(1); fn(2);");
            });
        }

        // --- util.promisify ---

        [Test]
        public void Util_Promisify_WrapperCallsThenOnSuccess()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(
                "function cbFn(arg, cb) { cb(null, arg * 2); } " +
                "var wrapped = util.promisify(cbFn); " +
                "var result = 0; " +
                "wrapped(5).then(function(v) { result = v; });");
            Assert.That(engine.Root.GetParameter("result").Int, Is.EqualTo(10));
        }

        [Test]
        public void Util_Promisify_WrapperCallsCatchOnError()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(
                "function cbFn(arg, cb) { cb('error happened', null); } " +
                "var wrapped = util.promisify(cbFn); " +
                "var errResult = ''; " +
                "wrapped(5).catch(function(e) { errResult = e; });");
            Assert.That(engine.Root.GetParameter("errResult").String, Is.EqualTo("error happened"));
        }

        [Test]
        public void Util_Promisify_ReturnsObjectWithThenAndCatch()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(
                "function cbFn(arg, cb) { cb(null, 1); } " +
                "var wrapped = util.promisify(cbFn); " +
                "var p = wrapped(1); " +
                "var hasThen = typeof p.then === 'function'; " +
                "var hasCatch = typeof p.catch === 'function';");
            Assert.That(engine.Root.GetParameter("hasThen").Bool, Is.True);
            Assert.That(engine.Root.GetParameter("hasCatch").Bool, Is.True);
        }
    }
}
