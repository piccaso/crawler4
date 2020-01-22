using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Crawler3WebsocketClient {
    public class JsonProcessor {
        private readonly IWebsocketLogger _logger;
        private readonly JsonSerializer _serializer;
        public JsonProcessor(IWebsocketLogger logger = null) {
            _logger = logger;
            _serializer = JsonSerializer.CreateDefault();
            _serializer.ContractResolver = new CamelCasePropertyNamesContractResolver();
            _serializer.NullValueHandling = NullValueHandling.Ignore;   
        }

        public string Serialize(object o) {
            return JToken.FromObject(o, _serializer).ToString();
        }

        public event Action OnEot;
        public event Action<CrawlerResponseEdges> OnEdges;
        public event Action<CrawlerResponseNode> OnNode;
        public event Action<CrawlerResponseStatus> OnStatus;

        public void ProcessMessage(string message) {
            if(message is null || message.Length < 3 || message[0] != '!') {
                _logger?.LogInfo(message ?? "<null>");
                return;
            }
            message = message.TrimStart('!');
            try {
                var jt = JToken.Parse(message);
                var responseBase = jt.ToObject<CrawlerResponseBase>(_serializer);
                //_logger.LogInfo($"Receiving message, type={responseBase.Type ?? "<null>"}");
                switch (responseBase.Type) {
                    case "eot": OnEot?.Invoke(); break;
                    case "status": OnStatus?.Invoke(jt.ToObject<CrawlerResponseStatus>()); break;
                    case "edges": OnEdges?.Invoke(jt.ToObject<CrawlerResponseEdges>()); break;
                    case "node": OnNode?.Invoke(jt.ToObject<CrawlerResponseNode>()); break;
                    default: break;
                }
            }
            catch (JsonReaderException ex) {_logger?.LogWarn($"invalid json: {message}", ex);}
            catch (JsonSerializationException ex) {_logger?.LogWarn($"invalid json: {message}", ex);}
        }
    }
}
