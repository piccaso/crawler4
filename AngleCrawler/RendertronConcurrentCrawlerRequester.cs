using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Io;

namespace AngleCrawler {
    public class RendertronConcurrentCrawlerRequester : IConcurrentCrawlerRequester {
        private readonly string _rendertronUrl;
        private readonly IConcurrentCrawlerRequester _requester;

        public RendertronConcurrentCrawlerRequester(HttpClient httpClient,
            string rendertronUrl = "https://render-tron.appspot.com/") {
            _rendertronUrl = rendertronUrl;
            _requester = new HttpClientConcurrentCrawlerRequester(httpClient);
        }

        public async Task<IResponse> OpenAsync(string url, string referrer, IDictionary<string, string> requestHeaders,
            CancellationToken cancellationToken) {
            var rendertronUrl = $"{_rendertronUrl.TrimEnd('/')}/render/{Uri.EscapeDataString(url)}";
            var response = await _requester.OpenAsync(rendertronUrl, referrer, requestHeaders, cancellationToken);
            return new DefaultResponse {
                Headers = response.Headers,
                StatusCode = response.StatusCode,
                Content = response.Content,
                Address = new Url(url)
            };
        }
    }
}