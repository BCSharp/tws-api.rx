/* Copyright © 2014 Paweł A. Konieczny
 * See "LICENCE.txt" for details.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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


namespace IBApi.Reactive
{
    public class TwsClient : IDisposable
    {
        readonly int _clientId;
        readonly string _defaultHost;
        readonly int _defaultPort;
        readonly TwsListener _listener;
        readonly TwsSender _sender;

        enum State { Disconnected, Connected, Disposed }
        State _state;
        int _reqNum = 0;


        /// <summary>
        ///     Create an instance of TwsClient. Multiple instances of this class may be instanrtiated,
        ///     though TWS/Gateway allows only max. 10 simultaneous connections, so it is preferable
        ///     to instantiate only one TwsClient per process.
        /// </summary>
        /// <param name="clientId">
        ///     Unique ID of the connection. Must be unique across all connections to the same TWS/Gateway host,
        ///     across all processes. 
        /// </param>
        /// <param name="host">
        ///     Optional default DNS name or IP number of the host running TWS/Gateway.
        ///     Used if no host provided to <see cref="Connect"/> or <see cref="ConnectAsync"/>.
        /// </param>
        /// <param name="port">
        ///     Optional defaultport number opened by TWS/Gateway listening for API connections.
        ///     Used if no port number provided to <see cref="Connect"/> or <see cref="ConnectAsync"/>.
        /// </param>
        public TwsClient(int clientId, string host = "127.0.0.1", int port = 7496)
        {
            _clientId = clientId;
            _defaultHost = host;
            _defaultPort = port;
            _listener = new TwsListener();
            _sender = new TwsSender(_listener);
            _state = State.Disconnected;
        }


        /// <summary>
        ///     Close connection (of opened) and release resources.
        ///     This is a permissive Dispose, i.e. it is acceptable to dispose an already disposed instance.
        ///     Not thread-safe.
        /// </summary>
        public virtual void Dispose()
        {
            if (_state == State.Disposed) return;
            Disconnect();
            _state = State.Disposed;
            _listener.Dispose();
        }


        /// <summary>
        ///     Establish a connection to TWS/Gateway.
        /// </summary>
        /// <param name="host">
        ///     Optional override of host name specified in the constructor.
        /// </param>
        /// <param name="port">
        ///     Optional override of the port number specified in the constructor.
        /// </param>
        /// <param name="ct">
        ///     Optional canellation token. Note that cancellation is cooperative.
        /// </param>
        /// <returns>
        ///     Task that completes when the connection is established and the API client is ready to use,
        ///     or cancels on <paramref name="ct"/>.
        /// </returns>
        public async Task ConnectAsync(string host = null, int port = 0, CancellationToken ct = default(CancellationToken))
        {
            if (_state == State.Disposed) throw new ObjectDisposedException("TwsClient");
            if (_state == State.Connected) throw new InvalidOperationException("TwsClient already connected.");

            var connectionEstablished = _listener.OrderIdAvailable();
            // eConnect may block for long
            await Task.Run(() =>
                _sender.eConnect(host ?? _defaultHost, port > 0 ? port : _defaultPort, _clientId),
                ct
            );
            await connectionEstablished.ToTask(ct);
            _state = State.Connected;
        }


        /// <summary>
        ///     Synchronous version of <see cref="ConnectAsync"/>.
        /// </summary>
        /// <exception cref="AggregateException">
        ///     Exception(s) occured during making connection.
        /// </exception>
        public void Connect(string host = null, int port = 0, CancellationToken ct = default(CancellationToken))
        {
            ConnectAsync(host, port, ct).Wait(ct);
        }


        /// <summary>
        ///     Disconnect from TWS.
        /// </summary>
        /// <remarks>
        ///     The method returns immediately after the socket is closed, without waiting for TWS to tidy up.
        ///     Do not use if you intend to connect again, as TWS may not be ready yet to accept a new connection using the same client ID.
        /// </remarks>
        /// <seealso cref="DisconnectAsync"/>
        public void Disconnect()
        {
            if (_state == State.Disposed) throw new ObjectDisposedException("TwsClient");
            _sender.eDisconnect();
            _state = State.Disconnected;
        }

        /// <summary>
        ///     Disconnect from TWS.
        /// </summary>
        /// <remarks>
        ///     When the returned task completes, the client may connect again.
        /// </remarks>
        public async Task DisconnectAsync()
        {
            if (_state == State.Disconnected) return;
            if (_state == State.Disposed) throw new ObjectDisposedException("TwsClient");

            _sender.eDisconnect();
            // Give TWS some time to clean up. There is no information how much is needed, but experiments show 2s is enough.
            await Task.Delay(TimeSpan.FromSeconds(2));  
            _state = State.Disconnected;
        }


        /// <summary>
        ///     Hot observable of all errors, warnings, and info messages generated by TWS-API.
        ///     The observable stream never completes, but can become faulted if TWS-API generates exceptions.
        /// </summary>
        /// <remarks>
        ///     The exceptions from TWS-API indicate a case of usage errors, argument errors, internal errors (bugs),
        ///     or protocol errors on the socket line. Some of them are recoverable, but TwsClient treats all of them
        ///     as fatal: after an exeption, the faulted TwsClient instance should be disposed (this may change in the future).
        ///     Also, if there are multiple exceptions reported, they are not aggregated; only thie first one is reported here.
        /// </remarks>
        /// <seealso href="https://www.interactivebrokers.com/en/software/api/apiguide/tables/api_message_codes.htm"/>
        public IObservable<CodeMsgPair> Errors
        { get { return _listener.Errors.Select(pair => pair.Item2); } }


        /// <summary>
        ///     Create a cold observable of historical bar data.
        ///     Data downloaded upon subscription.
        /// </summary>
        /// <remarks>
        ///     Pacing logic is the responsibility of the subscriber(s).
        ///     This method is thread-safe.
        /// </remarks>
        /// <seealso href="https://www.interactivebrokers.com/en/software/api/apiguide/csharp/reqhistoricaldata.htm"/>
        public IObservable<Bar> RequestHistoricalData(Contract contract, DateTime endDateTime, string duration, string barSizeSetting, string whatToShow, bool useRTH)
        {
            if (_state == State.Disposed) throw new ObjectDisposedException("TwsClient");

            return Observable.Create<Bar>(obs =>
            {
                if (_state != State.Connected) 
                { 
                    obs.OnError(new InvalidOperationException("TwsClient not connected."));
                    return () => {};
                }

                int reqNum = Interlocked.Increment(ref _reqNum);

                var subs = _listener.GetHistoricalData(reqNum, barSizeSetting != "1 day").Subscribe(obs);
                _sender.reqHistoricalData(reqNum, contract, endDateTime.ToUniversalTime().ToString("yyyyMMdd HH\\:mm\\:ss UTC"), duration, barSizeSetting, whatToShow, useRTH? 1:0, 2, null);

                return () =>
                {
                    _sender.cancelHistoricalData(reqNum);
                    subs.Dispose();
                    _listener.DeleteHistoricalData(reqNum);
                };
            });
        }

        /// <summary>
        ///     The same as <see cref="RequestHistoricalData(Contract,DateTime,string,string,string,bool)"/> but using DateTimeOffset as <paramref name="enddateTime"/>.
        /// </summary>
        /// <remarks>
        ///     Pacing logic is the responsibility of the subscriber(s).
        ///     This method is thread-safe.
        /// </remarks>
        /// <seealso href="https://www.interactivebrokers.com/en/software/api/apiguide/csharp/reqhistoricaldata.htm"/>
        public IObservable<Bar> RequestHistoricalData(Contract contract, DateTimeOffset endDateTime, string duration, string barSizeSetting, string whatToShow, bool useRTH)
        {
            return RequestHistoricalData(contract, endDateTime.UtcDateTime, duration, barSizeSetting, whatToShow, useRTH);
        }


        /// <summary>
        ///     Request a snapshot of portfolio data for the given account.
        /// </summary>
        /// <param name="accountName">
        ///     The account name to access. Use null for default account.
        /// </param>
        /// <returns>
        ///     Cold observable of account data, position line and account time together.
        ///     The observable may be subscribed to multiple times but it will always yield the same data,
        ///     captured at the moment of the call to this method rather than at the moment of subscription.
        ///     Call this method again to receive a newer snapshot.
        /// </returns>
        /// <remarks>
        ///     TWS-API can handle only one request at a time so this method serializes requests asynchronously.
        /// </remarks>
        /// <seealso href="https://www.interactivebrokers.com/en/software/api/apiguide/csharp/reqaccountupdates.htm"/>
        public IObservable<AccountData> RequestPortfolioSnapshot(string accountName = null)
        {
            if (_state == State.Disposed) throw new ObjectDisposedException("TwsClient");
            if (_state != State.Connected) throw new InvalidOperationException("TwsClient not connected.");

            accountName = accountName ?? String.Empty;
            var ostm = _listener.GetPortfolioSnapshot(accountName, enable => _sender.reqAccountUpdates(enable, accountName));
            return ostm;
        }


        /// <summary>
        ///     This method provides an observable that does not complete by itself but is kept updated with any changes to the account.
        /// </summary>
        /// <remarks>
        ///     This method is thread-safe but because of the limitations of TWS-API, only one account can be serverd at a time.
        ///     Connecting to a subscription to another account will force the currently active one to complete.
        ///     This is not a problem for Individual Accounts, Financial Advisors with multiple accounts should prefer
        ///     other methods.
        /// </remarks>
        /// <param name="accountName">
        ///     The account name to access. Use null for default account.
        /// </param>
        /// <returns>
        ///     Dormant hot connectable observable, turns hot on Connect().
        /// </returns>
        public IConnectableObservable<AccountData> RequestPortfolioData(string accountName = null)
        {
            if (_state == State.Disposed) throw new ObjectDisposedException("TwsClient");

            accountName = accountName ?? String.Empty;
            return Observable.Create<AccountData>(obs =>
            {
                if (_state != State.Connected) 
                { 
                    obs.OnError(new InvalidOperationException("TwsClient not connected."));
                    return () => {};
                }

                object token;
                var subs = _listener.SubscribeToPortfolioData(obs, accountName, enable => _sender.reqAccountUpdates(enable, accountName), out token);

                return () =>
                {
                    subs.Dispose();
                    _listener.UnsubscribeFromPortfolioData(token);
                };
            }).Publish();
        }
    }
}
