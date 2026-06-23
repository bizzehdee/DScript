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
using DScript.Compiler;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    [TestFixture]
    public class IntlTests
    {
        private static ScriptVar RunScript(string code)
        {
            var engine = new ScriptEngine();
            var compiler = new DScriptCompiler();
            var chunk = compiler.CompileProgram(code);
            new VirtualMachine(engine).Run(chunk, new Vm.Environment(engine.Root, null));
            return engine.Root.GetParameter("r");
        }

        // ── Intl.getCanonicalLocales ─────────────────────────────────────────

        [Test]
        public void GetCanonicalLocales_SingleString()
        {
            var r = RunScript(@"var r = Intl.getCanonicalLocales('en-US').length;");
            Assert.That(r.Int, Is.EqualTo(1));
        }

        [Test]
        public void GetCanonicalLocales_EmptyUndefined()
        {
            var r = RunScript(@"var r = Intl.getCanonicalLocales(undefined).length;");
            Assert.That(r.Int, Is.EqualTo(0));
        }

        // ── Intl.Collator ────────────────────────────────────────────────────

        [Test]
        public void Collator_CompareEqual()
        {
            var r = RunScript(@"
var c = new Intl.Collator('en-US');
var r = c.compare('a', 'a');
");
            Assert.That(r.Int, Is.EqualTo(0));
        }

        [Test]
        public void Collator_CompareLessThan()
        {
            var r = RunScript(@"
var c = new Intl.Collator('en-US');
var r = c.compare('a', 'b') < 0;
");
            Assert.That(r.Bool, Is.True);
        }

        [Test]
        public void Collator_CompareGreaterThan()
        {
            var r = RunScript(@"
var c = new Intl.Collator('en-US');
var r = c.compare('b', 'a') > 0;
");
            Assert.That(r.Bool, Is.True);
        }

        [Test]
        public void Collator_ResolvedOptions_HasLocale()
        {
            var r = RunScript(@"
var c = new Intl.Collator('en-US');
var opts = c.resolvedOptions();
var r = opts.locale;
");
            Assert.That(r.String, Is.EqualTo("en-US"));
        }

        // ── Intl.NumberFormat ────────────────────────────────────────────────

        [Test]
        public void NumberFormat_FormatReturnsString()
        {
            var r = RunScript(@"
var nf = new Intl.NumberFormat('en-US');
var r = typeof nf.format(1234);
");
            Assert.That(r.String, Is.EqualTo("string"));
        }

        [Test]
        public void NumberFormat_ResolvedOptions_HasLocale()
        {
            var r = RunScript(@"
var nf = new Intl.NumberFormat('en-US');
var opts = nf.resolvedOptions();
var r = opts.locale;
");
            Assert.That(r.String, Is.EqualTo("en-US"));
        }

        [Test]
        public void NumberFormat_FormatToParts_ReturnsArray()
        {
            var r = RunScript(@"
var nf = new Intl.NumberFormat('en-US');
var parts = nf.formatToParts(42);
var r = parts.length;
");
            Assert.That(r.Int, Is.GreaterThan(0));
        }

        // ── Intl.DateTimeFormat ──────────────────────────────────────────────

        [Test]
        public void DateTimeFormat_FormatReturnsString()
        {
            var r = RunScript(@"
var dtf = new Intl.DateTimeFormat('en-US');
var r = typeof dtf.format(0);
");
            Assert.That(r.String, Is.EqualTo("string"));
        }

        [Test]
        public void DateTimeFormat_ResolvedOptions_HasLocale()
        {
            var r = RunScript(@"
var dtf = new Intl.DateTimeFormat('en-US');
var opts = dtf.resolvedOptions();
var r = opts.locale;
");
            Assert.That(r.String, Is.EqualTo("en-US"));
        }

        // ── Intl.DisplayNames ────────────────────────────────────────────────

        [Test]
        public void DisplayNames_Of_Language()
        {
            var r = RunScript(@"
var dn = new Intl.DisplayNames('en', { type: 'language' });
var r = typeof dn.of('fr');
");
            Assert.That(r.String, Is.EqualTo("string"));
        }

        [Test]
        public void DisplayNames_Of_Region()
        {
            var r = RunScript(@"
var dn = new Intl.DisplayNames('en', { type: 'region' });
var name = dn.of('US');
var r = name.length > 0;
");
            Assert.That(r.Bool, Is.True);
        }

        // ── Intl.PluralRules ─────────────────────────────────────────────────

        [Test]
        public void PluralRules_SelectOne()
        {
            var r = RunScript(@"
var pr = new Intl.PluralRules('en-US');
var r = pr.select(1);
");
            Assert.That(r.String, Is.EqualTo("one"));
        }

        [Test]
        public void PluralRules_SelectOther()
        {
            var r = RunScript(@"
var pr = new Intl.PluralRules('en-US');
var r = pr.select(5);
");
            Assert.That(r.String, Is.EqualTo("other"));
        }

        [Test]
        public void PluralRules_ResolvedOptions_HasLocale()
        {
            var r = RunScript(@"
var pr = new Intl.PluralRules('en-US');
var opts = pr.resolvedOptions();
var r = opts.locale;
");
            Assert.That(r.String, Is.EqualTo("en-US"));
        }
    }
}
