/* Copyright © 2014 Paweł A. Konieczny
 * See "LICENCE.txt" for details.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        /// <param name="timeout">
        ///     Timeout for establishing the connection in miliseconds.
        ///     Use <see cref="Timeout.Inifinite"/> to disable the timeout.
        /// </param>
        /// <returns>
        ///     Task that completes when the connection is established and the API client is ready to use,
        ///     or cancels on a timeout.
        /// </returns>
        public async Task ConnectAsync(string host = null, int port = 0, int timeout = 10000)
        {
            if (_state == State.Disposed) throw new ObjectDisposedException("TwsClient");
            if (_state == State.Connected) throw new InvalidOperationException("TwsClient already connected.");

            _sender.eConnect(host ?? _defaultHost, port > 0? port : _defaultPort, _clientId);
            await _listener.OrderIdAvailable(TimeSpan.FromMilliseconds(timeout));
            _state = State.Connected;
        }


        /// <summary>
        ///     Synchronous version of <see cref="ConnectAsync"/>.
        /// </summary>
        /// <exception cref="TimeoutException">
        ///     Connection not established within the given timeout.
        /// </exception>
        public void Connect(string host = null, int port = 0, int timeout = 10000)
        {
            try
            {
                ConnectAsync(host, port, timeout).Wait();
            }
            catch (AggregateException ex)
            {
                if (ex.InnerException is TaskCanceledException)  // TODO: Refactor for C# 6.0
                    throw new TimeoutException("Connection failed.");
                else
                    throw;
            }
        }


        /// <summary>
        ///     Disconnect from TWS.
        /// </summary>
        /// <remarks>
        ///     The method returns immediately after the socket is closed, without waiting for TWS to tidy up.
        ///     Do not use if you intend to connect again, as TWS may not be ready yet to accept a new connection using the same client ID.
        /// </remarks>
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
            // Give TWS some time to cleanup. There is no information how much is needed, but experiments show 2s is enough.
            await Task.Delay(TimeSpan.FromSeconds(2));  
            _state = State.Disconnected;
        }

    }
}
