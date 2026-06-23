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
using System.Diagnostics;
using System.Text;

namespace DScript.Extras.FunctionProviders
{
    [ScriptClass("child_process")]
    public static class ChildProcessFunctionProvider
    {
        private static void RequireSpawn(object userData)
        {
            if (userData is ScriptEngine engine)
                EnginePermissionStore.Require(engine, EnginePermissions.ProcessSpawn);
        }

        private static (string file, string args) ParseShellCmd(string cmd)
        {
            if (OperatingSystem.IsWindows())
                return ("cmd.exe", $"/c {cmd}");
            return ("/bin/sh", $"-c {cmd}");
        }

        private static int GetTimeout(ScriptVar opts)
        {
            if (opts.IsUndefined) return 30000;
            var t = opts.FindChild("timeout");
            return t != null ? t.Var.Int : 30000;
        }

        private static string GetCwd(ScriptVar opts)
        {
            if (opts.IsUndefined) return null;
            var c = opts.FindChild("cwd");
            return c != null && !c.Var.IsUndefined ? c.Var.String : null;
        }

        private static Encoding GetEncoding(ScriptVar opts)
        {
            if (opts.IsUndefined) return Encoding.UTF8;
            var enc = opts.FindChild("encoding");
            if (enc == null || enc.Var.IsUndefined) return Encoding.UTF8;
            return enc.Var.String.ToLowerInvariant() switch
            {
                "ascii" => Encoding.ASCII,
                "latin1" or "binary" => Encoding.Latin1,
                _ => Encoding.UTF8,
            };
        }

        [ScriptMethod("execSync", "cmd", "opts")]
        public static void ExecSyncImpl(ScriptVar var, object userData)
        {
            RequireSpawn(userData);
            var cmd = var.GetParameter("cmd").String;
            var opts = var.GetParameter("opts");
            var (file, args) = ParseShellCmd(cmd);
            var enc = GetEncoding(opts);
            var cwd = GetCwd(opts);
            var timeout = GetTimeout(opts);

            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = enc,
                StandardErrorEncoding = enc,
            };
            if (cwd != null) psi.WorkingDirectory = cwd;

            using var proc = Process.Start(psi);
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            if (!proc.WaitForExit(timeout))
            {
                proc.Kill(entireProcessTree: true);
                throw new ScriptException($"execSync: command timed out after {timeout}ms");
            }
            if (proc.ExitCode != 0)
                throw new ScriptException($"Command failed: {stderr.Trim()}");

            var.ReturnVar.String = stdout;
        }

        [ScriptMethod("spawnSync", "cmd", "args", "opts")]
        public static void SpawnSyncImpl(ScriptVar var, object userData)
        {
            RequireSpawn(userData);
            var cmd = var.GetParameter("cmd").String;
            var argsVar = var.GetParameter("args");
            var opts = var.GetParameter("opts");
            var enc = GetEncoding(opts);
            var cwd = GetCwd(opts);
            var timeout = GetTimeout(opts);

            var sb = new StringBuilder();
            if (!argsVar.IsUndefined && argsVar.IsArray)
            {
                var len = argsVar.GetArrayLength();
                for (var i = 0; i < len; i++)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(argsVar.GetArrayIndex(i).String);
                }
            }

            var psi = new ProcessStartInfo(cmd, sb.ToString())
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = enc,
                StandardErrorEncoding = enc,
            };
            if (cwd != null) psi.WorkingDirectory = cwd;

            int status;
            string stdout, stderr;
            string signal = null;

            try
            {
                using var proc = Process.Start(psi);
                stdout = proc.StandardOutput.ReadToEnd();
                stderr = proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(timeout))
                {
                    proc.Kill(entireProcessTree: true);
                    signal = "SIGTERM";
                    status = -1;
                }
                else
                {
                    status = proc.ExitCode;
                }
            }
            catch (Exception ex)
            {
                throw new ScriptException($"spawnSync failed: {ex.Message}");
            }

            var result = new ScriptVar(ScriptVar.Flags.Object);
            result.AddChild("stdout", new ScriptVar(stdout));
            result.AddChild("stderr", new ScriptVar(stderr));
            result.AddChild("status", new ScriptVar(status));
            if (signal != null)
                result.AddChild("signal", new ScriptVar(signal));
            else
                result.AddChild("signal", new ScriptVar(ScriptVar.Flags.Null));

            var.ReturnVar = result;
        }

        [ScriptMethod("exec", "cmd", "cb")]
        public static void ExecImpl(ScriptVar var, object userData)
        {
            RequireSpawn(userData);
            var engine = userData as ScriptEngine;
            var cmd = var.GetParameter("cmd").String;
            var cb = var.GetParameter("cb");
            var (file, shellArgs) = ParseShellCmd(cmd);

            var psi = new ProcessStartInfo(file, shellArgs)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            string stdout, stderr;
            int exitCode;
            try
            {
                using var proc = Process.Start(psi);
                stdout = proc.StandardOutput.ReadToEnd();
                stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                exitCode = proc.ExitCode;
            }
            catch (Exception ex)
            {
                if (cb.IsFunction && engine != null)
                    engine.CallFunction(cb, null, new ScriptVar(ex.Message));
                return;
            }

            if (cb.IsFunction && engine != null)
            {
                var errArg = exitCode != 0 ? new ScriptVar(stderr.Trim()) : new ScriptVar(ScriptVar.Flags.Null);
                engine.CallFunction(cb, null, errArg, new ScriptVar(stdout), new ScriptVar(stderr));
            }
        }

        [ScriptMethod("spawn", "cmd", "args", "opts")]
        public static void SpawnImpl(ScriptVar var, object userData)
        {
            RequireSpawn(userData);
            var engine = userData as ScriptEngine;
            var cmd = var.GetParameter("cmd").String;
            var argsVar = var.GetParameter("args");
            var opts = var.GetParameter("opts");

            var sb = new StringBuilder();
            if (!argsVar.IsUndefined && argsVar.IsArray)
            {
                var argLen = argsVar.GetArrayLength();
                for (var i = 0; i < argLen; i++)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(argsVar.GetArrayIndex(i).String);
                }
            }

            var psi = new ProcessStartInfo(cmd, sb.ToString())
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            var cwd = GetCwd(opts);
            if (cwd != null) psi.WorkingDirectory = cwd;
            var timeout = GetTimeout(opts);

            // Hidden property keys for completed state and handler arrays.
            const string exitCodeKey = "__cpExitCode__";  // int or undefined (not yet done)
            const string stdoutKey   = "__cpStdout__";
            const string dataKey     = "__cpData__";
            const string errorKey    = "__cpError__";

            var procObj = new ScriptVar(ScriptVar.Flags.Object);
            // Mark as not-yet-exited; set to int after the process finishes.
            procObj.AddChild(exitCodeKey, new ScriptVar(ScriptVar.Flags.Undefined));
            procObj.AddChild(stdoutKey, new ScriptVar(ScriptVar.Flags.Undefined));

            var dataHandlers = new ScriptVar(); dataHandlers.SetArray();
            var errorHandlers = new ScriptVar(); errorHandlers.SetArray();
            procObj.AddChild(dataKey, dataHandlers);
            procObj.AddChild(errorKey, errorHandlers);

            // .on(event, fn) — if the process has already exited (exitCodeKey is an int),
            // fire 'exit' / 'data' immediately; otherwise store for deferred firing.
            var capturedObj = procObj;
            var capturedEngine = engine;
            var onFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            onFn.AddChild("event", new ScriptVar(ScriptVar.Flags.Undefined));
            onFn.AddChild("fn", new ScriptVar(ScriptVar.Flags.Undefined));
            onFn.SetCallback((scope, _) =>
            {
                var evName = scope.FindChild("event")?.Var?.String ?? "";
                var fn = scope.FindChild("fn")?.Var;
                if (fn == null || !fn.IsFunction || capturedEngine == null) return;

                var exitCodeVar = capturedObj.FindChild(exitCodeKey)?.Var;
                var done = exitCodeVar != null && !exitCodeVar.IsUndefined;

                if (evName == "exit")
                {
                    if (done)
                        capturedEngine.CallFunction(fn, null, exitCodeVar);
                    // else: process still running — in sync model this can't happen
                }
                else if (evName == "data")
                {
                    if (done)
                    {
                        var so = capturedObj.FindChild(stdoutKey)?.Var;
                        if (so != null && !so.IsUndefined)
                            capturedEngine.CallFunction(fn, null, so);
                    }
                    else
                    {
                        var arr = capturedObj.FindChild(dataKey)?.Var;
                        arr?.SetArrayIndex(arr.GetArrayLength(), fn);
                    }
                }
                else if (evName == "error")
                {
                    var arr = capturedObj.FindChild(errorKey)?.Var;
                    arr?.SetArrayIndex(arr.GetArrayLength(), fn);
                }
            }, null);
            procObj.AddChild("on", onFn);

            // Run the process synchronously, then set exitCodeKey so subsequent .on() calls
            // fire immediately. DScript has no async I/O loop; spawn is blocking.
            string stdout = string.Empty;
            int exitCode = -1;
            try
            {
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    procObj.AddChild("pid", new ScriptVar(proc.Id));
                    stdout = proc.StandardOutput.ReadToEnd();
                    proc.StandardError.ReadToEnd(); // drain stderr to prevent deadlock
                    proc.WaitForExit(timeout);
                    exitCode = proc.ExitCode;
                }
                else
                {
                    procObj.AddChild("pid", new ScriptVar(-1));
                }
            }
            catch (Exception ex)
            {
                if (engine != null)
                {
                    var errArr = capturedObj.FindChild(errorKey)?.Var;
                    if (errArr != null)
                    {
                        var errLen = errArr.GetArrayLength();
                        for (var i = 0; i < errLen; i++)
                        {
                            var fn = errArr.GetArrayIndex(i);
                            if (fn.IsFunction)
                                engine.CallFunction(fn, null, new ScriptVar(ex.Message));
                        }
                    }
                }
                var.ReturnVar = procObj;
                return;
            }

            // Stamp completed state so subsequent .on() calls fire immediately.
            capturedObj.FindChild(exitCodeKey).Var.Int = exitCode;
            capturedObj.FindChild(stdoutKey).Var.String = stdout;

            var.ReturnVar = procObj;
        }
    }
}
