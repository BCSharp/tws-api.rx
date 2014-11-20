﻿/* Copyright © 2014 Paweł A. Konieczny
 * See "LICENCE.txt" for details.
 */
using System;
using System.Reactive;
using System.Reactive.PlatformServices;
using System.Reactive.Joins;
using System.Reactive.Threading;
using System.Reactive.Threading.Tasks;
using System.Reactive.Disposables;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace IBApi.Reactive
{
    public static class Utilities
    {
        public static Task AsTask(this WaitHandle handle)
        {
            return AsTask(handle, Timeout.InfiniteTimeSpan);
        }

        public static Task AsTask(this WaitHandle handle, TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<Unit>();
            RegisteredWaitHandle registration = ThreadPool.RegisterWaitForSingleObject(handle, (state, timedOut) =>
            {
                var local_tcs = (TaskCompletionSource<Unit>)state;
                if (timedOut)
                    local_tcs.TrySetCanceled();
                else
                    local_tcs.TrySetResult(Unit.Default);
            }, state: tcs, timeout: timeout, executeOnlyOnce: true);
            tcs.Task.ContinueWith((_, state) => ((RegisteredWaitHandle)state).Unregister(null), registration, TaskScheduler.Default);
            return tcs.Task;
        }

        // Not thread-safe, both observables have to be synchronized.
        // Fortunately, this is the case n the usage within this library
        public static IObservable<T> MergeErrors<T,U>(this IObservable<T> lhs, IObservable<U> rhs)
        {
            return Observable.Create<T>(obs =>
            {
                bool completed = false;

                return new CompositeDisposable(
                    lhs.Subscribe(
                        item => { if (!completed) obs.OnNext(item); },
                        ex => { if (!completed) obs.OnError(ex); completed = true; },
                        () => { if (!completed) obs.OnCompleted(); completed = true; }
                    ),
                    rhs.Subscribe(
                        _ => {},
                        ex => { if (!completed) obs.OnError(ex); completed = true; }
                    )
                );
            });
        }

    }
}
