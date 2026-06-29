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

namespace DScript
{
    /// <summary>
    /// Implemented by deferred (lazily-evaluated) arrays produced by the array
    /// pipeline fusion optimisation (filter → map). When any operation other than
    /// a recognised fusion consumer (reduce, forEach, another map/filter) observes
    /// the array — length query, indexed read, for-in/for-of, mutation — the array
    /// is materialised in place by calling <see cref="Materialize"/>.
    /// </summary>
    public interface IFusedArray
    {
        /// <summary>
        /// Evaluate the deferred chain and populate <paramref name="target"/> with
        /// the resulting elements. Implementations must call
        /// <c>target.SetData(null)</c> as their first action to clear the chain
        /// reference and prevent re-entrant materialisation.
        /// </summary>
        void Materialize(ScriptVar target);
    }
}
