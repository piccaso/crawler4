using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Crawler3WebsocketClient {
    public class Crawler3Client {
        private readonly Func<WebsocketJsonClient> _websocketClientFactory;
        private readonly IWebsocketLogger _logger;

        public Crawler3Client(Func<WebsocketJsonClient> websocketClientFactory, IWebsocketLogger logger) {
            _websocketClientFactory = websocketClientFactory;
            _logger = logger;
        }

        public async Task<(ICollection<CrawlerResponseEdge> edges, ICollection<CrawlerResponseNode> nodes)>
            FetchUrlsAsync(IEnumerable<string> urls, string urlFilter, bool screenShots, int maxRetries = 10,
                CancellationToken cancellationToken = default) {
            var config = new CrawlerConfig {
                TakeScreenShots = screenShots,
                FollowInternalLinks = false,
                UrlFilter = urlFilter
            };

            foreach (var url in urls) config.RequestQueue.Add(url);

            config.MaxRequestsPerCrawl = config.RequestQueue.Count * 110; // count + 10%

            var edges = new List<CrawlerResponseEdge>();
            var nodes = new List<CrawlerResponseNode>();
            var eot = false;

            void OnEotAction() {
                eot = true;
            }

            void OnEdgesAction(CrawlerResponseEdges newEdges) {
                edges.AddRange(edges);
            }

            void OnNodeAction(CrawlerResponseNode newNode) {
                nodes.Add(newNode);
            }

            void OnStatusAction(CrawlerResponseStatus crawlerStatus = null) {
                var statusMsg = $"Crawler3Client Status: {nodes.Count}/{config.RequestQueue.Count} Nodes";
                _logger?.LogInfo(statusMsg);
            }

            while (!cancellationToken.IsCancellationRequested && !eot)
                try {
                    using var socket = _websocketClientFactory();
                    try {
                        edges.Clear();
                        nodes.Clear();
                        socket.OnNode += OnNodeAction;
                        socket.OnEot += OnEotAction;
                        socket.OnEdges += OnEdgesAction;
                        socket.OnStatus += OnStatusAction;
                        await socket.SendAsync(config, cancellationToken);
                        var exception = await socket.ReceiveAllAsync(cancellationToken: cancellationToken);
                        if (exception != null && !eot) throw exception;
                    }
                    finally {
                        socket.OnNode -= OnNodeAction;
                        socket.OnEot -= OnEotAction;
                        socket.OnEdges -= OnEdgesAction;
                        socket.OnStatus -= OnStatusAction;
                    }
                }
                catch (Exception ex) {
                    maxRetries--;
                    if (maxRetries <= 0) throw;
                    _logger.LogInfo("Retrying", ex);
                }

            return (edges, nodes);
        }
    }
}