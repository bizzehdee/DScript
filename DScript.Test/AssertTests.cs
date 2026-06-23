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
    public class AssertTests
    {
        private static void Exec(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            // Use Run() directly so ScriptException/JITException propagates to the caller.
            // engine.Execute() silently swallows those exceptions to stderr.
            engine.Run(ScriptEngine.Compile(code));
        }

        [Test]
        public void Assert_Ok_TruthyValue_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Exec("assert.ok(1);"));
        }

        [Test]
        public void Assert_Ok_FalsyValue_Throws()
        {
            Assert.Throws<ScriptException>(() => Exec("assert.ok(0);"));
        }

        [Test]
        public void Assert_Ok_TrueValue_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Exec("assert.ok(true);"));
        }

        [Test]
        public void Assert_Ok_ZeroString_FalsyThrows()
        {
            Assert.Throws<ScriptException>(() => Exec("assert.ok('');"));
        }

        [Test]
        public void Assert_Ok_WithCustomMessage()
        {
            var ex = Assert.Throws<ScriptException>(() => Exec("assert.ok(false, 'custom msg');"));
            Assert.That(ex.Message, Does.Contain("custom msg"));
        }

        [Test]
        public void Assert_Equal_SameValues_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Exec("assert.equal(1, 1);"));
        }

        [Test]
        public void Assert_Equal_DifferentValues_Throws()
        {
            Assert.Throws<ScriptException>(() => Exec("assert.equal(1, 2);"));
        }

        [Test]
        public void Assert_StrictEqual_SameValues_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Exec("assert.strictEqual('abc', 'abc');"));
        }

        [Test]
        public void Assert_StrictEqual_DifferentValues_Throws()
        {
            Assert.Throws<ScriptException>(() => Exec("assert.strictEqual(1, 2);"));
        }

        [Test]
        public void Assert_NotEqual_DifferentValues_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Exec("assert.notEqual(1, 2);"));
        }

        [Test]
        public void Assert_NotEqual_SameValues_Throws()
        {
            Assert.Throws<ScriptException>(() => Exec("assert.notEqual(1, 1);"));
        }

        [Test]
        public void Assert_NotStrictEqual_DifferentValues_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Exec("assert.notStrictEqual(1, 2);"));
        }

        [Test]
        public void Assert_DeepEqual_IdenticalObjects_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Exec("assert.deepEqual({a:1}, {a:1});"));
        }

        [Test]
        public void Assert_DeepEqual_DifferentObjects_Throws()
        {
            Assert.Throws<ScriptException>(() => Exec("assert.deepEqual({a:1}, {a:2});"));
        }

        [Test]
        public void Assert_DeepEqual_IdenticalArrays_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Exec("assert.deepEqual([1,2,3], [1,2,3]);"));
        }

        [Test]
        public void Assert_DeepEqual_DifferentArrays_Throws()
        {
            Assert.Throws<ScriptException>(() => Exec("assert.deepEqual([1,2], [1,3]);"));
        }

        [Test]
        public void Assert_Throws_FunctionThatThrows_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Exec("assert.throws(function() { throw 'boom'; });"));
        }

        [Test]
        public void Assert_Throws_FunctionThatDoesNotThrow_Throws()
        {
            Assert.Throws<ScriptException>(() => Exec("assert.throws(function() { var x = 1; });"));
        }

        [Test]
        public void Assert_DoesNotThrow_SafeFunction_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Exec("assert.doesNotThrow(function() { var x = 1; });"));
        }

        [Test]
        public void Assert_DoesNotThrow_ThrowingFunction_Throws()
        {
            Assert.Throws<ScriptException>(() => Exec("assert.doesNotThrow(function() { throw 'boom'; });"));
        }

        [Test]
        public void Assert_Fail_AlwaysThrows()
        {
            Assert.Throws<ScriptException>(() => Exec("assert.fail('expected failure');"));
        }

        [Test]
        public void Assert_Fail_DefaultMessage()
        {
            var ex = Assert.Throws<ScriptException>(() => Exec("assert.fail();"));
            Assert.That(ex.Message, Does.Contain("Assertion failed"));
        }
    }
}
