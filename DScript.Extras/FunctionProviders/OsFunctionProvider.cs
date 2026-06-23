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
using System.IO;
using System.Net;
using System.Runtime.InteropServices;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("os")]
    public static class OsFunctionProvider
    {
        [ScriptMethod("hostname")]
        public static void OsHostnameImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.String = Dns.GetHostName();
        }

        [ScriptMethod("platform")]
        public static void OsPlatformImpl(ScriptVar var, object userData)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                var.ReturnVar.String = "win32";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                var.ReturnVar.String = "darwin";
            else
                var.ReturnVar.String = "linux";
        }

        [ScriptMethod("arch")]
        public static void OsArchImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.String = RuntimeInformation.ProcessArchitecture.ToString().ToLower();
        }

        [ScriptMethod("homedir")]
        public static void OsHomedirImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.String = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        [ScriptMethod("tmpdir")]
        public static void OsTmpdirImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.String = Path.GetTempPath().TrimEnd('/', '\\');
        }

        [ScriptMethod("totalmem")]
        public static void OsTotalmemImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = (double)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }

        [ScriptMethod("freemem")]
        public static void OsFreememImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Float = (double)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }

        [ScriptMethod("cpus")]
        public static void OsCpusImpl(ScriptVar var, object userData)
        {
            var.ReturnVar.Int = Environment.ProcessorCount;
        }

        [ScriptProperty("EOL")]
        public static void OsEolImpl(ScriptVar var, object userData)
        {
            var.String = Environment.NewLine;
        }
    }
}
