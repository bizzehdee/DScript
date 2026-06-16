using NUnit.Framework;

namespace DScript.Test
{
    // Behaviour-level tests for ScriptVar covering the bug fixes that are
    // exercisable directly through the public API (without running script).
    public class ScriptVarBehaviourTests
    {
        [Test]
        public void RemoveAllChildren_ReleasesChildReferences()
        {
            var parent = new ScriptVar(null, ScriptVar.Flags.Object);

            // Hold an external reference so the child survives for inspection.
            var child = new ScriptVar(5).Ref();
            parent.AddChild("x", child);

            Assert.That(child.GetRefs(), Is.EqualTo(2), "AddChild should take a reference");

            parent.RemoveAllChildren();

            Assert.That(child.GetRefs(), Is.EqualTo(1), "RemoveAllChildren should release the child's reference");
        }

        [Test]
        public void FindChildOrCreateByPath_CreatesFullNestedPath()
        {
            var root = new ScriptVar(null, ScriptVar.Flags.Object);

            var leaf = root.FindChildOrCreateByPath("a.b.c");
            leaf.Var.Int = 7;

            var navigated = root.FindChild("a").Var.FindChild("b").Var.FindChild("c");

            Assert.That(navigated, Is.Not.Null, "the full a.b.c path should be created");
            Assert.That(navigated.Var, Is.SameAs(leaf.Var));
            Assert.That(navigated.Var.Int, Is.EqualTo(7));
        }

        [Test]
        public void GetArrayLength_ReflectsElementsCopiedByCopyValue()
        {
            var source = new ScriptVar(null, ScriptVar.Flags.Array);
            source.SetArrayIndex(0, new ScriptVar(10));
            source.SetArrayIndex(1, new ScriptVar(20));
            source.SetArrayIndex(2, new ScriptVar(30));

            var destination = new ScriptVar(null, ScriptVar.Flags.Array);
            destination.CopyValue(source);

            Assert.That(destination.GetArrayLength(), Is.EqualTo(3));
        }

        [Test]
        public void DoubleValue_FormatsAndParsesWithInvariantCulture()
        {
            Assert.That(new ScriptVar(1.5).String, Is.EqualTo("1.5"));
            Assert.That(new ScriptVar("2.5", ScriptVar.Flags.Double).Float, Is.EqualTo(2.5));
        }

        [Test]
        public void MathsOp_IntegerDivideByZero_YieldsInfinity()
        {
            var positive = new ScriptVar(5).MathsOp(new ScriptVar(0), (ScriptLex.LexTypes)'/');
            var negative = new ScriptVar(-5).MathsOp(new ScriptVar(0), (ScriptLex.LexTypes)'/');

            Assert.That(double.IsPositiveInfinity(positive.Float), Is.True);
            Assert.That(double.IsNegativeInfinity(negative.Float), Is.True);
        }

        [Test]
        public void MathsOp_IntegerZeroDivideZeroAndModuloByZero_YieldNaN()
        {
            var zeroOverZero = new ScriptVar(0).MathsOp(new ScriptVar(0), (ScriptLex.LexTypes)'/');
            var moduloByZero = new ScriptVar(5).MathsOp(new ScriptVar(0), (ScriptLex.LexTypes)'%');

            Assert.That(double.IsNaN(zeroOverZero.Float), Is.True);
            Assert.That(double.IsNaN(moduloByZero.Float), Is.True);
        }
    }
}
