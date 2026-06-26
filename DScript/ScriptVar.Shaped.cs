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

using DScript.Vm;

namespace DScript
{
    /// <summary>
    /// A <see cref="ScriptVar"/> that carries hidden-class (shape) state.
    ///
    /// Only JS-defined class instances (and any future shape-tracked object kind)
    /// are created as <see cref="ShapedScriptVar"/>; every other ScriptVar — the
    /// millions of short-lived primitives, scopes, arrays, closures and call frames
    /// the VM allocates — is a plain <see cref="ScriptVar"/> and therefore does not
    /// pay the 16 bytes these two reference fields cost.
    ///
    /// The VM and <see cref="PropCacheCell"/> reach the shape path with a single
    /// <c>obj as ShapedScriptVar</c> test; <see cref="ScriptVar"/>'s own mutators
    /// gate on the <see cref="ScriptVar.Flags.ShapeTracked"/> flag, which is set
    /// only on instances of this type, so the cast back to
    /// <see cref="ShapedScriptVar"/> is always valid.
    /// </summary>
    internal sealed class ShapedScriptVar : ScriptVar
    {
        // Hidden class / shape for this object. Non-null once the first user-visible
        // property has been added without getters/setters/deletions. Walking
        // <see cref="_shapeRoot"/> by shape.Slots[name] steps gives the link for that
        // name, enabling O(1) reads keyed on (shape.Id, name) instead of FindChild.
        // The child linked list remains the source of truth; these are an
        // acceleration layer only.
        //
        // _shapeRoot is the FIRST shape-tracked link added to this object
        // (non-shape-tracked links like "prototype" may precede it in the linked list).
        // Walking SlotIndex steps from _shapeRoot gives the link at that slot without
        // allocating any per-instance array.
        internal Shape _shape;
        internal ScriptVarLink _shapeRoot;

        internal ShapedScriptVar() : base(Flags.Object | Flags.ShapeTracked)
        {
        }
    }
}
