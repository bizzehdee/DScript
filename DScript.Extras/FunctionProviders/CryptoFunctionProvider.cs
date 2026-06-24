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

using System;
using System.Security.Cryptography;
using System.Text;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("crypto")]
    public static class CryptoFunctionProvider
    {
        [ScriptMethod("randomUUID")]
        public static void CryptoRandomUUIDImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.String = Guid.NewGuid().ToString();
        }

        [ScriptMethod("randomBytes", "n")]
        public static void CryptoRandomBytesImpl(ScriptVar var, object userData)
        {
            var n = var.GetParameter("n").Int;
            if (n < 0) n = 0;
            var bytes = RandomNumberGenerator.GetBytes(n);
            var result = ScriptVar.CreateUndefined();
            result.SetArray();
            for (var i = 0; i < bytes.Length; i++)
                result.SetArrayIndex(i, ScriptVar.FromInt(bytes[i]));
            var.ReturnVar = result;
        }

        [ScriptMethod("getRandomValues", "arr")]
        public static void CryptoGetRandomValuesImpl(ScriptVar var, object userData)
        {
            var arr = var.GetParameter("arr");
            var len = arr.GetArrayLength();
            var bytes = RandomNumberGenerator.GetBytes(len * 4);
            for (var i = 0; i < len; i++)
            {
                var value = BitConverter.ToInt32(bytes, i * 4);
                arr.SetArrayIndex(i, ScriptVar.FromInt(value));
            }
            var.ReturnVar = arr;
        }

        [ScriptMethod("createHash", "algo")]
        public static void CryptoCreateHashImpl(ScriptVar var, object userData)
        {
            var algo = var.GetParameter("algo").String;
            var hashObj = ScriptVar.CreateObject();
            var accumulator = new StringBuilder();

            hashObj.AddChild("update", MakeUpdateFn(accumulator));
            hashObj.AddChild("digest", MakeHashDigestFn(algo, accumulator));
            var.ReturnVar = hashObj;
        }

        [ScriptMethod("createHmac", "algo", "key")]
        public static void CryptoCreateHmacImpl(ScriptVar var, object userData)
        {
            var algo = var.GetParameter("algo").String;
            var key = var.GetParameter("key").String;
            var accumulator = new StringBuilder();

            var hmacObj = ScriptVar.CreateObject();
            hmacObj.AddChild("update", MakeUpdateFn(accumulator));
            hmacObj.AddChild("digest", MakeHmacDigestFn(algo, key, accumulator));
            var.ReturnVar = hmacObj;
        }

        private static ScriptVar MakeUpdateFn(StringBuilder accumulator)
        {
            var fn = ScriptVar.CreateNativeFunction();
            fn.AddChild("data", ScriptVar.CreateUndefined());
            fn.SetCallback((scope, _) =>
            {
                var data = scope.FindChild("data")?.Var;
                if (data != null && !data.IsUndefined)
                    accumulator.Append(data.String);
                // return this (the parent hash/hmac object) for chaining — not easily
                // accessible here, so we return undefined (callers rarely chain)
            }, null);
            return fn;
        }

        private static ScriptVar MakeHashDigestFn(string algo, StringBuilder accumulator)
        {
            var fn = ScriptVar.CreateNativeFunction();
            fn.AddChild("enc", ScriptVar.CreateUndefined());
            fn.SetCallback((scope, _) =>
            {
                var input = Encoding.UTF8.GetBytes(accumulator.ToString());
                var hashBytes = ComputeHash(algo, input);
                var encVar = scope.FindChild("enc")?.Var;
                var enc = (encVar == null || encVar.IsUndefined) ? "hex" : encVar.String;
                var retVar = scope.FindChild(ScriptVar.ReturnVarName)?.Var;
                if (retVar != null)
                    retVar.String = enc == "base64"
                        ? Convert.ToBase64String(hashBytes)
                        : BytesToHex(hashBytes);
            }, null);
            return fn;
        }

        private static ScriptVar MakeHmacDigestFn(string algo, string key, StringBuilder accumulator)
        {
            var fn = ScriptVar.CreateNativeFunction();
            fn.AddChild("enc", ScriptVar.CreateUndefined());
            fn.SetCallback((scope, _) =>
            {
                var input = Encoding.UTF8.GetBytes(accumulator.ToString());
                var keyBytes = Encoding.UTF8.GetBytes(key);
                var hashBytes = ComputeHmac(algo, keyBytes, input);
                var encVar = scope.FindChild("enc")?.Var;
                var enc = (encVar == null || encVar.IsUndefined) ? "hex" : encVar.String;
                var retVar = scope.FindChild(ScriptVar.ReturnVarName)?.Var;
                if (retVar != null)
                    retVar.String = enc == "base64"
                        ? Convert.ToBase64String(hashBytes)
                        : BytesToHex(hashBytes);
            }, null);
            return fn;
        }

        private static byte[] ComputeHash(string algo, byte[] data)
        {
            return algo.ToLower() switch
            {
                "sha256" => SHA256.HashData(data),
                "sha512" => SHA512.HashData(data),
                "sha1"   => SHA1.HashData(data),
                "md5"    => MD5.HashData(data),
                _ => SHA256.HashData(data),
            };
        }

        private static byte[] ComputeHmac(string algo, byte[] key, byte[] data)
        {
            return algo.ToLower() switch
            {
                "sha256" => HMACSHA256.HashData(key, data),
                "sha512" => HMACSHA512.HashData(key, data),
                "sha1"   => HMACSHA1.HashData(key, data),
                "md5"    => HMACMD5.HashData(key, data),
                _ => HMACSHA256.HashData(key, data),
            };
        }

        private static string BytesToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
