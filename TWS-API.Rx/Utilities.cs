/* Copyright © 2014 Paweł A. Konieczny
 * See "LICENCE.txt" for details.
 */
using System;
using System.Reactive;
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
    }
}
