using NUnit.Framework;
using DScript.Vm;

namespace DScript.Test
{
    /// <summary>
    /// Unit tests for the Phase 11 spike <see cref="Value"/> type: factories, queries,
    /// and round-tripping to/from <see cref="ScriptVar"/> including edge values.
    /// </summary>
    [TestFixture]
    public class ValueTests
    {
        [Test]
        public void IntRoundTrip()
        {
            var v = Value.Int(42);
            Assert.That(v.Kind, Is.EqualTo(ValueKind.Int));
            Assert.That(v.IsInt, Is.True);
            Assert.That(v.IsNumber, Is.True);
            Assert.That(v.AsInt, Is.EqualTo(42));
            Assert.That(v.AsDouble, Is.EqualTo(42.0));
            Assert.That(v.ToScriptVar().Int, Is.EqualTo(42));
        }

        [TestCase(2.5)]
        [TestCase(-0.0)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void DoubleRoundTrip(double d)
        {
            var v = Value.Double(d);
            Assert.That(v.Kind, Is.EqualTo(ValueKind.Double));
            Assert.That(v.IsDouble, Is.True);
            Assert.That(v.IsNumber, Is.True);
            // NaN compares unequal to itself; check bit patterns for exactness.
            Assert.That(System.BitConverter.DoubleToInt64Bits(v.AsDouble),
                        Is.EqualTo(System.BitConverter.DoubleToInt64Bits(d)));
            Assert.That(System.BitConverter.DoubleToInt64Bits(v.ToScriptVar().Float),
                        Is.EqualTo(System.BitConverter.DoubleToInt64Bits(d)));
        }

        [Test]
        public void BoolIsInt()
        {
            Assert.That(Value.Bool(true).Kind, Is.EqualTo(ValueKind.Int));
            Assert.That(Value.Bool(true).AsInt, Is.EqualTo(1));
            Assert.That(Value.Bool(false).AsInt, Is.EqualTo(0));
        }

        [Test]
        public void NullAndUndefined()
        {
            Assert.That(Value.Null.Kind, Is.EqualTo(ValueKind.Null));
            Assert.That(Value.Null.IsNull, Is.True);
            Assert.That(Value.Null.IsNumber, Is.False);
            Assert.That(Value.Null.ToScriptVar().IsNull, Is.True);

            Assert.That(Value.Undefined.Kind, Is.EqualTo(ValueKind.Undefined));
            Assert.That(Value.Undefined.IsUndefined, Is.True);
            Assert.That(Value.Undefined.ToScriptVar().IsUndefined, Is.True);
        }

        [Test]
        public void RefRoundTripPreservesInstance()
        {
            var sv = ScriptVar.FromString("hello");
            var v = Value.Ref(sv);
            Assert.That(v.Kind, Is.EqualTo(ValueKind.Ref));
            Assert.That(v.IsRef, Is.True);
            Assert.That(v.AsRef, Is.SameAs(sv));
            Assert.That(v.ToScriptVar(), Is.SameAs(sv), "a Ref returns its wrapped instance, not a copy");
        }

        [Test]
        public void FromScriptVarClassifiesPrimitives()
        {
            Assert.That(Value.From(ScriptVar.FromInt(7)).Kind, Is.EqualTo(ValueKind.Int));
            Assert.That(Value.From(ScriptVar.FromInt(7)).AsInt, Is.EqualTo(7));
            Assert.That(Value.From(ScriptVar.FromDouble(1.5)).Kind, Is.EqualTo(ValueKind.Double));
            Assert.That(Value.From(ScriptVar.CreateNull()).Kind, Is.EqualTo(ValueKind.Null));
            Assert.That(Value.From(ScriptVar.CreateUndefined()).Kind, Is.EqualTo(ValueKind.Undefined));
            Assert.That(Value.From(null).Kind, Is.EqualTo(ValueKind.Undefined));
        }

        [Test]
        public void FromScriptVarClassifiesNonPrimitivesAsRef()
        {
            var s = ScriptVar.FromString("x");
            var o = ScriptVar.CreateObject();
            Assert.That(Value.From(s).Kind, Is.EqualTo(ValueKind.Ref));
            Assert.That(Value.From(s).AsRef, Is.SameAs(s));
            Assert.That(Value.From(o).Kind, Is.EqualTo(ValueKind.Ref));
        }

        [Test]
        public void FromThenToScriptVarPreservesValue()
        {
            Assert.That(Value.From(ScriptVar.FromInt(123)).ToScriptVar().Int, Is.EqualTo(123));
            Assert.That(Value.From(ScriptVar.FromString("abc")).ToScriptVar().String, Is.EqualTo("abc"));
        }
    }
}
