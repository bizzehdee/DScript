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

using System.Collections.Generic;
using DScript.Extras.FunctionProviders;

namespace DScript.Extras.Registrars
{
    internal static class MapRegistrar
    {
        internal static void Register(ScriptEngine engine)
        {
            var mapCtorVar = ScriptVar.CreateNativeFunction();
            mapCtorVar.AddChild("iterable", ScriptVar.CreateUndefined());
            mapCtorVar.SetCallback((scope, _) =>
            {
                var mapObj = new MapObject();
                var iterableArg = scope.FindChild("iterable")?.Var;
                if (iterableArg != null && !iterableArg.IsUndefined && iterableArg.IsArray)
                {
                    var len = iterableArg.GetArrayLength();
                    for (var i = 0; i < len; i++)
                    {
                        var pair = iterableArg.GetArrayIndex(i);
                        var key = pair.GetArrayIndex(0);
                        var val = pair.GetArrayIndex(1);
                        mapObj.Data[key] = val.DeepCopy();
                    }
                }
                // Store the MapObject onto `this` so that Map methods (get, set, …)
                // can access it via thisVar.GetData().  The Construct opcode creates
                // the instance and passes it as `this`; by mutating `this` rather
                // than replacing ReturnVar we preserve the prototype link
                // (instance.__proto__ = mapCtorVar) that allows method lookup.
                var thisVar = scope.FindChild("this")?.Var;
                thisVar?.SetData(mapObj);
            }, null);

            engine.Root.AddChild("Map", mapCtorVar);
        }
    }
}
