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
    /// Lightweight Promise implementation used by async/await.
    ///
    /// Promises have three states: Pending, Fulfilled, Rejected.
    /// Resolution/rejection schedules any registered callbacks via
    /// <see cref="MicroTaskQueue"/>. Callbacks registered after settlement
    /// are also scheduled immediately (as micro-tasks), matching the
    /// JavaScript specification.
    /// </summary>
    internal sealed class PromiseObject
    {
        public enum PromiseState { Pending, Fulfilled, Rejected }

        public PromiseState Status { get; private set; } = PromiseState.Pending;
        public ScriptVar Value { get; private set; }
        public ScriptVar Reason { get; private set; }

        private List<Action<ScriptVar>> _thenCallbacks;
        private List<Action<ScriptVar>> _catchCallbacks;

        public void Resolve(ScriptVar value)
        {
            if (Status != PromiseState.Pending) return;
            Status = PromiseState.Fulfilled;
            Value = value ?? new ScriptVar(ScriptVar.Flags.Undefined);
            if (_thenCallbacks != null)
            {
                var val = Value;
                foreach (var cb in _thenCallbacks)
                {
                    var captured = cb;
                    MicroTaskQueue.Enqueue(() => captured(val));
                }
                _thenCallbacks = null;
            }
        }

        public void Reject(ScriptVar reason)
        {
            if (Status != PromiseState.Pending) return;
            Status = PromiseState.Rejected;
            Reason = reason ?? new ScriptVar(ScriptVar.Flags.Undefined);
            if (_catchCallbacks != null)
            {
                var rsn = Reason;
                foreach (var cb in _catchCallbacks)
                {
                    var captured = cb;
                    MicroTaskQueue.Enqueue(() => captured(rsn));
                }
                _catchCallbacks = null;
            }
        }

        /// <summary>
        /// Attach fulfillment and optional rejection handlers. Returns a new
        /// Promise that resolves/rejects based on the return of the handlers
        /// (or propagates unhandled rejection/fulfillment through).
        /// </summary>
        public PromiseObject Then(Action<ScriptVar> onFulfilled, Action<ScriptVar> onRejected = null)
        {
            var next = new PromiseObject();

            Action<ScriptVar> fulfilled = v =>
            {
                try
                {
                    onFulfilled?.Invoke(v);
                    next.Resolve(v);
                }
                catch (Exception ex)
                {
                    next.Reject(new ScriptVar(ex.Message));
                }
            };

            Action<ScriptVar> rejected = r =>
            {
                if (onRejected != null)
                {
                    try
                    {
                        onRejected(r);
                        next.Resolve(r);
                    }
                    catch (Exception ex)
                    {
                        next.Reject(new ScriptVar(ex.Message));
                    }
                }
                else
                {
                    next.Reject(r);
                }
            };

            switch (Status)
            {
                case PromiseState.Fulfilled:
                    MicroTaskQueue.Enqueue(() => fulfilled(Value));
                    break;
                case PromiseState.Rejected:
                    MicroTaskQueue.Enqueue(() => rejected(Reason));
                    break;
                default:
                    _thenCallbacks ??= new List<Action<ScriptVar>>();
                    _catchCallbacks ??= new List<Action<ScriptVar>>();
                    _thenCallbacks.Add(fulfilled);
                    _catchCallbacks.Add(rejected);
                    break;
            }

            return next;
        }

        /// <summary>
        /// Attach a handler that runs on both fulfillment and rejection.
        /// The original value/reason is propagated regardless of the handler's return.
        /// If the handler throws, the returned Promise rejects with that error.
        /// </summary>
        public PromiseObject Finally(Action onFinally)
        {
            var next = new PromiseObject();

            Action<ScriptVar> fulfilled = v =>
            {
                try { onFinally?.Invoke(); next.Resolve(v); }
                catch (Exception ex) { next.Reject(new ScriptVar(ex.Message)); }
            };

            Action<ScriptVar> rejected = r =>
            {
                try { onFinally?.Invoke(); next.Reject(r); }
                catch (Exception ex) { next.Reject(new ScriptVar(ex.Message)); }
            };

            switch (Status)
            {
                case PromiseState.Fulfilled:
                    MicroTaskQueue.Enqueue(() => fulfilled(Value));
                    break;
                case PromiseState.Rejected:
                    MicroTaskQueue.Enqueue(() => rejected(Reason));
                    break;
                default:
                    _thenCallbacks ??= new List<Action<ScriptVar>>();
                    _catchCallbacks ??= new List<Action<ScriptVar>>();
                    _thenCallbacks.Add(fulfilled);
                    _catchCallbacks.Add(rejected);
                    break;
            }

            return next;
        }

        /// <summary>
        /// Convenience: create an already-fulfilled Promise.
        /// </summary>
        public static PromiseObject Resolved(ScriptVar value)
        {
            var p = new PromiseObject();
            p.Resolve(value);
            return p;
        }

        /// <summary>
        /// Convenience: create an already-rejected Promise.
        /// </summary>
        public static PromiseObject Rejected(ScriptVar reason)
        {
            var p = new PromiseObject();
            p.Reject(reason);
            return p;
        }

        /// <summary>
        /// Wrap a ScriptVar in a PromiseObject. If it already IS a promise
        /// (stored as object data), return it directly; otherwise wrap the
        /// value in a resolved promise.
        /// </summary>
        public static PromiseObject Wrap(ScriptVar value)
        {
            if (value != null && value.IsObject)
            {
                var data = value.GetData();
                if (data is PromiseObject existing)
                    return existing;
            }
            return Resolved(value ?? new ScriptVar(ScriptVar.Flags.Undefined));
        }

        /// <summary>
        /// Build a ScriptVar that wraps this PromiseObject and exposes
        /// <c>.then(callback)</c> and <c>.catch(callback)</c> as callable methods.
        /// Also stores this object as the ScriptVar's data for later extraction.
        /// </summary>
        public ScriptVar ToScriptVar(VirtualMachine vm)
        {
            var obj = new ScriptVar(ScriptVar.Flags.Object);
            obj.SetData(this);

            // .then(onFulfilled)
            var thenFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            thenFn.AddChild("onFulfilled", new ScriptVar(ScriptVar.Flags.Undefined));
            var self = this;
            thenFn.SetCallback((scope, _) =>
            {
                var cb = scope.FindChild("onFulfilled")?.Var;
                PromiseObject nextPromise = null;
                nextPromise = self.Then(v =>
                {
                    if (cb != null && cb.IsFunction)
                        vm.InvokeCallable(cb, null, [v]);
                });
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(nextPromise.ToScriptVar(vm));
            }, null);
            obj.AddChild("then", thenFn);

            // .catch(onRejected)
            var catchFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            catchFn.AddChild("onRejected", new ScriptVar(ScriptVar.Flags.Undefined));
            catchFn.SetCallback((scope, _) =>
            {
                var cb = scope.FindChild("onRejected")?.Var;
                PromiseObject nextPromise = null;
                nextPromise = self.Then(null, r =>
                {
                    if (cb != null && cb.IsFunction)
                        vm.InvokeCallable(cb, null, [r]);
                });
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(nextPromise.ToScriptVar(vm));
            }, null);
            obj.AddChild("catch", catchFn);

            // .finally(onFinally)
            var finallyFn = new ScriptVar(ScriptVar.Flags.Function | ScriptVar.Flags.Native);
            finallyFn.AddChild("onFinally", new ScriptVar(ScriptVar.Flags.Undefined));
            finallyFn.SetCallback((scope, _) =>
            {
                var cb = scope.FindChild("onFinally")?.Var;
                var nextPromise = self.Finally(() =>
                {
                    if (cb != null && cb.IsFunction)
                        vm.InvokeCallable(cb, null, []);
                });
                scope.FindChildOrCreate(ScriptVar.ReturnVarName).ReplaceWith(nextPromise.ToScriptVar(vm));
            }, null);
            obj.AddChild("finally", finallyFn);

            return obj;
        }
    }
}
