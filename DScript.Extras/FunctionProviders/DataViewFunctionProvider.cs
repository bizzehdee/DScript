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

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("DataView")]
    public static class DataViewFunctionProvider
    {
        private static DataViewObject GetDV(ScriptVar thisVar)
            => thisVar.GetData() as DataViewObject
               ?? throw new JITException(MakeTypeError("not a DataView"));

        private static bool LE(ScriptVar v) => !v.IsUndefined && v.Bool;

        // ── readers ──────────────────────────────────────────────────────────

        [ScriptMethod("getInt8", "byteOffset")]
        public static void DVGetInt8(ScriptVar var, object _)
            => var.ReturnVar.Int = GetDV(var.GetParameter("this")).GetInt8(var.GetParameter("byteOffset").Int);

        [ScriptMethod("getUint8", "byteOffset")]
        public static void DVGetUint8(ScriptVar var, object _)
            => var.ReturnVar.Int = GetDV(var.GetParameter("this")).GetUint8(var.GetParameter("byteOffset").Int);

        [ScriptMethod("getInt16", "byteOffset", "littleEndian")]
        public static void DVGetInt16(ScriptVar var, object _)
            => var.ReturnVar.Int = GetDV(var.GetParameter("this")).GetInt16(var.GetParameter("byteOffset").Int, LE(var.GetParameter("littleEndian")));

        [ScriptMethod("getUint16", "byteOffset", "littleEndian")]
        public static void DVGetUint16(ScriptVar var, object _)
            => var.ReturnVar.Int = GetDV(var.GetParameter("this")).GetUint16(var.GetParameter("byteOffset").Int, LE(var.GetParameter("littleEndian")));

        [ScriptMethod("getInt32", "byteOffset", "littleEndian")]
        public static void DVGetInt32(ScriptVar var, object _)
            => var.ReturnVar.Int = GetDV(var.GetParameter("this")).GetInt32(var.GetParameter("byteOffset").Int, LE(var.GetParameter("littleEndian")));

        [ScriptMethod("getUint32", "byteOffset", "littleEndian")]
        public static void DVGetUint32(ScriptVar var, object _)
            => var.ReturnVar = ScriptVar.FromDouble(GetDV(var.GetParameter("this")).GetUint32(var.GetParameter("byteOffset").Int, LE(var.GetParameter("littleEndian"))));

        [ScriptMethod("getFloat32", "byteOffset", "littleEndian")]
        public static void DVGetFloat32(ScriptVar var, object _)
            => var.ReturnVar = ScriptVar.FromDouble(GetDV(var.GetParameter("this")).GetFloat32(var.GetParameter("byteOffset").Int, LE(var.GetParameter("littleEndian"))));

        [ScriptMethod("getFloat64", "byteOffset", "littleEndian")]
        public static void DVGetFloat64(ScriptVar var, object _)
            => var.ReturnVar = ScriptVar.FromDouble(GetDV(var.GetParameter("this")).GetFloat64(var.GetParameter("byteOffset").Int, LE(var.GetParameter("littleEndian"))));

        [ScriptMethod("getBigInt64", "byteOffset", "littleEndian")]
        public static void DVGetBigInt64(ScriptVar var, object _)
            => var.ReturnVar = ScriptVar.CreateBigInt(new System.Numerics.BigInteger(GetDV(var.GetParameter("this")).GetBigInt64(var.GetParameter("byteOffset").Int, LE(var.GetParameter("littleEndian")))));

        [ScriptMethod("getBigUint64", "byteOffset", "littleEndian")]
        public static void DVGetBigUint64(ScriptVar var, object _)
            => var.ReturnVar = ScriptVar.CreateBigInt(new System.Numerics.BigInteger(GetDV(var.GetParameter("this")).GetBigUint64(var.GetParameter("byteOffset").Int, LE(var.GetParameter("littleEndian")))));

        // ── writers ──────────────────────────────────────────────────────────

        [ScriptMethod("setInt8", "byteOffset", "value")]
        public static void DVSetInt8(ScriptVar var, object _)
            => GetDV(var.GetParameter("this")).SetInt8(var.GetParameter("byteOffset").Int, (sbyte)var.GetParameter("value").Int);

        [ScriptMethod("setUint8", "byteOffset", "value")]
        public static void DVSetUint8(ScriptVar var, object _)
            => GetDV(var.GetParameter("this")).SetUint8(var.GetParameter("byteOffset").Int, (byte)var.GetParameter("value").Int);

        [ScriptMethod("setInt16", "byteOffset", "value", "littleEndian")]
        public static void DVSetInt16(ScriptVar var, object _)
            => GetDV(var.GetParameter("this")).SetInt16(var.GetParameter("byteOffset").Int, (short)var.GetParameter("value").Int, LE(var.GetParameter("littleEndian")));

        [ScriptMethod("setUint16", "byteOffset", "value", "littleEndian")]
        public static void DVSetUint16(ScriptVar var, object _)
            => GetDV(var.GetParameter("this")).SetUint16(var.GetParameter("byteOffset").Int, (ushort)var.GetParameter("value").Int, LE(var.GetParameter("littleEndian")));

        [ScriptMethod("setInt32", "byteOffset", "value", "littleEndian")]
        public static void DVSetInt32(ScriptVar var, object _)
            => GetDV(var.GetParameter("this")).SetInt32(var.GetParameter("byteOffset").Int, var.GetParameter("value").Int, LE(var.GetParameter("littleEndian")));

        [ScriptMethod("setUint32", "byteOffset", "value", "littleEndian")]
        public static void DVSetUint32(ScriptVar var, object _)
            => GetDV(var.GetParameter("this")).SetUint32(var.GetParameter("byteOffset").Int, (uint)(long)var.GetParameter("value").Float, LE(var.GetParameter("littleEndian")));

        [ScriptMethod("setFloat32", "byteOffset", "value", "littleEndian")]
        public static void DVSetFloat32(ScriptVar var, object _)
            => GetDV(var.GetParameter("this")).SetFloat32(var.GetParameter("byteOffset").Int, (float)var.GetParameter("value").Float, LE(var.GetParameter("littleEndian")));

        [ScriptMethod("setFloat64", "byteOffset", "value", "littleEndian")]
        public static void DVSetFloat64(ScriptVar var, object _)
            => GetDV(var.GetParameter("this")).SetFloat64(var.GetParameter("byteOffset").Int, var.GetParameter("value").Float, LE(var.GetParameter("littleEndian")));

        [ScriptMethod("setBigInt64", "byteOffset", "value", "littleEndian")]
        public static void DVSetBigInt64(ScriptVar var, object _)
        {
            var v = var.GetParameter("value");
            var raw = v.IsBigInt ? (long)v.BigIntData : (long)v.Float;
            GetDV(var.GetParameter("this")).SetBigInt64(var.GetParameter("byteOffset").Int, raw, LE(var.GetParameter("littleEndian")));
        }

        [ScriptMethod("setBigUint64", "byteOffset", "value", "littleEndian")]
        public static void DVSetBigUint64(ScriptVar var, object _)
        {
            var v = var.GetParameter("value");
            var raw = v.IsBigInt ? (ulong)v.BigIntData : (ulong)(long)v.Float;
            GetDV(var.GetParameter("this")).SetBigUint64(var.GetParameter("byteOffset").Int, raw, LE(var.GetParameter("littleEndian")));
        }

        private static ScriptVar MakeTypeError(string message)
        {
            var err = ScriptVar.CreateObject();
            err.AddChild("name", ScriptVar.FromString("TypeError"));
            err.AddChild("message", ScriptVar.FromString(message));
            return err;
        }
    }
}
