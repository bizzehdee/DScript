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
using System.Text;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("fs")]
    public static class FsFunctionProvider
    {
        // ── Permission helpers ───────────────────────────────────────────────────

        private static void RequireFsRead(object userData, string path)
        {
            if (userData is not ScriptEngine engine) return;
            EnginePermissionStore.Require(engine, EnginePermissions.FileSystemRead);
            RequireFsPath(engine, path);
        }

        private static void RequireFsWrite(object userData, string path)
        {
            if (userData is not ScriptEngine engine) return;
            EnginePermissionStore.Require(engine, EnginePermissions.FileSystemWrite);
            RequireFsPath(engine, path);
        }

        // Throws PermissionException(FileSystemEscape) when the resolved path is
        // outside the process working directory and FileSystemEscape is not granted.
        private static void RequireFsPath(ScriptEngine engine, string path)
        {
            if ((EnginePermissionStore.Get(engine) & EnginePermissions.FileSystemEscape) != 0)
                return; // escape explicitly granted — no further check

            var absPath  = Path.GetFullPath(path);
            var cwd      = Path.GetFullPath(Directory.GetCurrentDirectory());
            // Use case-insensitive comparison on Windows (FAT/NTFS), ordinal on Unix.
            var cmp      = Path.DirectorySeparatorChar == '\\'
                           ? StringComparison.OrdinalIgnoreCase
                           : StringComparison.Ordinal;
            var cwdSlash = cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                           + Path.DirectorySeparatorChar;

            if (!absPath.StartsWith(cwdSlash, cmp)
                && !string.Equals(absPath, cwd, cmp))
            {
                EnginePermissionStore.Require(engine, EnginePermissions.FileSystemEscape);
            }
        }

        // ── Read operations ──────────────────────────────────────────────────────

        [ScriptMethod("readFileSync", "path", "enc")]
        public static void FsReadFileSyncImpl(ScriptVar var, object userData)
        {
            var path = var.GetParameter("path").String;
            RequireFsRead(userData, path);
            var encVar = var.GetParameter("enc");
            if (!encVar.IsUndefined && encVar.String == "buffer")
            {
                var bytes = File.ReadAllBytes(path);
                var arr = new ScriptVar();
                arr.SetArray();
                for (var i = 0; i < bytes.Length; i++)
                    arr.SetArrayIndex(i, new ScriptVar(bytes[i]));
                var.ReturnVar = arr;
            }
            else
            {
                var enc = GetEncoding(encVar.IsUndefined ? "utf8" : encVar.String);
                var.ReturnVar.String = File.ReadAllText(path, enc);
            }
        }

        [ScriptMethod("existsSync", "path")]
        public static void FsExistsSyncImpl(ScriptVar var, object userData)
        {
            var path = var.GetParameter("path").String;
            RequireFsRead(userData, path);
            var.ReturnVar.Bool = File.Exists(path) || Directory.Exists(path);
        }

        [ScriptMethod("readdirSync", "path")]
        public static void FsReaddirSyncImpl(ScriptVar var, object userData)
        {
            var path = var.GetParameter("path").String;
            RequireFsRead(userData, path);
            var entries = Directory.GetFileSystemEntries(path);
            var arr = new ScriptVar();
            arr.SetArray();
            for (var i = 0; i < entries.Length; i++)
                arr.SetArrayIndex(i, new ScriptVar(Path.GetFileName(entries[i])));
            var.ReturnVar = arr;
        }

        [ScriptMethod("statSync", "path")]
        public static void FsStatSyncImpl(ScriptVar var, object userData)
        {
            var path = var.GetParameter("path").String;
            RequireFsRead(userData, path);
            var isFile = File.Exists(path);
            var isDir  = !isFile && Directory.Exists(path);

            long size     = 0;
            double mtimeMs = 0;
            if (isFile)
            {
                var fi  = new FileInfo(path);
                size    = fi.Length;
                mtimeMs = (fi.LastWriteTimeUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            }
            else if (isDir)
            {
                var di  = new DirectoryInfo(path);
                mtimeMs = (di.LastWriteTimeUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            }

            var stat = new ScriptVar(ScriptVar.Flags.Object);
            stat.AddChild("size",    new ScriptVar((int)size));
            stat.AddChild("mtimeMs", new ScriptVar(mtimeMs));

            var isFileCaptured = isFile;
            var isDirCaptured  = isDir;

            var isFileFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            isFileFn.SetCallback((scope, _) => { scope.ReturnVar = new ScriptVar(isFileCaptured); }, null);

            var isDirFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            isDirFn.SetCallback((scope, _) => { scope.ReturnVar = new ScriptVar(isDirCaptured); }, null);

            stat.AddChild("isFile",      isFileFn);
            stat.AddChild("isDirectory", isDirFn);
            var.ReturnVar = stat;
        }

        // ── Write operations ─────────────────────────────────────────────────────

        [ScriptMethod("writeFileSync", "path", "data", "enc")]
        public static void FsWriteFileSyncImpl(ScriptVar var, object userData)
        {
            var path = var.GetParameter("path").String;
            RequireFsWrite(userData, path);
            var dataVar = var.GetParameter("data");
            var encVar  = var.GetParameter("enc");
            if (dataVar.IsArray)
            {
                var len   = dataVar.GetArrayLength();
                var bytes = new byte[len];
                for (var i = 0; i < len; i++)
                    bytes[i] = (byte)dataVar.GetArrayIndex(i).Int;
                File.WriteAllBytes(path, bytes);
            }
            else
            {
                var enc = GetEncoding(encVar.IsUndefined ? "utf8" : encVar.String);
                File.WriteAllText(path, dataVar.String, enc);
            }
        }

        [ScriptMethod("appendFileSync", "path", "data", "enc")]
        public static void FsAppendFileSyncImpl(ScriptVar var, object userData)
        {
            var path = var.GetParameter("path").String;
            RequireFsWrite(userData, path);
            var dataVar = var.GetParameter("data");
            var encVar  = var.GetParameter("enc");
            var enc     = GetEncoding(encVar.IsUndefined ? "utf8" : encVar.String);
            File.AppendAllText(path, dataVar.String, enc);
        }

        [ScriptMethod("mkdirSync", "path", "opts")]
        public static void FsMkdirSyncImpl(ScriptVar var, object userData)
        {
            var path = var.GetParameter("path").String;
            RequireFsWrite(userData, path);
            var opts = var.GetParameter("opts");
            if (!opts.IsUndefined)
            {
                var recChild = opts.FindChild("recursive");
                if (recChild != null && recChild.Var.Bool)
                {
                    Directory.CreateDirectory(path);
                    return;
                }
            }
            Directory.CreateDirectory(path);
        }

        [ScriptMethod("rmdirSync", "path")]
        public static void FsRmdirSyncImpl(ScriptVar var, object userData)
        {
            var path = var.GetParameter("path").String;
            RequireFsWrite(userData, path);
            Directory.Delete(path);
        }

        [ScriptMethod("unlinkSync", "path")]
        public static void FsUnlinkSyncImpl(ScriptVar var, object userData)
        {
            var path = var.GetParameter("path").String;
            RequireFsWrite(userData, path);
            File.Delete(path);
        }

        [ScriptMethod("renameSync", "oldPath", "newPath")]
        public static void FsRenameSyncImpl(ScriptVar var, object userData)
        {
            var oldPath = var.GetParameter("oldPath").String;
            var newPath = var.GetParameter("newPath").String;
            RequireFsWrite(userData, oldPath);
            RequireFsWrite(userData, newPath);
            File.Move(oldPath, newPath);
        }

        [ScriptMethod("copyFileSync", "src", "dest")]
        public static void FsCopyFileSyncImpl(ScriptVar var, object userData)
        {
            var src  = var.GetParameter("src").String;
            var dest = var.GetParameter("dest").String;
            RequireFsRead(userData, src);
            RequireFsWrite(userData, dest);
            File.Copy(src, dest, overwrite: true);
        }

        // ── Encoding helper ──────────────────────────────────────────────────────

        private static Encoding GetEncoding(string name)
        {
            return (name ?? "utf8").ToLowerInvariant() switch
            {
                "utf8" or "utf-8" => Encoding.UTF8,
                "ascii"           => Encoding.ASCII,
                "latin1" or "binary" => Encoding.Latin1,
                "base64"          => Encoding.UTF8,
                _                 => Encoding.UTF8
            };
        }
    }
}
