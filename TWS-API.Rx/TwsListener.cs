/* Copyright © 2014 Paweł A. Konieczny
 * See "LICENCE.txt" for details.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.PlatformServices;
using System.Reactive.Joins;
using System.Reactive.Threading;
using System.Reactive.Threading.Tasks;
using System.Reactive.Disposables;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using IBApi;
using IBApi.Reactive;


namespace IBApi.Reactive
{
    class TwsListener : DefaultEWrapper, IDisposable
    {
        public void Dispose()
        {
            // Unfortunately, the way how the callback thread termination is currectly handled by TWS-API,
            // there is no way to know when the thread is really stopped, which is the precondition for disposal.
            // Hopefully, this will be fixed in future releases.
            // For now, we rely on Garbage Collector to pick up the disposables.
            // _orderIdReceived.Dispose();
        }


        #region Order ID Management

        /// Order IDs have to be unique within the scope of each client connection.
        /// Since one client connection can have multiple clients, running on different threads,
        /// this class centralizes order ID management. 

        int _orderId;
        readonly ManualResetEvent _orderIdReceived = new ManualResetEvent(initialState: false);


        /// <summary>
        ///     Make sure that a valid order ID is received from JTS. 
        ///     Thread-safe, but it is sufficient (and recommended) to call it only once.
        ///     Must be called before a call to <see cref="GetNewOrderId"/>.
        /// </summary>
        /// <returns>
        ///     A task that completes when an order ID is available
        ///     or cancels if no valid order ID received from TWS within the given timeout.
        ///     The latter contition usually means there is a connection problem.
        /// </returns>
        /// <param name="timeout">
        ///     Timeout for the task returned. Use <see cref="Timeout.InfiniteTimeSpan"/> to disable the timeout.
        /// </param>
        public Task OrderIdAvailable(TimeSpan timeout)
        {
            // First check whether we have already received an order ID; it is being sent right after connect
            if (_orderIdReceived.WaitOne(0))
            {
                var tcs = new TaskCompletionSource<Unit>();
                tcs.SetResult(Unit.Default);
                return tcs.Task;
            }

            // If not, wait for _orderIdReceived with a timeout
            return _orderIdReceived
                .AsTask(timeout)
                // Ambing with errors so that any exceptions from the wrapper will propagate to the task
                .ToObservable()
                .Amb(Errors.Where(_ => false).Select(_ => Unit.Default))
                .ToTask();
        }


        /// <summary>
        ///     EWrapper callback.
        ///     Only the first callback is effective, subsequent calls are ignored.
        /// </summary>
        public override void nextValidId(int orderId)
        {
            if (!_orderIdReceived.WaitOne(0)) // set it only once
            {
                _orderId = orderId;
                _orderIdReceived.Set();
            }
        }

        /// <summary>
        ///     Provide a new order ID in a thread-safe way. Order IDs are not reused.
        ///     Use after a successful call to <see cref="OrderIdAvailable"/>.
        /// </summary>
        /// <returns>
        ///     Available order ID to be used to submit a new order to the broker.
        /// </returns>
        public int GetNewOrderId()
        {
            // After OrderIdAvailable(), _orderId is guaranteed to be properly initialized.
            return Interlocked.Increment(ref _orderId) - 1; // -1 because Increment works like ++id and we want id++
        }

        #endregion

        #region Error Management

        /// <summary>
        ///     Hot observable stream of errors and warnings from TWS.
        /// </summary>
        /// <remarks>
        ///     Tuple elements are parameters of <see cref="Terror(int,int,string)"/>.
        ///     TODO: C# 6.0: Replace tuple by a lean immutable class.
        /// </remarks>
        public IObservable<Tuple<int, int, string>> Errors 
        { 
            get { return _twsErrorsSub.AsObservable(); } 
        }

        ISubject<Tuple<int, int, string>> _twsErrorsSub = new Subject<Tuple<int, int, string>>();

        /// <summary>
        ///     Error or warning received from TWS
        /// </summary>
        /// <param name="id">
        ///     Request ID associated with this error, if any.
        ///     -1 means no particular request ID is related to this error.
        /// </param>
        /// <param name="errorCode">
        ///     For codes <see href="https://www.interactivebrokers.com/en/software/api/api.htm"/>
        /// </param>
        /// <param name="errorMsg">
        ///     Descriptive error message.
        /// </param>
        public override void error(int id, int errorCode, string errorMsg)
        {
            _twsErrorsSub.OnNext(Tuple.Create(id, errorCode, errorMsg));
        }


        /// <summary>
        ///     An older version of error messages. Probably never used anymore.
        /// </summary>
        public override void error(string str)
        {
            _twsErrorsSub.OnNext(Tuple.Create(-1, 0, str));
        }


        /// <summary>
        ///     Fatal exception occurred in the TWS-API library, leaving it in a potentially unstable state.
        ///     Most likely a programming bug.
        ///     Reinitialization recommended.
        /// </summary>
        /// <remarks>
        ///     This error will set all observable streams into a faulted state, as no more data is guaranteed to be received.
        /// </remarks>
        public override void error(Exception ex)
        {
            _twsErrorsSub.OnError(ex);
        }

        #endregion
    }
}
