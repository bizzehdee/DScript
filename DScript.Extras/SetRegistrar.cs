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

using DScript.Extras.FunctionProviders;

namespace DScript.Extras
{
    internal static class SetRegistrar
    {
        internal static void Register(ScriptEngine engine)
        {
            var setCtorVar = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            setCtorVar.AddChild("iterable", new ScriptVar(ScriptVar.Flags.Undefined));
            setCtorVar.SetCallback((scope, _) =>
            {
                var setObj = new SetObject();
                var iterableArg = scope.FindChild("iterable")?.Var;
                if (iterableArg != null && !iterableArg.IsUndefined && iterableArg.IsArray)
                {
                    var len = iterableArg.GetArrayLength();
                    for (var i = 0; i < len; i++)
                        setObj.Data.Add(iterableArg.GetArrayIndex(i));
                }
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(setObj.ToScriptVar());
            }, null);

            engine.Root.AddChild("Set", setCtorVar);
        }
    }
}
