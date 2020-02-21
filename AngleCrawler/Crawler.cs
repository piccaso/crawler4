using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Io;

namespace AngleCrawler
{
    public class CrawlerConfig {
        public string UrlFilter { get; set; }
        public ICollection<string> ExcludeFilters { get; set; } = new List<string>();
        public bool CheckExternalLinks { get; set; }
        public bool FollowInternalLinks { get; set; } = true;
        public int MaxRequestsPerCrawl { get; set; } = 500;
        public int MaxConcurrency { get; set; } = 1;
        public int Retries { get; set; } = 5;
        public int DelayBetweenRetries { get; set; } = 1000;
        public int RequesterTimeout { get; set; } = 90000;
    }

    public class RequestUrl {
        public string Url { get; set; }
        public string Referrer { get; set; }
    }

    public class CrawlerNode {
        public string Url { get; set; }
        public int Status { get; set; }
        public string Title { get; set; }
        public string Html { get; set; }
        public bool External { get; set; }
        public string Error { get; set; }
        public double LoadTimeSeconds { get; set; }
        public IDictionary<string, string> Headers { get; set; }
    }

    public class CrawlerEdge {
        public string Parent { get; set; }
        public string Child { get; set; }
        public string Relation { get; set; }
    }

    public class CrawlerResult {
        public CrawlerNode Node { get; set; }
        public IList<CrawlerEdge> Edges { get; set; }
    }

    public interface IConcurrentCrawlerRequester {
        Task<IResponse> OpenAsync(string url, string referrer, CancellationToken cancellationToken);
    }

    public interface ICustomProcessor {
        Task ProcessAsync(IDocument document, CrawlerNode node, IList<CrawlerEdge> edges);
    }

    public class Crawler : IDisposable {
        private readonly CrawlerConfig _config;
        private readonly IConcurrentCrawlerRequester _requester;
        private readonly IRequestQueue<RequestUrl> _requestQueue;
        private readonly ICustomProcessor _customProcessor;
        private readonly Channel<CrawlerResult> _resultsChannel = Channel.CreateUnbounded<CrawlerResult>();
        public ChannelReader<CrawlerResult> ResultsChannelReader => _resultsChannel.Reader;
        private readonly IConcurrentUrlStore _urlStore = new ConcurrentUrlStore();
        private readonly CancellationTokenSource _cts;
        private readonly PseudoUrl[] _excludeFilters;
        private readonly PseudoUrl _pseudoUrl;

        private long _requestCount = 0;
        private long _activeWorkers = 0;

        public Crawler(CrawlerConfig config, IConcurrentCrawlerRequester requester, IRequestQueue<RequestUrl> requestQueue, ICustomProcessor customProcessor, CancellationToken cancellationToken) {
            _config = config;
            _requester = requester;
            _requestQueue = requestQueue;
            _customProcessor = customProcessor;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _excludeFilters = config.ExcludeFilters.Select(x => new PseudoUrl(x)).ToArray();
            _pseudoUrl = new PseudoUrl(_config.UrlFilter);
        }

        public Task<bool> EnqueueAsync(string url, string referrer = null) {
            return EnqueueAsync(new RequestUrl {Url = url, Referrer = referrer});
        }

        public async Task<bool> EnqueueAsync(RequestUrl requestUrl) {
            requestUrl.Url = RemoveFragment(requestUrl.Url);
            foreach (var excludeFilter in _excludeFilters) {
                if (excludeFilter.Match(requestUrl.Url)) return false;
            }
            if (!_urlStore.Add(requestUrl.Url)) return false;
            var channelSize = await _requestQueue.CountAsync(_cts.Token);
            var requestCount = Interlocked.Read(ref _requestCount);
            var activeWorkers = Interlocked.Read(ref _activeWorkers);
            if (channelSize + requestCount + activeWorkers >= _config.MaxRequestsPerCrawl) return false;
            return await _requestQueue.EnqueueAsync(requestUrl, _cts.Token);
        }

        public async Task CrawlAsync() {
            try {
                var tasks = new List<Task>();
                for (var i = 0; i < _config.MaxConcurrency; i++) {
                    tasks.Add(WorkAsync());
                }

                tasks.Add(StopOnEmptyChannelAsync());

                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) {
                /* just stop */
            }
        }

        public event Action<(long channelSize, long activeWorkers, long requestCount)> OnStatus;

        private async Task StopOnEmptyChannelAsync() {
            var shutdownCount = 0;
            var statusChecksum = 0L;
            while (true) {
                await Task.Delay(500);
                var channelSize = await _requestQueue.CountAsync(_cts.Token);
                var activeWorkers = Interlocked.Read(ref _activeWorkers);
                var requestCount = Interlocked.Read(ref _requestCount);
                if (channelSize <= 0 && activeWorkers <= 0) shutdownCount++;
                else shutdownCount = 0;
                if (shutdownCount > 4 || _cts.IsCancellationRequested) {
                    await _requestQueue.CloseAsync(CancellationToken.None);
                    _resultsChannel.Writer.TryComplete();
                    return;
                }

                var newStatusChecksum = unchecked(channelSize ^ activeWorkers ^ requestCount);
                if (statusChecksum != newStatusChecksum) {
                    statusChecksum = newStatusChecksum;
                    OnStatus?.Invoke((channelSize, activeWorkers, requestCount));
                }
            }
            
        }

        private async Task WorkAsync() {
            while (!_cts.IsCancellationRequested) {
                var (success, requestUrl) = await _requestQueue.DequeueAsync(_cts.Token);
                if(!success) break;

                var requestCount = Interlocked.Read(ref _requestCount);
                if (requestCount >= _config.MaxRequestsPerCrawl) continue;
                Interlocked.Increment(ref _activeWorkers);
                try {
                    await ProcessUrlAsync(requestUrl);
                }
                finally {
                    Interlocked.Decrement(ref _activeWorkers);
                }
            }
        }

        private async Task<bool> WriteResultAsync(CrawlerNode node, IList<CrawlerEdge> edges) {
            try
            {
                await _resultsChannel.Writer.WriteAsync(new CrawlerResult{Node = node, Edges = edges}, _cts.Token);
                return true;
            }
            catch (ChannelClosedException) {
                return false;
            }
        }

        private async Task ProcessUrlAsync(RequestUrl requestUrl) {
            using var timeout = new CancellationTokenSource(_config.RequesterTimeout);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, _cts.Token);
            try
            {
                var (node, edges) = await Try.HarderAsync(_config.Retries, () => ProcessRequestAsync(requestUrl, cts.Token), _config.DelayBetweenRetries, cts.Token);
                Interlocked.Increment(ref _requestCount);
                await WriteResultAsync(node, edges);
                foreach (var edge in edges) {
                    var childExternal = !_pseudoUrl.Match(edge.Child);
                    var parentExternal = !_pseudoUrl.Match(edge.Parent);
                    var takeIt = false;

                    if (!childExternal && !parentExternal && _config.FollowInternalLinks) {
                        takeIt = true;
                    }
                    else if(!childExternal && parentExternal && _config.CheckExternalLinks) {
                        takeIt = true;
                    }
                    
                    if (takeIt) {
                        await EnqueueAsync(edge.Child, edge.Parent);
                    }
                }
            }
            catch (Exception e) {
                var node = new CrawlerNode {
                    Url = requestUrl.Url,
                    Status = (int) HttpStatusCode.GatewayTimeout,
                    External = !_pseudoUrl.Match(requestUrl.Url),
                    Error = e.Message,
                };
                var edges = new List<CrawlerEdge>();
                await WriteResultAsync(node, edges);
            }
        }

        private async Task<(CrawlerNode node, IList<CrawlerEdge> edges)> ProcessRequestAsync(RequestUrl requestUrl, CancellationToken ct) {
            var stopwatch = new Stopwatch();
            var edges = new List<CrawlerEdge>();
            var node = new CrawlerNode();
            stopwatch.Start();
            using var response = await _requester.OpenAsync(requestUrl.Url, requestUrl.Referrer, ct);
            stopwatch.Stop();
            node.LoadTimeSeconds = stopwatch.Elapsed.TotalSeconds;
            var context = BrowsingContext.New();
            using var doc = await context.OpenAsync(response, ct);
            node.Url = doc.Url;
            void AddEdge(string child, string relation, string parent = null) => edges.Add(new CrawlerEdge {Child = child, Parent = parent ?? node.Url, Relation = relation});
            var contentType = doc.ContentType;
            var contentTypeOk = contentType.StartsWith("text/html");
            if (contentTypeOk && doc.QuerySelector("link[rel=canonical]") is IHtmlLinkElement cl && Uri.TryCreate(cl.Href, UriKind.Absolute, out var clHr)) {
                node.Url = clHr.ToString();
            }

            if (requestUrl.Url != node.Url) {
                AddEdge(node.Url, "redirect", requestUrl.Url);
                _urlStore.Add(node.Url);
            }
            node.Status = (int)response.StatusCode;
            node.Headers = new Dictionary<string, string>(response.Headers);
            
            if (contentTypeOk) {
                var links = doc.QuerySelectorAll("a").OfType<IHtmlAnchorElement>();
                foreach (var element in links) {
                    if (Uri.TryCreate(element.Href, UriKind.Absolute, out var childUrl)) {
                        var rel = element.GetAttribute("rel");
                        AddEdge(childUrl.ToString(), rel);
                    }
                }
                node.Html = doc.Source.Text;
                node.Title = doc.QuerySelector("title")?.TextContent?.Trim();
            }
            node.External = !_pseudoUrl.Match(node.Url);

            if (_customProcessor != null) await _customProcessor.ProcessAsync(doc, node, edges);

            return (node, edges);
        }

        public static string RemoveFragment(string url) => url.Contains("#") ? Regex.Replace(url, @"#.*?$", "") : url;

        public void Dispose() {
            _cts.Dispose();
            _urlStore.Dispose();
        }
    }
}
