using DScript;
using DScript.Vm;
using NUnit.Framework;

namespace DScript.Test
{
    // Phase 7: compile to bytecode, save to bytes, load, and run later.
    public class BytecodePersistenceTests
    {
        // Compile on a throwaway engine, save+load the bytecode, run on runEngine.
        private static ScriptVar RoundTrip(string source, ScriptEngine runEngine)
        {
            var compiled = new ScriptEngine().Compile(source);
            var bytes = BytecodeSerializer.Save(compiled);
            var loaded = BytecodeSerializer.Load(bytes);
            runEngine.Run(loaded);
            return runEngine.Root;
        }

        [Test]
        public void RoundTripsArithmetic()
        {
            var root = RoundTrip("var x = (2 + 3) * 4;", new ScriptEngine());
            Assert.That(root.GetParameter("x").Int, Is.EqualTo(20));
        }

        [Test]
        public void RoundTripsFunctionAndCall()
        {
            var root = RoundTrip("function add(a, b) { return a + b; } var r = add(40, 2);", new ScriptEngine());
            Assert.That(root.GetParameter("r").Int, Is.EqualTo(42));
        }

        [Test]
        public void RoundTripsClosures()
        {
            var src =
                "function make() { var n = 0; return function() { n = n + 1; return n; }; }" +
                "var c = make(); var a = c(); var b = c();";
            var root = RoundTrip(src, new ScriptEngine());
            Assert.That(root.GetParameter("a").Int, Is.EqualTo(1));
            Assert.That(root.GetParameter("b").Int, Is.EqualTo(2));
        }

        [Test]
        public void LoadedProgramResolvesNativesByName()
        {
            // the bytecode only references "triple" by name; the running engine
            // supplies the native, so no relinking is required.
            var runEngine = new ScriptEngine();
            runEngine.AddNative("function triple(x)", (v, _) => { v.ReturnVar.Int = v.GetParameter("x").Int * 3; }, null);

            var root = RoundTrip("var r = triple(7);", runEngine);
            Assert.That(root.GetParameter("r").Int, Is.EqualTo(21));
        }

        [Test]
        public void SaveLoadPreservesStringsAndProgramStructure()
        {
            var root = RoundTrip("var s = \"hello\"; var t = s + \" world\"; var n = t.length;", new ScriptEngine());
            Assert.That(root.GetParameter("t").String, Is.EqualTo("hello world"));
            Assert.That(root.GetParameter("n").Int, Is.EqualTo(11));
        }

        [Test]
        public void LoadRejectsNonBytecode()
        {
            Assert.That(() => BytecodeSerializer.Load(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }), Throws.TypeOf<ScriptException>());
        }
    }
}
