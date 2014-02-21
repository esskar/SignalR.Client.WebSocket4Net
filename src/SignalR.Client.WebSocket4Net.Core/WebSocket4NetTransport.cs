using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNet.SignalR.Client.Infrastructure;
using Microsoft.AspNet.SignalR.Client.Transports;
using SuperSocket.ClientEngine;
using WebSocket4Net;

namespace SignalR.Client.WebSocket4Net
{
    public class WebSocket4NetTransport : IClientTransport
    {
        private CancellationToken _disconnectToken;
        private WebSocket _webSocket;
        private WebSocketConnectionInfo _connectionInfo;
        private TaskCompletionSource<object> _connectCompletionSource;
        private bool _isOpen;

        public WebSocket4NetTransport()
        {
            _disconnectToken = CancellationToken.None;
            this.ReconnectDelay = TimeSpan.FromSeconds(2);
        }

        public TimeSpan ReconnectDelay { get; set; }

        public void Dispose()
        {
            this.Disconnect();
        }

        public Task<NegotiationResponse> Negotiate(IConnection connection, string connectionData)
        {
            throw new NotImplementedException();
        }

        public Task Start(IConnection connection, string connectionData, CancellationToken disconnectToken)
        {
            _disconnectToken = disconnectToken;
            _connectionInfo = new WebSocketConnectionInfo(connection, connectionData);

            _connectCompletionSource = new TaskCompletionSource<object>();
            this.Connect().ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _connectCompletionSource.SetException(t.Exception);
                }
                else if (t.IsCanceled)
                {
                    _connectCompletionSource.SetCanceled();
                }
            }, TaskContinuationOptions.NotOnRanToCompletion);

            return _connectCompletionSource.Task;
        }

        public Task Send(IConnection connection, string data, string connectionData)
        {
            throw new NotImplementedException();
        }

        public void Abort(IConnection connection, TimeSpan timeout, string connectionData)
        {
            throw new NotImplementedException();
        }

        public void LostConnection(IConnection connection)
        {
            throw new NotImplementedException();
        }

        public string Name
        {
            get { return "webSockets"; }
        }

        public bool SupportsKeepAlive
        {
            get { return true; }
        }

        private Task Connect(bool reconnecting = false)
        {
            return Task.Run(() =>
            {
                this.Disconnect(true);

                var url = _connectionInfo.Connection.Url + (reconnecting ? "reconnect" : "connect");
                url += TransportHelper.GetReceiveQueryString(_connectionInfo.Connection, _connectionInfo.Data, this.Name);

                var builder = new UriBuilder(url);
                builder.Scheme = builder.Scheme == "https" ? "wss" : "ws";

                _connectionInfo.Connection.Trace(TraceLevels.Events, "WS connecting to: {0}", builder.Uri);

                _webSocket = new WebSocket(builder.Uri.AbsoluteUri);
                _webSocket.Error += OnWebSocketError;
                _webSocket.Opened += OnWebSocketOpened;
                _webSocket.Closed += OnWebSocketClosed;
                _webSocket.DataReceived += OnWebSocketDataReceived;
                _webSocket.MessageReceived += OnWebSocketDataReceived;
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
            _webSocket.MessageReceived -= OnWebSocketDataReceived;
        }        

        private void OnWebSocketError(object sender, ErrorEventArgs errorEventArgs)
        {
            _connectionInfo.Connection.OnError(errorEventArgs.Exception);
            if (!_isOpen)
                _connectCompletionSource.SetException(errorEventArgs.Exception);
        }

        private void OnWebSocketDataReceived(object sender, MessageReceivedEventArgs messageReceivedEventArgs)
        {
            throw new NotImplementedException();
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
            _isOpen = true;
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