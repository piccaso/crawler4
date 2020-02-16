﻿using System;
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
        public bool CheckExternalLinks { get; set; }
        public bool FollowInternalLinks { get; set; } = true;
        public int MaxRequestsPerCrawl { get; set; } = 500;
        public int MaxConcurrency { get; set; } = 1;
        public IDictionary<string, string> RequestHeaders { get; set; } = new Dictionary<string, string>();
        public CancellationToken CancellationToken { get; set; } = default;
        public int Retries { get; set; } = 5;
        public int DelayBetweenRetries { get; set; } = 1000;
    }

    public class RequestUrl {
        public string Url { get; set; }
        public string Referrer { get; set; }
    }

    public class CrawlerNode {
        public string Url { get; set; }
        public int Status { get; set; }
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

    public interface IConcurrentCrawlerRequester {
        Task<IResponse> OpenAsync(string url, string referrer, IDictionary<string, string> requestHeaders, CancellationToken cancellationToken);
    }

    public class Crawler : IDisposable {
        private readonly CrawlerConfig _config;
        private readonly IConcurrentCrawlerRequester _requester;
        private readonly Channel<RequestUrl> _urlChannel = Channel.CreateUnbounded<RequestUrl>();
        private readonly Channel<(CrawlerNode node, IList<CrawlerEdge> edges)> _resultsChannel = Channel.CreateUnbounded<(CrawlerNode node, IList<CrawlerEdge> edges)>();
        public ChannelReader<(CrawlerNode node, IList<CrawlerEdge> edges)> ResultsChannelReader => _resultsChannel.Reader;
        private readonly IConcurrentUrlStore _urlStore = new ConcurrentUrlStore();
        private readonly CancellationTokenSource _cts;

        private long _channelSize = 0;
        private long _requestCount = 0;
        private long _activeWorkers = 0;

        public Crawler(CrawlerConfig config, IConcurrentCrawlerRequester requester) {
            _config = config;
            _requester = requester;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(_config.CancellationToken);
        }

        public Task<bool> EnqueueAsync(string url, string referrer = null) {
            return EnqueueAsync(new RequestUrl {Url = url, Referrer = referrer});
        }

        public async Task<bool> EnqueueAsync(RequestUrl requestUrl) {
            var channelSize = Interlocked.Read(ref _channelSize);
            var requestCount = Interlocked.Read(ref _requestCount);
            var activeWorkers = Interlocked.Read(ref _activeWorkers);
            if (channelSize + requestCount + activeWorkers > _config.MaxRequestsPerCrawl) return false;
            try {
                await _urlChannel.Writer.WriteAsync(requestUrl, _cts.Token);
                Interlocked.Increment(ref _channelSize);
                return true;
            }
            catch (ChannelClosedException) {
                return false;
            }
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
                var channelSize = Interlocked.Read(ref _channelSize);
                var activeWorkers = Interlocked.Read(ref _activeWorkers);
                var requestCount = Interlocked.Read(ref _requestCount);
                if (channelSize <= 0 && activeWorkers <= 0) shutdownCount++;
                else shutdownCount = 0;
                if (shutdownCount > 3 || _cts.IsCancellationRequested) {
                    _urlChannel.Writer.TryComplete();
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
            while (await _urlChannel.Reader.WaitToReadAsync(_cts.Token) && !_cts.IsCancellationRequested) {
                if (!_urlChannel.Reader.TryRead(out var requestUrl)) continue;
                Interlocked.Decrement(ref _channelSize);
                var requestCount = Interlocked.Read(ref _requestCount);
                if (requestCount >= _config.MaxRequestsPerCrawl) continue;
                Interlocked.Increment(ref _activeWorkers);
                try {
                    requestUrl.Url = RemoveFragment(requestUrl.Url);
                    if (_urlStore.Add(requestUrl.Url)) await ProcessUrlAsync(requestUrl);
                }
                finally {
                    Interlocked.Decrement(ref _activeWorkers);
                }
            }
        }

        private async Task<bool> WriteResultAsync(CrawlerNode node, IList<CrawlerEdge> edges) {
            try
            {
                await _resultsChannel.Writer.WriteAsync((node, edges), _cts.Token);
                return true;
            }
            catch (ChannelClosedException) {
                return false;
            }
        }

        private async Task ProcessUrlAsync(RequestUrl requestUrl) {
            var pseudoUrl = new PseudoUrl(_config.UrlFilter);
            try
            {
                var (node, edges) = await Try.HarderAsync(_config.Retries, () => ProcessRequestAsync(requestUrl, _config.RequestHeaders), _config.DelayBetweenRetries);
                node.External = !pseudoUrl.Match(node.Url);
                Interlocked.Increment(ref _requestCount);
                await WriteResultAsync(node, edges);
                foreach (var edge in edges) {
                    if(edge.Relation == "redirect") continue; // bin there, done that!
                    var childExternal = !pseudoUrl.Match(edge.Child);
                    var parentExternal = !pseudoUrl.Match(edge.Parent);
                    var takeIt = false;

                    if (!childExternal && !parentExternal && _config.FollowInternalLinks) {
                        takeIt = true;
                    }
                    else if(!childExternal && parentExternal && _config.CheckExternalLinks) {
                        takeIt = true;
                    }

                    if(takeIt) await EnqueueAsync(edge.Child, edge.Parent);
                }
            }
            catch (Exception e) {
                var node = new CrawlerNode {
                    Url = requestUrl.Url,
                    Status = (int) HttpStatusCode.GatewayTimeout,
                    External = !pseudoUrl.Match(requestUrl.Url),
                    Error = e.Message,
                };
                var edges = new List<CrawlerEdge>();
                await WriteResultAsync(node, edges);
            }
        }

        private async Task<(CrawlerNode node, IList<CrawlerEdge> edges)> ProcessRequestAsync(RequestUrl requestUrl, IDictionary<string, string> requestHeaders) {
            var stopwatch = new Stopwatch();
            var edges = new List<CrawlerEdge>();
            var node = new CrawlerNode();
            stopwatch.Start();
            var response = await _requester.OpenAsync(requestUrl.Url, requestUrl.Referrer, requestHeaders, _cts.Token);
            stopwatch.Stop();
            node.LoadTimeSeconds = stopwatch.Elapsed.TotalSeconds;
            var context = BrowsingContext.New();
            using var doc = await context.OpenAsync(response, _cts.Token);
            node.Url = doc.Url;
            void AddEdge(string child, string relation, string parent = null) => edges.Add(new CrawlerEdge {Child = child, Parent = parent ?? node.Url, Relation = relation});
            var contentType = doc.ContentType;
            var contentTypeOk = contentType.StartsWith("text/html");
            if (contentTypeOk && doc.QuerySelector("link[rel=canonical]") is IHtmlLinkElement cl && Uri.TryCreate(cl.Href, UriKind.Absolute, out var clHr)) {
                node.Url = clHr.ToString();
            }

            if (requestUrl.Url != node.Url) {
                AddEdge(node.Url, "redirect", requestUrl.Url);
            }
            node.Status = (int)response.StatusCode;
            node.Headers = response.Headers;
            
            if (contentTypeOk) {
                var links = doc.QuerySelectorAll("a").OfType<IHtmlAnchorElement>();
                foreach (var element in links) {
                    if (Uri.TryCreate(element.Href, UriKind.Absolute, out var childUrl)) {
                        var rel = element.GetAttribute("rel");
                        AddEdge(childUrl.ToString(), rel);
                    }
                }
                node.Html = doc.Source.Text;
            }

            await response.Content.DisposeAsync();
            return (node, edges);
        }

        public static string RemoveFragment(string url) => url.Contains("#") ? Regex.Replace(url, @"#.*?$", "") : url;

        public void Dispose() {
            _cts.Dispose();
            _urlStore.Dispose();
        }
    }
}