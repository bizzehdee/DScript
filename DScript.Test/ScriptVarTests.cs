using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;

namespace DScript.Test
{
    public class ScriptVarTests
    {
        [Test]
        public void EmptyScriptVarIsUndefined()
        {
            var v = new ScriptVar();

            Assert.That(v.IsUndefined, Is.True);
        }

        [Test]
        public void BoolScriptVarIsIntAndTrue()
        {
            var v = new ScriptVar(true);

            Assert.That(v.IsInt, Is.True);
            Assert.That(v.Bool, Is.True);
        }
    }
}
