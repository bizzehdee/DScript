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
    /// </summary>
    [Flags]
    public enum EnginePermissions
    {
        None                 = 0,
        FileSystem           = 1 << 0,
        Network              = 1 << 1,
        ProcessSpawn         = 1 << 2,
        ProcessExit          = 1 << 3,
        EnvironmentVariables = 1 << 4,
        /// <summary>All permissions granted (default for backwards compatibility).</summary>
        All                  = ~0
    }

    /// <summary>
    /// Thrown when a DScript operation is blocked by the permission model.
    /// Catchable by the host but not by script code.
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
