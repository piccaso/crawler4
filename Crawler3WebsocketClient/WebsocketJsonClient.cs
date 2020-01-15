﻿using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Crawler3WebsocketClient {
    public class WebsocketJsonClient : IDisposable {
        private readonly Uri _socketUrl;
        private readonly IWebsocketLogger _logger;
        private readonly int _bufferSize;
        private readonly Encoding _encoding;
        private readonly JsonSerializer _serializer;
        private ClientWebSocket _socket = new ClientWebSocket();
        
        public WebsocketJsonClient(Uri socketUrl, IWebsocketLogger logger = null, ICredentials credentials = null, int bufferSize = 5_242_880, Encoding encoding = null, IWebProxy proxy = null) {
            _socketUrl = socketUrl;
            _logger = logger;
            _bufferSize = bufferSize;
            _encoding = encoding ?? Encoding.UTF8;
            if (credentials != null) _socket.Options.Credentials = credentials;
            if (proxy != null) _socket.Options.Proxy = proxy;
            if (_socket.Options.Credentials == null && !string.IsNullOrEmpty(_socketUrl.UserInfo) && _socketUrl.UserInfo.Contains(":")) {
                var split = _socketUrl.UserInfo.Split(':');
                if(split.Length == 2) _socket.Options.Credentials = new NetworkCredential(Uri.UnescapeDataString(split[0]), Uri.UnescapeDataString(split[1]));
            }
            _serializer = JsonSerializer.CreateDefault();
            _serializer.ContractResolver = new CamelCasePropertyNamesContractResolver();
            _serializer.NullValueHandling = NullValueHandling.Ignore;
        }

        public void Connect(CancellationToken cancellationToken = default) => ConnectAsync(cancellationToken).GetAwaiter().GetResult();
        public async Task ConnectAsync(CancellationToken cancellationToken = default) {
            if(_socket.State == WebSocketState.Open) return;
            _logger?.LogInfo("Websocket Connected");
            await _socket.ConnectAsync(_socketUrl, cancellationToken);
        }

        internal void Send(string message, CancellationToken cancellationToken = default) => SendAsync(message, cancellationToken).GetAwaiter().GetResult();
        internal async Task SendAsync(string message, CancellationToken cancellationToken = default) {
            await ConnectAsync(cancellationToken);

            var buffer = _encoding.GetBytes(message);
            var segment = new ArraySegment<byte>(buffer);
            await _socket.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken);
        }

        public void Send(CrawlerConfig config, CancellationToken cancellationToken = default) => SendAsync(config, cancellationToken).GetAwaiter().GetResult();
        public async Task SendAsync(CrawlerConfig config, CancellationToken cancellationToken = default) {
            var message = JToken.FromObject(config, _serializer).ToString();
            await SendAsync(message, cancellationToken);
        }

        public Exception ReceiveAll(CancellationToken cancellationToken = default) => ReceiveAllAsync(cancellationToken).GetAwaiter().GetResult();
        public async Task<Exception> ReceiveAllAsync(CancellationToken cancellationToken = default) {
            Exception lastException = null;
            while (!cancellationToken.IsCancellationRequested) {
                var (message, exception) = await ReceiveAsync(cancellationToken);
                if (message == null || exception != null) {
                    lastException = exception;
                    break;
                }
            }
            return lastException;
        }


        internal (string message,Exception exception) Receive(CancellationToken cancellationToken = default) => ReceiveAsync(cancellationToken).GetAwaiter().GetResult();
        internal async Task<(string message,Exception exception)> ReceiveAsync(CancellationToken cancellationToken = default) {

            Exception ex = null;
            try {
                await ConnectAsync(cancellationToken);

                await using var ms = new MemoryStream();
                var buffer = new byte[_bufferSize];
                var segment = new ArraySegment<byte>(buffer, 0, buffer.Length);
                bool endOfMessage;
                do {
                    var result = await _socket.ReceiveAsync(segment, cancellationToken);
                    if (result.MessageType != WebSocketMessageType.Text) return (null, ex);
                    endOfMessage = result.EndOfMessage;
                    if (result.Count > 0) {
                        ms.Write(segment.AsSpan(0, result.Count));
                    }
                } while (!endOfMessage);

                ms.Seek(0, SeekOrigin.Begin);

                var message = _encoding.GetString(ms.ToArray());
                ProcessMessage(message);

                return (message, ex);
            } catch (WebSocketException e) {
                ex = e;
            } catch (TaskCanceledException e) {
                ex = e;
            } catch (OperationCanceledException e) {
                ex = e;
            }
            _logger?.LogWarn("Exception on receive", ex);
            return (null, ex);
        }

        public event Action OnAck;
        public event Action OnEot;
        public event Action<CrawlerResponseEdge> OnEdge;
        public event Action<CrawlerResponseNode> OnNode;

            
        private void ProcessMessage(string message) {
            if(message is null || message.Length < 3 || message[0] != '!') {
                _logger?.LogInfo(message ?? "<null>");
                return;
            }
            message = message.TrimStart('!');
            try {
                var jt = JToken.Parse(message);
                var responseBase = jt.ToObject<CrawlerResponseBase>(_serializer);
                _logger.LogInfo($"Receiving message, type={responseBase.Type ?? "<null>"}");
                switch (responseBase.Type) {
                    case "ack": OnAck?.Invoke(); break;
                    case "eot": OnEot?.Invoke(); break;
                    case "edge": OnEdge?.Invoke(jt.ToObject<CrawlerResponseEdge>()); break;
                    case "node": OnNode?.Invoke(jt.ToObject<CrawlerResponseNode>()); break;
                    default: break;
                }
            }
            catch (JsonReaderException ex) {_logger?.LogWarn($"invalid json: {message}", ex);}
            catch (JsonSerializationException ex) {_logger?.LogWarn($"invalid json: {message}", ex);}
        }

        /// <inheritdoc />
        public void Dispose() {
            if (_socket != null) {
                _socket.Dispose();
                _socket = null;
            }
        }
    }
}