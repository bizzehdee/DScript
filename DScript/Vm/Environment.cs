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
    /// A lexical scope: a set of variable bindings (held as children of a
    /// <see cref="ScriptVar"/>) plus a link to the enclosing scope. Variable
    /// resolution walks the <see cref="Parent"/> chain to the global scope,
    /// giving true lexical scoping — a function closes over the environment it
    /// was defined in, not the call-time stack.
    /// </summary>
    public sealed class Environment
    {
        /// <summary>Bindings declared directly in this scope.</summary>
        public ScriptVar Vars { get; }

        /// <summary>Enclosing lexical scope, or null for the global scope.</summary>
        public Environment Parent { get; }

        /// <summary>
        /// Bumped whenever a new binding is added to this scope. The inline cache
        /// (see <see cref="Chunk.InlineCacheEntry"/>) records the version it
        /// resolved against and re-resolves if it changes, so a freshly declared
        /// variable that shadows an outer one is never served stale from the cache.
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// True for synthetic scopes created by <see cref="OpCode.EnterBlock"/>.
        /// <c>var</c> declarations (as opposed to <c>let</c>/<c>const</c>) skip
        /// block scopes and hoist into the nearest enclosing non-block environment,
        /// preserving JavaScript's function-scoped <c>var</c> semantics.
        /// </summary>
        public bool IsBlockScope { get; }

        public Environment(ScriptVar vars, Environment parent, bool isBlockScope = false)
        {
            Vars = vars;
            Parent = parent;
            IsBlockScope = isBlockScope;
        }

        /// <summary>Find the binding for <paramref name="name"/>, or null.</summary>
        public ScriptVarLink Resolve(string name)
        {
            for (var env = this; env != null; env = env.Parent)
            {
                var link = env.Vars.FindChild(name);
                if (link != null) return link;
            }

            return null;
        }

        private Environment cachedGlobal;

        /// <summary>The outermost (global) environment.</summary>
        public Environment Global()
        {
            if (Parent == null) return this;
            return cachedGlobal ??= Parent.Global();
        }
    }
}
