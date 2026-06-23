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

namespace DScript.Vm
{
    /// <summary>
    /// Singleton well-known Symbol instances shared between the engine and the VM.
    /// These correspond to ES2015 well-known symbols such as Symbol.iterator.
    /// </summary>
    internal static class WellKnownSymbols
    {
        /// <summary>Symbol.iterator — method that returns the default iterator.</summary>
        public static readonly ScriptVar Iterator = ScriptVar.CreateSymbol("iterator");

        /// <summary>Symbol.hasInstance — determines instanceof behaviour.</summary>
        public static readonly ScriptVar HasInstance = ScriptVar.CreateSymbol("hasInstance");

        /// <summary>Symbol.toPrimitive — converts object to a primitive value.</summary>
        public static readonly ScriptVar ToPrimitive = ScriptVar.CreateSymbol("toPrimitive");

        /// <summary>Symbol.toStringTag — default description for Object.prototype.toString.</summary>
        public static readonly ScriptVar ToStringTag = ScriptVar.CreateSymbol("toStringTag");
    }
}
