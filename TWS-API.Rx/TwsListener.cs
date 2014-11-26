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
using System.Collections.Concurrent;
using System.Globalization;


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


        #region Connectionn and Order ID Management

        /// Order IDs have to be unique within the scope of each client connection.
        /// Since one client connection can have multiple clients, running on different threads,
        /// this class centralizes order ID management. 

        int _orderId;
        ISubject<int> _orderIdSubject;


        /// <summary>
        ///     Make sure that a valid order ID is received from JTS. 
        ///     Must be called before connection requested and awaited before a call to <see cref="GetNewOrderId"/>.
        /// </summary>
        /// <returns>
        ///     An observable stream that completes when an order ID is available.
        ///     The stream value is the next valid order ID as reported by TWS.
        ///     Note that this value is useful for debugging purposes only.
        ///     For new orders, use <see cref="GetNewOrderId"/>.
        /// </returns>
        public IObservable<int> OrderIdAvailable()
        {
            var sub = new AsyncSubject<int>();
            _orderIdSubject = sub;

            return sub.MergeErrors(
                Errors.Where(err => err.Item2.IsError())
                      .Select(err => new ApplicationException(err.Item2.Message))
                      .AsError()
            );
        }


        /// <summary>
        ///     EWrapper callback.
        ///     Only the first callback is effectively setting the ID.
        /// </summary>
        public override void nextValidId(int orderId)
        {
            // Set it only once
            Interlocked.CompareExchange(ref _orderId, orderId, 0);

            // Push through Rx network if possible
            var sub = _orderIdSubject;
            if (sub != null)
            {
                sub.OnNext(orderId);
                sub.OnCompleted();
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
            // After OrderIdAvailable() completes, _orderId is guaranteed to be properly initialized.
            return Interlocked.Increment(ref _orderId) - 1; // -1 because Increment works like ++id and we want id++
        }

        /// <summary>
        ///     EWrapper callback.
        ///     Delete infromation whether order ID sent after connect is received.
        /// </summary>
        public override void connectionClosed()
        {
            _orderIdSubject = null;
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
        public IObservable<Tuple<int, CodeMsgPair>> Errors 
        { 
            get { return _twsErrorsSub.AsObservable(); } 
        }

        ISubject<Tuple<int, CodeMsgPair>> _twsErrorsSub = new Subject<Tuple<int, CodeMsgPair>>();

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
            _twsErrorsSub.OnNext(Tuple.Create(id, new CodeMsgPair(errorCode, errorMsg)));
        }


        /// <summary>
        ///     An older version of error messages. Probably never used anymore.
        /// </summary>
        public override void error(string str)
        {
            _twsErrorsSub.OnNext(Tuple.Create(-1, new CodeMsgPair(0, str)));
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


        #region Historical Data

        ConcurrentDictionary<int, Tuple<ISubject<Bar>, bool>> _historicalDataDict = new ConcurrentDictionary<int, Tuple<ISubject<Bar>, bool>>();

        public IObservable<Bar> GetHistoricalData(int reqId, bool intraday)
        {
            ISubject<Bar> historical_data = new Subject<Bar>();
            _historicalDataDict.TryAdd(reqId, Tuple.Create(historical_data, intraday));  // always suceeds as reqId is unique

            return historical_data.MergeErrors(
                Errors.Where(err => err.Item1 == reqId)
                      .Select(err => new ApplicationException(err.Item2.Message))
                      .AsError()
            );
        }

        public void DeleteHistoricalData(int reqId)
        {
            Tuple<ISubject<Bar>, bool> historical_data;
            _historicalDataDict.TryRemove(reqId, out historical_data);
        }

        public override void historicalData(int reqId, string date, double open, double high, double low, double close, int volume, int count, double WAP, bool hasGaps)
        {
            Tuple<ISubject<Bar>, bool> historical_data;
            if (_historicalDataDict.TryGetValue(reqId, out historical_data))
            {
                DateTime timestamp;
                if (historical_data.Item2) // intraday
                {
                    long epochtime = long.Parse(date); // the "date" string is in epoch seconds
                    timestamp = epoch.AddSeconds(epochtime);
                }
                else
                {
                    timestamp = DateTime.ParseExact(date, "yyyyMMdd", DateTimeFormatInfo.InvariantInfo);
                }
                historical_data.Item1.OnNext(new Bar(timestamp.Ticks, (decimal)open + Zero00, (decimal)high + Zero00, (decimal)low + Zero00, (decimal)close + Zero00, volume, (decimal)WAP));
            }
        }

        public override void historicalDataEnd(int reqId, string start, string end)
        {
            Tuple<ISubject<Bar>, bool> historical_data;
            if (_historicalDataDict.TryGetValue(reqId, out historical_data))
            {
                historical_data.Item1.OnCompleted();
            }
        }

        #endregion


        #region Account Updates

        /*
         * Account updates from TWS API have a number of limitations.
         * The major one is that there can be only one update request active at any time.
         * To work around that limitation we will queue simultaneous requests for different accounts
         * until the current one completes.
         */
        ISubject<AccountData> _portfolioSub;
        Action<bool> _enableAccountUpdates;
        string _accountName;
        object _accountUpdatesGate = new object();
        Queue<Tuple<ISubject<AccountData>, string,  Action<bool>>> _accountUpdatesQueue = new Queue<Tuple<ISubject<AccountData>, string, Action<bool>>>();

        public IObservable<AccountData> GetPortfolioSnapshot(string accountName, Action<bool> enable)
        {
            lock (_accountUpdatesGate)
            {
                if (_accountName == null) // no update in progress, start a new one
                {
                    _portfolioSub = new ReplaySubject<AccountData>();
                    _accountName = accountName;
                    _enableAccountUpdates = enable;
                    enable(true); // start
                    return _portfolioSub.MergeErrors(Errors);
                }
                else if (_accountName == accountName) // reuse the one in progress
                {
                    return _portfolioSub.MergeErrors(Errors);
                }
                else // queue request
                {
                    ISubject<AccountData> portfolioSub = new ReplaySubject<AccountData>();
                    _accountUpdatesQueue.Enqueue(Tuple.Create(portfolioSub, accountName, enable));
                    return portfolioSub.MergeErrors(Errors);
                }
            }
        }

        public override void updatePortfolio(Contract contract, int position, double marketPrice, double marketValue,
                                    double averageCost, double unrealizedPNL, double realizedPNL, string accountName)
        {
            try
            {
                var posLine = new PositionLine
                (
                    accountName,
                    contract.SecType,
                    contract.Symbol,
                    contract.Expiry,
                    position,
                    (decimal)marketPrice + Zero00,
                    (decimal)marketValue + Zero00,
                    (decimal)averageCost + Zero00,
                    (decimal)unrealizedPNL + Zero00,
                    (decimal)realizedPNL + Zero00
                );

                var sub = _portfolioSub;
                if (sub != null) sub.OnNext(new AccountData(posLine));
            }
            catch (Exception ex)
            {
                _twsErrorsSub.OnError(ex);
            }
        }


        public override void updateAccountValue(string key, string val, string currency, string accountName)
        {
            var sub = _portfolioSub;
            if (sub != null) sub.OnNext(new AccountData(accountName, key, val, currency));
        }


        public override void updateAccountTime(string timeStamp)
        {
            // timeStamp is in format "HH:mm" (or "H:mm"?) local time.
            var sub = _portfolioSub;
            if (sub != null) sub.OnNext(new AccountData(_accountName, "AccountTime", timeStamp, "hrs"));
        }


        public override void accountDownloadEnd(string accountName)
        {
            lock (_accountUpdatesGate)
            {
                if (_portfolioSub != null)
                {
                    _portfolioSub.OnCompleted();
                    _portfolioSub = null;
                }
                if (_enableAccountUpdates != null)
                {
                    _enableAccountUpdates(false);
                    _enableAccountUpdates = null;
                }
                _accountName = null;

                if (_accountUpdatesQueue.Count > 0)
                {
                    var next_request = _accountUpdatesQueue.Dequeue();
                    _portfolioSub = next_request.Item1;
                    _accountName = next_request.Item2;
                    _enableAccountUpdates = next_request.Item3;
                    _enableAccountUpdates(true);
                }
            }
        }



        #endregion


        readonly DateTime epoch = DateTime.Parse("1970-01-01T00:00:00Z").ToUniversalTime();
        readonly decimal Zero00 = 0.00m;
    }
}
