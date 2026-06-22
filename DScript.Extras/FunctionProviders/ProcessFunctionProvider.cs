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
using System.Runtime.InteropServices;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("process")]
    public static class ProcessFunctionProvider
    {
        [ScriptMethod("exit", "code")]
        public static void ProcessExitImpl(ScriptVar var, object userData)
        {
            var codeVar = var.GetParameter("code");
            Environment.Exit(codeVar.IsUndefined ? 0 : codeVar.Int);
        }

        [ScriptProperty("platform")]
        public static void ProcessPlatformImpl(ScriptVar var, object userData)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                var.String = "win32";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                var.String = "linux";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                var.String = "darwin";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
                var.String = "freebsd";
            else
                var.String = "unknown";
        }

        [ScriptProperty("version")]
        public static void ProcessVersionImpl(ScriptVar var, object userData)
        {
            var.String = "dscript/1.0.0";
        }

        [ScriptMethod("argv")]
        public static void ProcessArgvImpl(ScriptVar var, object userData)
        {
            var engine = (ScriptEngine)userData;
            var argvLink = engine.Root.FindChild("__argv__");
            if (argvLink != null)
                var.ReturnVar = argvLink.Var;
            else
            {
                var empty = new ScriptVar();
                empty.SetArray();
                var.ReturnVar = empty;
            }
        }

        [ScriptMethod("getenv", "name")]
        public static void ProcessGetenvImpl(ScriptVar var, object userData)
        {
            var name = var.GetParameter("name").String;
            var value = Environment.GetEnvironmentVariable(name);
            if (value == null)
                var.ReturnVar.SetUndefined();
            else
                var.ReturnVar.String = value;
        }
    }
}
