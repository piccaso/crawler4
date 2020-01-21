using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
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
        private readonly Channel<string> _jsonChannel = Channel.CreateUnbounded<string>();
        
        public WebsocketJsonClient(Uri socketUrl, IWebsocketLogger logger = null, ICredentials credentials = null, int bufferSize = 10_485_760, Encoding encoding = null, IWebProxy proxy = null) {
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

        public async Task<Exception> ReceiveAllAsync(int timeOutMsec = 1000 * 60 * 5, CancellationToken cancellationToken = default) {
            await ConnectAsync(cancellationToken);

            var processingTask = Task.Run(async () => {
                await ProcessAsync(cancellationToken);
            });

            Exception lastException = null;
            var eot = false;
            OnEot += () => eot = true;

            var receiveLoopTask = Task.Run(async () => {
                while (!cancellationToken.IsCancellationRequested && !eot) {

                    using var cts = new CancellationTokenSource();
                    cts.CancelAfter(timeOutMsec);
                    using var combinedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);

                    var (message, exception) = await ReceiveAsync(combinedCancellationTokenSource.Token);

                    if (exception != null) {
                        lastException = exception;
                        break;
                    }

                    if (message == null) {
                        lastException = new Exception("empty message received");
                        break;
                    } else {
                        await _jsonChannel.Writer.WriteAsync(message, cancellationToken);
                    }
                }
                _jsonChannel.Writer.Complete();
            });

            await receiveLoopTask;
            await processingTask;

            if (lastException != null && !eot) {
                _logger?.LogError("Exception on receive", lastException);
            }

            return lastException;
        }


        internal async Task<(string message,Exception exception)> ReceiveAsync(CancellationToken cancellationToken = default) {

            Exception ex = null;
            try {

                await using var ms = new MemoryStream();
                var buffer = new byte[_bufferSize];
                var segment = new ArraySegment<byte>(buffer, 0, buffer.Length);
                bool endOfMessage;
                do {
                    var result = await _socket.ReceiveAsync(segment, cancellationToken);
                    if (result.MessageType != WebSocketMessageType.Text) {
                        _logger?.LogError($"Unsupported MessageType '{result.MessageType}'");
                        return (null, ex);
                    }
                    endOfMessage = result.EndOfMessage;
                    if (result.Count > 0) {
                        ms.Write(segment.AsSpan(0, result.Count));
                    }
                } while (!endOfMessage);

                ms.Seek(0, SeekOrigin.Begin);

                var message = _encoding.GetString(ms.ToArray());

                return (message, ex);
            } catch (WebSocketException e) {
                ex = e;
            } catch (TaskCanceledException e) {
                ex = e;
            } catch (OperationCanceledException e) {
                ex = e;
            }
            return (null, ex);
        }

        public event Action OnEot;
        public event Action<CrawlerResponseEdge> OnEdge;
        public event Action<CrawlerResponseNode> OnNode;
        public event Action<CrawlerResponseStatus> OnStatus;

        private async Task ProcessAsync(CancellationToken ct) {
            while (!ct.IsCancellationRequested) {
                var message = await _jsonChannel.Reader.ReadAsync(ct);
                ProcessMessage(message);
                if(_jsonChannel.Reader.Completion.IsCompleted) break;
            }
        }

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
                    case "eot": OnEot?.Invoke(); break;
                    case "status": OnStatus?.Invoke(jt.ToObject<CrawlerResponseStatus>()); break;
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
