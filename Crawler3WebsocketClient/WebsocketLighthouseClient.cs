using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Crawler3WebsocketClient {
    public class WebsocketLighthouseClient {
        private readonly WebsocketJsonClient _websocketJsonClient;
        private readonly IWebsocketLogger _logger;
        public WebsocketLighthouseClient(WebsocketJsonClient websocketJsonClient, IWebsocketLogger logger) {
            _websocketJsonClient = websocketJsonClient;
            _logger = logger;
        }

        public string ToJson(object o) {
            var serializer = JsonSerializer.CreateDefault();
            serializer.ContractResolver = new CamelCasePropertyNamesContractResolver();
            return JToken.FromObject(o, serializer).ToString();
        }

        public async Task<(bool ack, IList<LighthouseResponse> responses)> AuditAsync(IEnumerable<WebsocketRequest> urls, CancellationToken ct = default(CancellationToken)) {
            var urlList = urls.ToList();
            var reqJson = ToJson(urlList);
            var ack = false;
            var lhrList = new List<LighthouseResponse>();
            try {
                await _websocketJsonClient.ConnectAsync(ct);
            }
            catch (System.Net.WebSockets.WebSocketException ex) {
                _logger.LogError("unable to connect", ex);
                return (ack, lhrList);
            }
            _logger.LogInfo($"Requesting {urlList.Count} Audits");
            var lighthouseResponses = new Dictionary<string, LighthouseResponse>();
            var receive = true;
            await _websocketJsonClient.SendAsync(reqJson, ct);
            _websocketJsonClient.OnAck += () => ack = true;
            _websocketJsonClient.OnEot += () => receive = false;
            _websocketJsonClient.OnLhr += lhr => {
                lighthouseResponses.AddOrGet(lhr.Url).LhrJson = lhr.Lhr?.ToString(); 
            };
            _websocketJsonClient.OnHtmlReport += htmlReport => {
                lighthouseResponses.AddOrGet(htmlReport.Url).HtmlReport = htmlReport.HtmlReport; 
            };
            _websocketJsonClient.OnScores += scores => {
                ack = true;
                var response = lighthouseResponses.AddOrGet(scores.Url);
                response.Accessibility = scores.Scores?.Accessibility ?? 0;
                response.BestPractices = scores.Scores?.BestPractices ?? 0; 
                response.Performance = scores.Scores?.Performance ?? 0;
                response.Seo = scores.Scores?.Seo ?? 0;
                response.Pwa = scores.Scores?.Pwa ?? 0;
                _logger.LogInfo($"Receiving Scores from url:{scores.Url}");
            };
            _websocketJsonClient.OnError += err => {
                if (string.IsNullOrWhiteSpace(err.Url)) {
                    err.Url = "http://unknown.url";
                }
                lighthouseResponses.AddOrGet(err.Url).Error = err.Error?.ToString() ?? "Undefined Error";
            };

            while (receive) {
                var (message, exception) = await _websocketJsonClient.ReceiveAsync(ct);
                if (exception != null) {
                    _logger.LogWarn("Receive exception", exception);
                }
                if(message == null || exception != null) break;
            }

            foreach (var (url, lighthouseResponse) in lighthouseResponses) {
                lighthouseResponse.Url = url;
            }
            lhrList.AddRange(lighthouseResponses.Values);

            return (ack,lhrList);
        }
    }

    internal static class Extensions {
        public static TV AddOrGet<TK, TV>(this IDictionary<TK, TV> dict, TK key) where TV : new() {
            if (key == null || (key is string strKey && string.IsNullOrWhiteSpace(strKey)) ) {
                return new TV(); // and go on with your life - don't let javascript ruin it!
            }
            if (!dict.TryGetValue(key, out var value)) {
                value     = new TV();
                dict[key] = value;
            }
            return value;
        }
    }
}
