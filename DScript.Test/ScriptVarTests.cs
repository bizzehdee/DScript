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

            Assert.IsTrue(v.IsUndefined);
        }

        [Test]
        public void BoolScriptVarIsIntAndTrue()
        {
            var v = new ScriptVar(true);

            Assert.IsTrue(v.IsInt);
            Assert.IsTrue(v.Bool);
        }
    }
}
