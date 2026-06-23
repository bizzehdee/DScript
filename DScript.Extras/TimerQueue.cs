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
using System.Runtime.CompilerServices;

namespace DScript.Extras
{
    /// <summary>
    /// Per-engine timer queue holding pending setTimeout / setInterval entries.
    /// Stored in a ConditionalWeakTable so it doesn't extend engine lifetime.
    /// </summary>
    public sealed class TimerQueue
    {
        private static readonly ConditionalWeakTable<ScriptEngine, TimerQueue> _queues = new();

        public static TimerQueue GetOrCreate(ScriptEngine engine)
            => _queues.GetOrCreateValue(engine);

        private int _nextId = 1;
        private readonly List<TimerEntry> _entries = new();

        private sealed class TimerEntry
        {
            public int Id { get; init; }
            public long DueMs { get; set; }
            public long IntervalMs { get; init; }
            public ScriptVar Fn { get; init; }
            public bool Cancelled { get; set; }
        }

        /// <summary>
        /// Schedule a one-shot timer. Returns the handle id.
        /// </summary>
        public int SetTimeout(ScriptVar fn, long delayMs, long nowMs)
        {
            var id = _nextId++;
            _entries.Add(new TimerEntry { Id = id, DueMs = nowMs + delayMs, IntervalMs = 0, Fn = fn.DeepCopy() });
            return id;
        }

        /// <summary>
        /// Schedule a repeating timer. Returns the handle id.
        /// </summary>
        public int SetInterval(ScriptVar fn, long intervalMs, long nowMs)
        {
            var id = _nextId++;
            _entries.Add(new TimerEntry { Id = id, DueMs = nowMs + intervalMs, IntervalMs = intervalMs, Fn = fn.DeepCopy() });
            return id;
        }

        /// <summary>
        /// Cancel a timer by id (covers both setTimeout and setInterval).
        /// </summary>
        public void Clear(int id)
        {
            foreach (var e in _entries)
                if (e.Id == id) e.Cancelled = true;
        }

        /// <summary>
        /// Fire all callbacks whose DueMs has passed. Repeating timers are rescheduled.
        /// </summary>
        public void Drain(ScriptEngine engine, long nowMs)
        {
            var toFire = new List<TimerEntry>();
            foreach (var e in _entries)
            {
                if (!e.Cancelled && e.DueMs <= nowMs)
                    toFire.Add(e);
            }
            foreach (var e in toFire)
            {
                _entries.Remove(e);
                if (e.Cancelled) continue;
                try { engine.CallFunction(e.Fn, null); }
                catch (Exception) { /* swallow timer callback exceptions */ }
                if (e.IntervalMs > 0 && !e.Cancelled)
                {
                    e.DueMs = nowMs + e.IntervalMs;
                    _entries.Add(e);
                }
            }
            // Prune cancelled one-shot entries
            _entries.RemoveAll(e => e.Cancelled && e.IntervalMs == 0);
        }

        /// <summary>
        /// Convenience: drain using the current UTC time.
        /// </summary>
        public void Drain(ScriptEngine engine)
            => Drain(engine, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        public int PendingCount => _entries.Count;
    }
}
