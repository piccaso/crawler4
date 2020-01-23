using System;
using System.Text.Json;


namespace Crawler3WebsocketClient {
    public class JsonProcessor {
        private readonly IWebsocketLogger _logger;
        private readonly JsonSerializerOptions _serializerOptions;
        public JsonProcessor(IWebsocketLogger logger = null) {
            _logger = logger;
            _serializerOptions = new JsonSerializerOptions() {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
            };
        }

        public string Serialize(object o) {
            return JsonSerializer.Serialize(o, _serializerOptions);
        }
        public T Deserialize<T>(string json) {
            return JsonSerializer.Deserialize<T>(json, _serializerOptions);
        }

        public event Action OnEot;
        public event Action<CrawlerResponseEdges> OnEdges;
        public event Action<CrawlerResponseNode> OnNode;
        public event Action<CrawlerResponseStatus> OnStatus;

        public void ProcessMessage(string message) {
            if(message is null || message.Length < 3 || message[0] != '!') {
                message ??= "<null>";
                if (message.StartsWith("Error", StringComparison.InvariantCultureIgnoreCase)) {
                    _logger?.LogError(message);
                } else if(message.StartsWith("Warn", StringComparison.InvariantCultureIgnoreCase)) {
                    _logger?.LogWarn(message);
                } else {
                    _logger?.LogInfo(message);
                }
                return;
            }
            message = message.TrimStart('!');
            try {
                var responseBase = Deserialize<CrawlerResponseBase>(message);
                switch (responseBase.Type) {
                    case "eot": OnEot?.Invoke(); break;
                    case "status": OnStatus?.Invoke(Deserialize<CrawlerResponseStatus>(message)); break;
                    case "edges": OnEdges?.Invoke(Deserialize<CrawlerResponseEdges>(message)); break;
                    case "node": OnNode?.Invoke(Deserialize<CrawlerResponseNode>(message)); break;
                    default: _logger?.LogWarn($"Unknown Message Type `{responseBase.Type ?? "<null>"}`"); break;
                }
            }
            catch (JsonException ex) {_logger?.LogWarn($"invalid json: {message}", ex);}
        }
    }
}
