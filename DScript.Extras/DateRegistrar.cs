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
using System.Globalization;
using DScript.Extras.FunctionProviders;

namespace DScript.Extras
{
    internal static class DateRegistrar
    {
        internal static void Register(ScriptEngine engine)
        {
            // Date constructor: new Date(), new Date(ms), new Date(str),
            //                   new Date(y, m, d, h?, min?, s?, ms?)
            var dateCtorVar = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            dateCtorVar.AddChild("arg0", new ScriptVar(ScriptVar.Flags.Undefined));
            dateCtorVar.AddChild("arg1", new ScriptVar(ScriptVar.Flags.Undefined));
            dateCtorVar.AddChild("arg2", new ScriptVar(ScriptVar.Flags.Undefined));
            dateCtorVar.AddChild("arg3", new ScriptVar(ScriptVar.Flags.Undefined));
            dateCtorVar.AddChild("arg4", new ScriptVar(ScriptVar.Flags.Undefined));
            dateCtorVar.AddChild("arg5", new ScriptVar(ScriptVar.Flags.Undefined));
            dateCtorVar.AddChild("arg6", new ScriptVar(ScriptVar.Flags.Undefined));
            dateCtorVar.SetCallback((scope, _) =>
            {
                var a0 = scope.FindChild("arg0")?.Var;
                DateTimeOffset dto;
                if (a0 == null || a0.IsUndefined)
                {
                    dto = DateTimeOffset.UtcNow;
                }
                else if (a0.IsInt || a0.IsDouble)
                {
                    var a1 = scope.FindChild("arg1")?.Var;
                    if (a1 != null && !a1.IsUndefined)
                    {
                        // Component form: (y, m, d, h?, min?, s?, ms?)
                        var a2 = scope.FindChild("arg2")?.Var;
                        var a3 = scope.FindChild("arg3")?.Var;
                        var a4 = scope.FindChild("arg4")?.Var;
                        var a5 = scope.FindChild("arg5")?.Var;
                        var a6 = scope.FindChild("arg6")?.Var;
                        dto = new DateTimeOffset(new DateTime(
                            a0.Int,
                            a1.Int + 1,
                            a2 != null && !a2.IsUndefined ? a2.Int : 1,
                            a3 != null && !a3.IsUndefined ? a3.Int : 0,
                            a4 != null && !a4.IsUndefined ? a4.Int : 0,
                            a5 != null && !a5.IsUndefined ? a5.Int : 0,
                            a6 != null && !a6.IsUndefined ? a6.Int : 0
                        ));
                    }
                    else
                    {
                        // Milliseconds from epoch
                        dto = DateTimeOffset.FromUnixTimeMilliseconds((long)a0.Float);
                    }
                }
                else
                {
                    // String form
                    if (DateTimeOffset.TryParse(a0.String, CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var parsed))
                        dto = parsed;
                    else
                        dto = DateTimeOffset.UtcNow;
                }

                var dateObj = new DateObject(dto);
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(dateObj.ToScriptVar());
            }, null);

            // Date.now() static method
            var dateNowFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            dateNowFn.SetCallback((scope, _) =>
            {
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(
                    new ScriptVar(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            }, null);
            dateCtorVar.AddChild("now", dateNowFn);

            engine.Root.AddChild("Date", dateCtorVar);
        }
    }
}
