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
using System.Threading;

namespace DScript.Vm
{
    /// <summary>
    /// Implements the generator protocol using two semaphores to synchronise
    /// the caller thread with the generator body thread.
    /// </summary>
    internal sealed class GeneratorObject
    {
        private Thread _thread;
        private readonly SemaphoreSlim _callerReady = new(0, 1);
        private readonly SemaphoreSlim _generatorReady = new(0, 1);
        private ScriptVar _yieldedValue = new ScriptVar();
        private ScriptVar _resumeValue = new ScriptVar();
        private bool _done;
        private Exception _error;
        private bool _started;

        /// <summary>
        /// Called from within the generator body (VM Yield opcode handler).
        /// Suspends the generator thread and hands a value to the caller.
        /// Returns the value passed to the next .next() call.
        /// </summary>
        public ScriptVar Yield(ScriptVar value)
        {
            _yieldedValue = value;
            _generatorReady.Release();   // wake caller
            _callerReady.Wait();         // wait for next .next()
            return _resumeValue;
        }

        /// <summary>
        /// Called when the generator body finishes (falls off the end or explicit return).
        /// </summary>
        public void Complete(ScriptVar returnValue)
        {
            _yieldedValue = returnValue ?? new ScriptVar();
            _done = true;
        }

        /// <summary>
        /// Advance the generator. Called by the .next() method on the iterator object.
        /// </summary>
        public (ScriptVar value, bool done) Next(ScriptVar input, Action<GeneratorObject> startBody)
        {
            if (_done)
                return (new ScriptVar(), true);

            _resumeValue = input ?? new ScriptVar();

            if (!_started)
            {
                _started = true;
                _thread = new Thread(() =>
                {
                    try
                    {
                        startBody(this);
                    }
                    catch (Exception ex)
                    {
                        _error = ex;
                    }
                    finally
                    {
                        if (!_done) _done = true;
                        _generatorReady.Release();
                    }
                })
                { IsBackground = true };
                _thread.Start();
            }
            else
            {
                _callerReady.Release(); // wake generator
            }

            _generatorReady.Wait(); // wait for yield or completion

            if (_error != null)
            {
                var err = _error;
                _error = null;
                throw new ScriptException("Generator threw: " + err.Message);
            }

            return (_yieldedValue, _done);
        }
    }
}
