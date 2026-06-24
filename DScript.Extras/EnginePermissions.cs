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
using System.Runtime.CompilerServices;

namespace DScript.Extras
{
    /// <summary>
    /// Permission flags controlling which host capabilities a DScript engine may access.
    /// Flags are composable; write permissions automatically include their read counterpart.
    /// </summary>
    [Flags]
    public enum EnginePermissions
    {
        None = 0,

        // ── File-system ──────────────────────────────────────────────────────────
        // Individual read/write bits (bit 0 = read, bit 5 = write-extra).
        // FileSystemWrite includes FileSystemRead so that (granted & FileSystemRead)
        // is always true when write is granted.
        /// <summary>Allow read-only fs operations (readFileSync, existsSync, statSync, readdirSync).</summary>
        FileSystemRead   = 1 << 0,                          // 1
        /// <summary>Allow write fs operations (writeFileSync, mkdirSync, etc.). Implies <see cref="FileSystemRead"/>.</summary>
        FileSystemWrite  = (1 << 5) | FileSystemRead,       // 33
        /// <summary>
        /// Allow fs operations that access paths outside the process working directory.
        /// Off by default; must be granted explicitly for scripts that need absolute paths
        /// or paths that escape the current working directory.
        /// </summary>
        FileSystemEscape = 1 << 6,                          // 64

        /// <summary>Read + write file-system access, confined to the current working directory. Does not include <see cref="FileSystemEscape"/>.</summary>
        FileSystem       = FileSystemRead | FileSystemWrite, // 33  (backwards-compat alias)
        /// <summary>Unrestricted file-system access including paths outside the working directory.</summary>
        FileSystemUnsafe = FileSystem | FileSystemEscape,    // 97

        // ── Network ──────────────────────────────────────────────────────────────
        /// <summary>Allow network operations (fetch, http, net).</summary>
        Network = 1 << 1,                                    // 2

        // ── Process ──────────────────────────────────────────────────────────────
        /// <summary>Allow spawning child processes. Off by default.</summary>
        ProcessSpawn = 1 << 2,                               // 4
        /// <summary>Allow process.exit(). Off by default.</summary>
        ProcessExit  = 1 << 3,                               // 8

        // ── Environment variables ─────────────────────────────────────────────────
        // Bit 4 = read, bit 7 = write-extra (write implies read).
        /// <summary>Allow reading environment variables (process.getenv, process.env).</summary>
        EnvironmentVariablesRead  = 1 << 4,                          // 16
        /// <summary>Allow writing environment variables (process.setenv). Implies <see cref="EnvironmentVariablesRead"/>.</summary>
        EnvironmentVariablesWrite = (1 << 7) | EnvironmentVariablesRead, // 144

        /// <summary>Read-only environment variable access. Alias for <see cref="EnvironmentVariablesRead"/> (backwards-compat).</summary>
        EnvironmentVariables = EnvironmentVariablesRead,             // 16

        // ── Composites ───────────────────────────────────────────────────────────
        /// <summary>All permissions granted.</summary>
        All = ~0,
    }

    /// <summary>
    /// Thrown when a DScript operation is blocked by the permission model.
    /// Not catchable by script-level <c>try/catch</c>.
    /// </summary>
    public sealed class PermissionException : Exception
    {
        public EnginePermissions Required { get; }

        public PermissionException(EnginePermissions required)
            : base($"Permission denied: {required} is not granted.")
        {
            Required = required;
        }
    }

    /// <summary>
    /// Associates <see cref="EnginePermissions"/> with a <see cref="ScriptEngine"/> instance
    /// without modifying the core DScript library.
    /// </summary>
    public static class EnginePermissionStore
    {
        private static readonly ConditionalWeakTable<ScriptEngine, PermissionsBox> _store = new();

        private sealed class PermissionsBox { public EnginePermissions Value; }

        public static void Set(ScriptEngine engine, EnginePermissions permissions)
        {
            var box = _store.GetOrCreateValue(engine);
            box.Value = permissions;
        }

        public static EnginePermissions Get(ScriptEngine engine)
        {
            return _store.TryGetValue(engine, out var box) ? box.Value : EnginePermissions.All;
        }

        /// <summary>
        /// Throws <see cref="PermissionException"/> if the engine lacks <paramref name="required"/>.
        /// </summary>
        public static void Require(ScriptEngine engine, EnginePermissions required)
        {
            var granted = Get(engine);
            if ((granted & required) != required)
                throw new PermissionException(required);
        }
    }
}
