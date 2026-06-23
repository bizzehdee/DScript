using NUnit.Framework;
using System;

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

        // ── MathsOp: integer branch via null operand (skips VM fast path) ────────

        [Test]
        public void MathsOp_NullAndInt_Bitwise_And()
        {
            // null is numeric (IsNumeric=true, IsDouble=false), int is numeric.
            // VM can't fast-path null+int, so MathsOp integer branch is exercised.
            var result = new ScriptVar(null, ScriptVar.Flags.Null).MathsOp(new ScriptVar(7), (ScriptLex.LexTypes)'&');
            Assert.That(result.Int, Is.EqualTo(0)); // 0 & 7 = 0
        }

        [Test]
        public void MathsOp_NullAndInt_Bitwise_Or()
        {
            var result = new ScriptVar(null, ScriptVar.Flags.Null).MathsOp(new ScriptVar(7), (ScriptLex.LexTypes)'|');
            Assert.That(result.Int, Is.EqualTo(7)); // 0 | 7 = 7
        }

        [Test]
        public void MathsOp_NullAndInt_Bitwise_Xor()
        {
            var result = new ScriptVar(null, ScriptVar.Flags.Null).MathsOp(new ScriptVar(5), (ScriptLex.LexTypes)'^');
            Assert.That(result.Int, Is.EqualTo(5)); // 0 ^ 5 = 5
        }

        [Test]
        public void MathsOp_NullAndInt_Comparison_NEqual()
        {
            var result = new ScriptVar(null, ScriptVar.Flags.Null).MathsOp(new ScriptVar(5), ScriptLex.LexTypes.NEqual);
            Assert.That(result.Bool, Is.True); // null(0) != 5
        }

        [Test]
        public void MathsOp_NullAndInt_Comparison_LEqual()
        {
            var result = new ScriptVar(null, ScriptVar.Flags.Null).MathsOp(new ScriptVar(5), ScriptLex.LexTypes.LEqual);
            Assert.That(result.Bool, Is.True); // 0 <= 5
        }

        [Test]
        public void MathsOp_NullAndInt_Comparison_GEqual()
        {
            var result = new ScriptVar(null, ScriptVar.Flags.Null).MathsOp(new ScriptVar(5), ScriptLex.LexTypes.GEqual);
            Assert.That(result.Bool, Is.False); // 0 >= 5
        }

        // ── MathsOp: double branch ────────────────────────────────────────────────

        [Test]
        public void MathsOp_Double_NEqual_ReturnsTrue()
        {
            var result = new ScriptVar(1.5).MathsOp(new ScriptVar(2.5), ScriptLex.LexTypes.NEqual);
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void MathsOp_Double_LessThan_ReturnsTrue()
        {
            var result = new ScriptVar(1.5).MathsOp(new ScriptVar(2.5), (ScriptLex.LexTypes)'<');
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void MathsOp_Double_LEqual_ReturnsTrue()
        {
            var result = new ScriptVar(1.5).MathsOp(new ScriptVar(1.5), ScriptLex.LexTypes.LEqual);
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void MathsOp_Double_GEqual_ReturnsTrue()
        {
            var result = new ScriptVar(2.5).MathsOp(new ScriptVar(1.5), ScriptLex.LexTypes.GEqual);
            Assert.That(result.Bool, Is.True);
        }

        // ── MathsOp: array and object equality ───────────────────────────────────

        [Test]
        public void MathsOp_ArraySameReference_Equal_ReturnsTrue()
        {
            var arr = new ScriptVar(null, ScriptVar.Flags.Array);
            arr.SetArrayIndex(0, new ScriptVar(1));
            var result = arr.MathsOp(arr, ScriptLex.LexTypes.Equal);
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void MathsOp_ArrayDifferentReference_Equal_ReturnsFalse()
        {
            var a = new ScriptVar(null, ScriptVar.Flags.Array);
            var b = new ScriptVar(null, ScriptVar.Flags.Array);
            var result = a.MathsOp(b, ScriptLex.LexTypes.Equal);
            Assert.That(result.Bool, Is.False);
        }

        [Test]
        public void MathsOp_ArrayDifferentReference_NEqual_ReturnsTrue()
        {
            var a = new ScriptVar(null, ScriptVar.Flags.Array);
            var b = new ScriptVar(null, ScriptVar.Flags.Array);
            var result = a.MathsOp(b, ScriptLex.LexTypes.NEqual);
            Assert.That(result.Bool, Is.True);
        }

        [Test]
        public void MathsOp_ObjectSameRef_NEqual_ReturnsFalse()
        {
            var obj = new ScriptVar(null, ScriptVar.Flags.Object);
            obj.AddChild("x", new ScriptVar(1));
            // NEqual on same reference: a==b is true, so NEqual -> false
            var result = obj.MathsOp(obj, ScriptLex.LexTypes.NEqual);
            Assert.That(result.Bool, Is.False);
        }

        [Test]
        public void MathsOp_ObjectDifferentReference_Equal_ReturnsFalse()
        {
            var a = new ScriptVar(null, ScriptVar.Flags.Object);
            var b = new ScriptVar(null, ScriptVar.Flags.Object);
            var result = a.MathsOp(b, ScriptLex.LexTypes.Equal);
            Assert.That(result.Bool, Is.False);
        }

        // ── ScriptVar: indexer, Bool setter, array out-of-bounds ─────────────────

        [Test]
        public void Indexer_ReturnsNamedChild()
        {
            var obj = new ScriptVar(null, ScriptVar.Flags.Object);
            obj.AddChild("y", new ScriptVar(42));
            Assert.That(obj["y"].Int, Is.EqualTo(42));
        }

        [Test]
        public void BoolSetter_SetsIntTo1()
        {
            var v = new ScriptVar();
            v.Bool = true;
            Assert.That(v.Bool, Is.True);
        }

        [Test]
        public void BoolSetter_SetsIntTo0()
        {
            var v = new ScriptVar(1);
            v.Bool = false;
            Assert.That(v.Bool, Is.False);
        }

        [Test]
        public void GetArrayIndex_OutOfBounds_ReturnsNull()
        {
            var arr = new ScriptVar(null, ScriptVar.Flags.Array);
            arr.SetArrayIndex(0, new ScriptVar(10));
            var result = arr.GetArrayIndex(99);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void SetArrayIndex_Undefined_RemovesElement()
        {
            var arr = new ScriptVar(null, ScriptVar.Flags.Array);
            arr.SetArrayIndex(0, new ScriptVar(1));
            arr.SetArrayIndex(1, new ScriptVar(2));
            arr.SetArrayIndex(1, new ScriptVar()); // undefined removes element
            Assert.That(arr.GetArrayLength(), Is.EqualTo(1));
        }

        // ── ScriptVar: CopyValue with null ────────────────────────────────────────

        [Test]
        public void CopyValue_NullSource_SetsUndefined()
        {
            var v = new ScriptVar(42);
            v.CopyValue(null);
            Assert.That(v.IsUndefined, Is.True);
        }

        // ── ScriptVarLink: copy constructor, ToString, GetIntName/SetIntName ──────

        [Test]
        public void ScriptVarLink_CopyConstructor_CopiesNameAndVar()
        {
            var sv = new ScriptVar(42);
            var original = new ScriptVarLink(sv, "myProp");
            var copy = new ScriptVarLink(original);
            Assert.That(copy.Name, Is.EqualTo("myProp"));
            Assert.That(copy.Var.Int, Is.EqualTo(42));
        }

        [Test]
        public void ScriptVarLink_ToString_ShowsNameAndValue()
        {
            var sv = new ScriptVar(99);
            var link = new ScriptVarLink(sv, "x");
            var str = link.ToString();
            Assert.That(str, Does.Contain("x").And.Contain("99"));
        }

        [Test]
        public void ScriptVarLink_ReplaceWith_NullLink_SetsUndefined()
        {
            var sv = new ScriptVar(5);
            var link = new ScriptVarLink(sv, "n");
            link.ReplaceWith((ScriptVarLink)null);
            Assert.That(link.Var.IsUndefined, Is.True);
        }

        [Test]
        public void ScriptVarLink_GetIntName_ParsesName()
        {
            var sv = new ScriptVar(1);
            var link = new ScriptVarLink(sv, "7");
            Assert.That(link.GetIntName(), Is.EqualTo(7));
        }

        [Test]
        public void ScriptVarLink_SetIntName_UpdatesName()
        {
            var sv = new ScriptVar(1);
            var link = new ScriptVarLink(sv, "0");
            link.SetIntName(42);
            Assert.That(link.Name, Is.EqualTo("42"));
        }

        // ── GetParsableString: null and undefined paths ───────────────────────────

        [Test]
        public void GetParsableString_Null_ReturnsNullLiteral()
        {
            var v = new ScriptVar(null, ScriptVar.Flags.Null);
            Assert.That(v.GetParsableString(), Is.EqualTo("null"));
        }

        [Test]
        public void GetParsableString_Undefined_ReturnsUndefinedLiteral()
        {
            var v = new ScriptVar(); // default is undefined
            Assert.That(v.GetParsableString(), Is.EqualTo("undefined"));
        }
    }
}
