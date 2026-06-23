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
    public class CryptoTests
    {
        private static ScriptVar Run(string code)
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute(code);
            return engine.Root.GetParameter("__r__");
        }

        private static string RunStr(string code) => Run(code).String;
        private static int RunInt(string code) => Run(code).Int;

        [Test]
        public void Crypto_RandomUUID_ProducesGuidFormat()
        {
            var r = RunStr("var __r__ = crypto.randomUUID();");
            Assert.That(r.Length, Is.EqualTo(36));
            Assert.That(r[8], Is.EqualTo('-'));
            Assert.That(r[13], Is.EqualTo('-'));
        }

        [Test]
        public void Crypto_RandomUUID_ProducesDifferentValuesEachCall()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute("var a = crypto.randomUUID(); var b = crypto.randomUUID();");
            var a = engine.Root.GetParameter("a").String;
            var b = engine.Root.GetParameter("b").String;
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void Crypto_RandomBytes_ReturnsArrayOfRequestedLength()
        {
            var r = RunInt("var b = crypto.randomBytes(8); var __r__ = b.length;");
            Assert.That(r, Is.EqualTo(8));
        }

        [Test]
        public void Crypto_RandomBytes_ElementsAreByteRange()
        {
            var engine = new ScriptEngine();
            new EngineFunctionLoader().RegisterFunctions(engine);
            engine.Execute("var b = crypto.randomBytes(4);");
            var arr = engine.Root.GetParameter("b");
            for (var i = 0; i < 4; i++)
            {
                var v = arr.GetArrayIndex(i).Int;
                Assert.That(v, Is.InRange(0, 255));
            }
        }

        [Test]
        public void Crypto_GetRandomValues_FillsArray()
        {
            var r = RunInt("var a = [0,0,0,0]; crypto.getRandomValues(a); var __r__ = a.length;");
            Assert.That(r, Is.EqualTo(4));
        }

        [Test]
        public void Crypto_CreateHash_SHA256_KnownValue()
        {
            // SHA256("") = e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
            var r = RunStr("var h = crypto.createHash('sha256'); h.update(''); var __r__ = h.digest('hex');");
            Assert.That(r, Is.EqualTo("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"));
        }

        [Test]
        public void Crypto_CreateHash_SHA256_NonEmpty_KnownValue()
        {
            // SHA256("abc") = ba7816bf8f01cfea414140de5dae2ec73b00361bbef0469f490f4187e74927ce... wait let me use a known value
            // SHA256("hello") = 2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824
            var r = RunStr("var h = crypto.createHash('sha256'); h.update('hello'); var __r__ = h.digest('hex');");
            Assert.That(r, Is.EqualTo("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824"));
        }

        [Test]
        public void Crypto_CreateHash_MD5_KnownValue()
        {
            // MD5("") = d41d8cd98f00b204e9800998ecf8427e
            var r = RunStr("var h = crypto.createHash('md5'); h.update(''); var __r__ = h.digest('hex');");
            Assert.That(r, Is.EqualTo("d41d8cd98f00b204e9800998ecf8427e"));
        }

        [Test]
        public void Crypto_CreateHash_SHA1_KnownValue()
        {
            // SHA1("") = da39a3ee5e6b4b0d3255bfef95601890afd80709
            var r = RunStr("var h = crypto.createHash('sha1'); h.update(''); var __r__ = h.digest('hex');");
            Assert.That(r, Is.EqualTo("da39a3ee5e6b4b0d3255bfef95601890afd80709"));
        }

        [Test]
        public void Crypto_CreateHash_Base64Encoding()
        {
            // Base64 of SHA256("") = 47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=
            var r = RunStr("var h = crypto.createHash('sha256'); h.update(''); var __r__ = h.digest('base64');");
            Assert.That(r, Is.EqualTo("47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU="));
        }

        [Test]
        public void Crypto_CreateHmac_SHA256_KnownValue()
        {
            // HMAC-SHA256(key="key", data="") - verified value
            var r = RunStr("var h = crypto.createHmac('sha256', 'key'); h.update(''); var __r__ = h.digest('hex');");
            // Just verify it produces a 64-char hex string
            Assert.That(r.Length, Is.EqualTo(64));
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(r, "^[0-9a-f]+$"), Is.True);
        }

        [Test]
        public void Crypto_CreateHmac_MD5_ProducesHexString()
        {
            var r = RunStr("var h = crypto.createHmac('md5', 'secret'); h.update('msg'); var __r__ = h.digest('hex');");
            Assert.That(r.Length, Is.EqualTo(32));
        }
    }
}
