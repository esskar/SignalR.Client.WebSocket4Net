using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNet.SignalR.Client.Transports;
using SuperSocket.ClientEngine;
using WebSocket4Net;
using Microsoft.AspNet.SignalR.Client.Http;
using System.Net;
using SuperSocket.ClientEngine.Proxy;
using Microsoft.AspNet.SignalR.Client.Infrastructure;
using Microsoft.AspNet.SignalR.WebSockets;
using ClientR.WebSockets4Net;

namespace SignalR.Client.WebSocket4Net
{
    public class WebSocket4NetTransport : WebSocketHandler, IClientTransport, IDisposable
    {
        private IHttpClient _client;
        private readonly TransportAbortHandler _abortHandler;
        private CancellationToken _disconnectToken;
        private TransportInitializationHandler _initializeHandler;
        private WebSocketConnectionInfo _connectionInfo;
        private CancellationTokenSource _webSocketTokenSource;
        private WebSocket _webSocket;
        private TaskCompletionSource<object> _connectCompletionSource;

        private bool _connected;
        private int _disposed;

        public WebSocket4NetTransport() : this(new DefaultHttpClient())
        {
        }
        public WebSocket4NetTransport(IHttpClient httpClient) : base(null)
        {
            this._client = httpClient;
            _disconnectToken = CancellationToken.None;
            this._abortHandler = new TransportAbortHandler(httpClient, this.Name);
            this.ReconnectDelay = TimeSpan.FromSeconds(2);
        }

        public TimeSpan ReconnectDelay { get; set; }

        public void Dispose()
        {
            this.Disconnect();
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (Interlocked.Exchange(ref this._disposed, 1) == 1)
                {
                    return;
                }
                if (this._webSocketTokenSource != null)
                {
                    this._webSocketTokenSource.Cancel();
                }
                this._abortHandler.Dispose();
                if (this._webSocketTokenSource != null)
                {
                    this._webSocketTokenSource.Dispose();
                }
            }
        }
        private async void DoReconnect()
        {
            while (TransportHelper.VerifyLastActive(this._connectionInfo.Connection) && this._connectionInfo.Connection.EnsureReconnecting())
            {
                try
                {
                    await this.PerformConnect(true);
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (ExceptionHelper.IsRequestAborted(ex))
                    {
                        break;
                    }
                    this._connectionInfo.Connection.OnError(ex);
                }
                await Task.Delay(this.ReconnectDelay);
            }
        }
        public override void OnClose()
        {
            this._connectionInfo.Connection.Trace(TraceLevels.Events, "WS: OnClose()", new object[0]);
            if (this._disconnectToken.IsCancellationRequested)
            {
                return;
            }
            if (this._abortHandler.TryCompleteAbort())
            {
                return;
            }
            this.DoReconnect();
        }
        public override void OnError()
        {
            this._connectionInfo.Connection.OnError(base.Error);
        }

        

        public Task<NegotiationResponse> Negotiate(IConnection connection, string connectionData)
        {
            return this._client.GetNegotiationResponse(connection, connectionData);
        }

        public Task Start(IConnection connection, string connectionData, CancellationToken disconnectToken)
        {
            if (connection == null)
            {
                throw new ArgumentNullException("connection");
            }
            this._initializeHandler = new TransportInitializationHandler(connection.TotalTransportConnectTimeout, disconnectToken);
            this._initializeHandler.OnFailure += delegate
            {
                this.Dispose();
            };

            _disconnectToken = disconnectToken;
            _connectionInfo = new WebSocketConnectionInfo(connection, connectionData);

            _connectCompletionSource = new TaskCompletionSource<object>();
            this.PerformConnect().ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    this._initializeHandler.Fail(t.Exception);
                    _connectCompletionSource.SetException(t.Exception);
                }
                else if (t.IsCanceled)
                {
                    this._initializeHandler.Fail();
                    _connectCompletionSource.SetCanceled();
                }
            }, TaskContinuationOptions.NotOnRanToCompletion);

            return _connectCompletionSource.Task;
        }

        public Task Send(IConnection connection, string data, string connectionData)
        {
            if (connection == null)
                throw new ArgumentNullException("connection");
                     
            return this.SendAsync(connection, data);
        }

        private Task SendAsync(IConnection connection, string data)
        {
            return Task.Run(() =>
            {
                if (_webSocket.State != WebSocketState.Open)
                {
                    var ex = new InvalidOperationException("Data cannot be sent during web socket reconnect.");
                    connection.OnError(ex);

                    throw ex;
                }

                _webSocket.Send(data);
            }, _disconnectToken);
        }

        public void Abort(IConnection connection, TimeSpan timeout, string connectionData)
        {
            this._abortHandler.Abort(connection, timeout, connectionData);
        }

        public void LostConnection(IConnection connection)
        {
            this._connectionInfo.Connection.Trace(TraceLevels.Events, "WS: LostConnection", new object[0]);
            if (this._webSocketTokenSource != null)
            {
                this._webSocketTokenSource.Cancel();
            }
        }

        public string Name
        {
            get { return "webSockets"; }
        }

        public bool SupportsKeepAlive
        {
            get { return true; }
        }

        public virtual Task PerformConnect()
        {
            return this.PerformConnect(false);
        }

        private Task PerformConnect(bool reconnecting = false)
        {
            return Task.Run(() =>
            {
                this.Disconnect(true);

                var url = _connectionInfo.Connection.Url + (reconnecting ? "reconnect" : "connect");
                url += TransportHelper.GetReceiveQueryString(_connectionInfo.Connection, _connectionInfo.Data, this.Name);

                var builder = new UriBuilder(url);
                builder.Scheme = builder.Scheme == "https" ? "wss" : "ws";

                _connectionInfo.Connection.Trace(TraceLevels.Events, "WS connecting to: {0}", builder.Uri);

                this._webSocketTokenSource = new CancellationTokenSource();
                _webSocket = new WebSocket(builder.Uri.AbsoluteUri);

                var proxy = System.Net.HttpWebRequest.GetSystemWebProxy();
                if (proxy != null)
                {
                    Uri destUri = new Uri(url);
                    Uri proxyUri = proxy.GetProxy(destUri);
                    if (proxyUri != null)
                    { 
                        DnsEndPoint proxyEndPoint = new DnsEndPoint(proxyUri.Host, proxyUri.Port);
                        SuperSocket.ClientEngine.Proxy.HttpConnectProxy ssProxy = new SuperSocket.ClientEngine.Proxy.HttpConnectProxy(proxyEndPoint);
                        _webSocket.Proxy = ssProxy;
                    }
                }
                _webSocket.Error += OnWebSocketError;
                _webSocket.Opened += OnWebSocketOpened;
                _webSocket.Closed += OnWebSocketClosed;
                _webSocket.DataReceived += OnWebSocketDataReceived;
                _webSocket.MessageReceived += OnMessage;
                _webSocket.Open();
            }, _disconnectToken);
        }

        private void Disconnect(bool releaseOnly = false)
        {
            if (_webSocket == null)
                return;

            _webSocket.Closed -= OnWebSocketClosed;
            if (!releaseOnly)
                _webSocket.Close();

            _webSocket.Error -= OnWebSocketError;
            _webSocket.Opened -= OnWebSocketOpened;
            _webSocket.DataReceived -= OnWebSocketDataReceived;
            _webSocket.MessageReceived -= OnMessage;
        }

        private void OnWebSocketError(object sender, ErrorEventArgs errorEventArgs)
        {
            _connectionInfo.Connection.OnError(errorEventArgs.Exception);
            if (!_connected)
                _connectCompletionSource.SetException(errorEventArgs.Exception);
        }

        private void OnMessage(object sender, MessageReceivedEventArgs messageReceivedEventArgs)
        {
            string message = messageReceivedEventArgs.Message;
            this._connectionInfo.Connection.Trace(TraceLevels.Messages, "WS: OnMessage({0})", new object[]
			{
				message
			});
            bool shouldReconnect;
            bool disconnected;
            IConnection connection = this._connectionInfo.Connection;
            string response = message;
            TransportHelper.ProcessResponse(this._connectionInfo.Connection, message, out shouldReconnect, out disconnected, new Action(this._initializeHandler.Success));
            if (disconnected && !this._disconnectToken.IsCancellationRequested)
            {
                this._connectionInfo.Connection.Trace(TraceLevels.Messages, "Disconnect command received from server.", new object[0]);
                this._connectionInfo.Connection.Disconnect();
            }                        
        }

        public override void OnOpen()
        {
            if (this._connectionInfo.Connection.ChangeState(ConnectionState.Reconnecting, ConnectionState.Connected))
            {
                this._connectionInfo.Connection.OnReconnected();
            }
        }

        private void OnWebSocketDataReceived(object sender, DataReceivedEventArgs dataReceivedEventArgs)
        {
            throw new NotImplementedException();
        }

        private void OnWebSocketClosed(object sender, EventArgs eventArgs)
        {
            throw new NotImplementedException();
        }

        private void OnWebSocketOpened(object sender, EventArgs eventArgs)
        {
            _connected = true;
            _connectCompletionSource.SetResult(null);
        }

        private class WebSocketConnectionInfo
        {
            public readonly IConnection Connection;
            public readonly string Data;

            public WebSocketConnectionInfo(IConnection connection, string data)
            {
                this.Connection = connection;
                this.Data = data;
            }
        }        
    }
}
