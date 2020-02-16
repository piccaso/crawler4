using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Io;
using HttpMethod = System.Net.Http.HttpMethod;

namespace AngleCrawler
{
    public class HttpClientConcurrentCrawlerRequester : IConcurrentCrawlerRequester
    {
        private readonly HttpClient _httpClient;

        public HttpClientConcurrentCrawlerRequester(HttpClient httpClient) {
            _httpClient = httpClient;
        }

        public async Task<IResponse> OpenAsync(string url, string referrer, IDictionary<string, string> requestHeaders, CancellationToken cancellationToken) {
            var contentStream = new MemoryStream();
            using var msg = new HttpRequestMessage(HttpMethod.Get, url);
            requestHeaders ??= new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(referrer)) requestHeaders["referer"] = referrer;
            foreach (var (key, value) in requestHeaders) {
                if(!msg.Headers.TryAddWithoutValidation(key, value)) msg.Content.Headers.TryAddWithoutValidation(key, value);
            }
            using var response = await _httpClient.SendAsync(msg, cancellationToken);
            await response.Content.CopyToAsync(contentStream);
            contentStream.Seek(0, SeekOrigin.Begin);
            return new DefaultResponse {
                Headers = response.Headers.ToDictionary(k => k.Key, v => string.Join(", ", v)),
                StatusCode = response.StatusCode,
                Address = new Url(response.RequestMessage.RequestUri.ToString()),
                Content = contentStream,
            };
        }
    }
}
