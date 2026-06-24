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
    /// Everything needed to abandon a speculative JIT run and re-execute the chunk on
    /// the interpreter. The speculative tier only compiles <b>pure</b> (call-free)
    /// functions, so no observable side effect can have occurred before a guard fails;
    /// re-running the whole chunk from its original arguments and environment is
    /// therefore safe and reproduces the correct result. No mid-stream operand-stack
    /// reconstruction is required.
    /// </summary>
    public readonly struct DeoptFrame
    {
        /// <summary>The chunk whose speculative compilation bailed out.</summary>
        public Chunk Chunk { get; }

        /// <summary>The arguments of the bailing invocation.</summary>
        public ScriptVar[] Args { get; }

        /// <summary>The call environment (parameters already bound) to re-run against.</summary>
        public Environment Env { get; }

        public DeoptFrame(Chunk chunk, ScriptVar[] args, Environment env)
        {
            Chunk = chunk;
            Args = args;
            Env = env;
        }
    }
}
