using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Io;

namespace AngleCrawler {
    public class PrerenderCloudConcurrentCrawlerRequester : IConcurrentCrawlerRequester {
        private readonly string _prerenderCloudServiceUrl;
        private readonly IConcurrentCrawlerRequester _requester;

        public PrerenderCloudConcurrentCrawlerRequester(HttpClient httpClient, string prerenderCloudServiceUrl = "http://service.prerender.cloud/") {
            _prerenderCloudServiceUrl = prerenderCloudServiceUrl;
            _requester = new HttpClientConcurrentCrawlerRequester(httpClient);
        }

        public async Task<IResponse> OpenAsync(string url, string referrer, CancellationToken cancellationToken) {
            // https://www.prerender.cloud/docs/api
            var prerenderCloudUrl = $"{_prerenderCloudServiceUrl.TrimEnd('/')}/{url}";
            var response = await _requester.OpenAsync(prerenderCloudUrl, referrer, cancellationToken);
            return new DefaultResponse {
                Headers = response.Headers,
                StatusCode = response.StatusCode,
                Content = response.Content,
                Address = new Url(url)
            };
        }
    }
}