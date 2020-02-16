using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Io;

namespace AngleCrawler {
    public class ZenscrapeConcurrentCrawlerRequester : IConcurrentCrawlerRequester {
        private readonly IConcurrentCrawlerRequester _requester;
        private readonly string _escapedApiKey;
        private readonly string _escapedRender;

        public ZenscrapeConcurrentCrawlerRequester(HttpClient httpClient, string zenscrapeApiKey,
            bool renderJavascript = true) {
            _requester = new HttpClientConcurrentCrawlerRequester(httpClient);
            _escapedApiKey = Uri.EscapeDataString(zenscrapeApiKey);
            _escapedRender = Uri.EscapeDataString(renderJavascript.ToString().ToLowerInvariant());
        }

        public async Task<IResponse> OpenAsync(string url, string referrer, IDictionary<string, string> requestHeaders,
            CancellationToken cancellationToken) {
            const string baseUrl = "https://app.zenscrape.com/api/v1/get";
            var scrapeUrl = $"{baseUrl}?apikey={_escapedApiKey}&url={Uri.EscapeDataString(url)}&render={_escapedRender}";
            var response = await _requester.OpenAsync(scrapeUrl, referrer, requestHeaders, cancellationToken);
            return new DefaultResponse {
                Headers = response.Headers,
                StatusCode = response.StatusCode,
                Content = response.Content,
                Address = new Url(url)
            };
        }
    }
}