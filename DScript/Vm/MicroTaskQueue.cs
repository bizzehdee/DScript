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
using System.Collections.Generic;

namespace DScript.Vm
{
    /// <summary>
    /// Simple synchronous micro-task queue. Tasks are enqueued when a Promise
    /// resolves or rejects and drained by calling <see cref="DrainAll"/>.
    /// </summary>
    internal static class MicroTaskQueue
    {
        private static readonly Queue<Action> _queue = new();

        public static void Enqueue(Action task) => _queue.Enqueue(task);

        /// <summary>
        /// Run all queued micro-tasks. New tasks that are enqueued during
        /// draining are also run, matching the JavaScript micro-task behaviour.
        /// </summary>
        public static void DrainAll()
        {
            while (_queue.Count > 0)
            {
                var task = _queue.Dequeue();
                task();
            }
        }

        /// <summary>
        /// Return the number of pending micro-tasks (useful for tests).
        /// </summary>
        public static int Count => _queue.Count;
    }
}
