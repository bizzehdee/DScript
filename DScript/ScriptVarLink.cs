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

namespace DScript
{
    public sealed class ScriptVarLink : IDisposable
    {
        #region IDisposable
        private bool disposed;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposed) return;

            if (disposing)
            {
                Var.UnRef();
            }

            // Indicate that the instance has been disposed.
            disposed = true;
        }
        #endregion

        // ── Thread-local link pool ────────────────────────────────────────────
        // Links are small (~80 bytes) but created and discarded at ~3–5 per
        // native call.  A ThreadStatic pool eliminates allocations on the hot
        // path without any locking.
        [System.ThreadStatic]
        private static ScriptVarLink[] _pool;
        [System.ThreadStatic]
        private static int _poolCount;
        private const int PoolMaxSize = 128;

        // Private no-arg constructor used only by Rent() when the pool is cold.
        private ScriptVarLink() { }

        /// <summary>
        /// Rents a ScriptVarLink from the thread-local pool (or allocates a new
        /// one when the pool is empty) and initialises all fields for fresh use.
        /// </summary>
        internal static ScriptVarLink Rent(ScriptVar var, string name, bool readOnly = false)
        {
            ScriptVarLink link;
            if (_poolCount > 0)
            {
                link = _pool[--_poolCount];
                _pool[_poolCount] = null; // clear slot so the GC can collect the object if pool is not retained
            }
            else
            {
                link = new ScriptVarLink();
            }
            link.Name = name;
            link.Var = var.Ref();
            link.IsConst = readOnly;
            link.Owned = false;
            link.Next = null;
            link.Prev = null;
            link.Owner = null;
            link.Enumerable = true;
            link.Configurable = true;
            link.Writable = true;
            link._getter = null;
            link._setter = null;
            link.disposed = false;
            return link;
        }

        /// <summary>
        /// Returns a link to the thread-local pool.  The caller is responsible
        /// for having already handled Var lifetime (Dispose() for the explicit
        /// disposal path; nothing for the ResetForReuse detach-without-UnRef path).
        /// This method nulls Var and the navigation fields to release GC references.
        /// </summary>
        internal static void Return(ScriptVarLink link)
        {
            _pool ??= new ScriptVarLink[PoolMaxSize];
            if (_poolCount >= PoolMaxSize) return; // drop to GC rather than growing pool
            link.Var = null;
            link.Owner = null;
            link.Next = null;
            link.Prev = null;
            link._getter = null;
            link._setter = null;
            _pool[_poolCount++] = link;
        }

        public string Name { get; private set; }
        public ScriptVarLink Next { get; internal set; }
        public ScriptVarLink Prev { get; internal set; }
        public ScriptVar Var { get; internal set; }
        public bool Owned { get; internal set; }
        public bool IsConst { get; private set; }

        // Property descriptor fields (ES5 Object.defineProperty semantics)
        public bool Enumerable { get; set; } = true;
        public bool Configurable { get; set; } = true;
        public bool Writable { get; set; } = true;

        private ScriptVar _getter;
        private ScriptVar _setter;

        /// <summary>Getter function for this accessor property. Setting it invalidates the owner's property cache.</summary>
        public ScriptVar Getter
        {
            get => _getter;
            set { _getter = value; Owner?.BumpShapeVersionAndInvalidateShape(); }
        }

        /// <summary>Setter function for this accessor property. Setting it invalidates the owner's property cache.</summary>
        public ScriptVar Setter
        {
            get => _setter;
            set { _setter = value; Owner?.BumpShapeVersionAndInvalidateShape(); }
        }

        /// <summary>True when this link has a get or set accessor (not a plain data property).</summary>
        public bool IsAccessor => _getter != null || _setter != null;

        /// <summary>The ScriptVar this link is a child of (set when added).</summary>
        internal ScriptVar Owner { get; set; }

        public ScriptVarLink(ScriptVar var, string name, bool readOnly = false)
        {
            Name = name;
            Var = var.Ref();
            Next = null;
            Prev = null;
            Owned = false;
            IsConst = readOnly;
        }

        public ScriptVarLink(ScriptVarLink toCopy)
        {
            Name = toCopy.Name;
            Var = toCopy.Var.Ref();
            Next = toCopy.Next;
            Prev = toCopy.Prev;
            Owned = toCopy.Owned;
            _getter = toCopy._getter;
            _setter = toCopy._setter;
            Enumerable = toCopy.Enumerable;
            Configurable = toCopy.Configurable;
            Writable = toCopy.Writable;
        }

        public void ReplaceWith(ScriptVar newVar)
        {
            if(IsConst && Var?.IsUndefined == false)
            {
                throw new JITException($"{Name} is const, cannot assign a new value");
            }

            var oldVar = Var;
            Var = newVar.Ref();
            oldVar.UnRef();
        }

        public void ReplaceWith(ScriptVarLink newVar)
        {

            ReplaceWith(newVar != null ? newVar.Var : ScriptVar.CreateUndefined());
        }

        public int GetIntName()
        {
            var retVal = Convert.ToInt32(Name);

            return retVal;
        }

        public void SetIntName(int n)
        {
            // renaming changes the owner's lookup key, so invalidate its index
            Owner?.InvalidateChildIndex();
            Name = $"{n}";
        }


        public override string ToString()
        {
            return $"{Name ?? "Unnamed"} = {Var?.String ?? "null"}";
        }
    }
}
