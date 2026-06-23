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

using System.Numerics;

namespace DScript.Vm
{
    public enum ConstantKind
    {
        Int,
        Double,
        String,
        Regex,
        BigInt
    }

    /// <summary>
    /// A literal value stored in a <see cref="Chunk"/>'s constant pool. Kept
    /// separate from <see cref="ScriptVar"/> (which is mutable and ref-counted)
    /// so constants are immutable, easily serialized, and materialized into a
    /// fresh <see cref="ScriptVar"/> each time they are pushed.
    /// </summary>
    public sealed class ConstantValue
    {
        public ConstantKind Kind { get; private set; }
        public int IntValue { get; private set; }
        public double DoubleValue { get; private set; }
        public string StringValue { get; private set; }
        public BigInteger BigIntValue { get; private set; }

        public static ConstantValue Int(int value) => new() { Kind = ConstantKind.Int, IntValue = value };
        public static ConstantValue Double(double value) => new() { Kind = ConstantKind.Double, DoubleValue = value };
        public static ConstantValue String(string value) => new() { Kind = ConstantKind.String, StringValue = value };
        public static ConstantValue Regex(string literal) => new() { Kind = ConstantKind.Regex, StringValue = literal };
        public static ConstantValue BigInt(BigInteger value) => new() { Kind = ConstantKind.BigInt, BigIntValue = value };

        /// <summary>Create a fresh ScriptVar for this constant (never shared).</summary>
        public ScriptVar Materialize()
        {
            switch (Kind)
            {
                case ConstantKind.Int: return new ScriptVar(IntValue);
                case ConstantKind.Double: return new ScriptVar(DoubleValue);
                case ConstantKind.String: return new ScriptVar(StringValue, ScriptVar.Flags.String);
                case ConstantKind.Regex: return new ScriptVar(StringValue, ScriptVar.Flags.Regexp);
                case ConstantKind.BigInt: return ScriptVar.CreateBigInt(BigIntValue);
                default: return new ScriptVar();
            }
        }

        public override string ToString()
        {
            return Kind switch
            {
                ConstantKind.Int => IntValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ConstantKind.Double => DoubleValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ConstantKind.String => $"\"{StringValue}\"",
                ConstantKind.Regex => StringValue,
                ConstantKind.BigInt => BigIntValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + "n",
                _ => "?"
            };
        }
    }
}
