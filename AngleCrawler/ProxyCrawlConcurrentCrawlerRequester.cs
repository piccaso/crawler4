using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Io;

namespace AngleCrawler {
    public class ProxyCrawlConcurrentCrawlerRequester : IConcurrentCrawlerRequester {
        private readonly string _token;
        private readonly IDictionary<string, string> _options;
        private readonly HttpClientConcurrentCrawlerRequester _requester;

        public ProxyCrawlConcurrentCrawlerRequester(HttpClient httpClient, string token, IDictionary<string, string> options = null) {
            _requester = new HttpClientConcurrentCrawlerRequester(httpClient);
            _token = token;
            _options = options;
        }

        public async Task<IResponse> OpenAsync(string url, string referrer, CancellationToken cancellationToken) {
            const string baseUrl = "https://api.proxycrawl.com/?";
            var sb = new StringBuilder();
            sb.Append(baseUrl);
            AddGetParameter(sb, "url", url, true);
            AddGetParameter(sb, "token", _token);
            AddGetParameter(sb, "format", "html");
            AddGetParameter(sb, "get_headers", "true");
            if (_options != null) {
                foreach (var option in _options) {
                    AddGetParameter(sb, option.Key, option.Value);
                }
            }

            IResponse response;
            do {
                response = await _requester.OpenAsync(sb.ToString(), referrer, cancellationToken);
                if (response.StatusCode == HttpStatusCode.TooManyRequests) {
                    response.Dispose();
                    response = null;
                    await Task.Delay(1500, cancellationToken);
                }
            } while (response == null);

            
            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new Exception("To many requests");
            var headers = new Dictionary<string, string>();
            foreach (var (name, value) in response.Headers) {
                if (name == "screenshot_url") headers["x-crawler-screenshot"] = value;
                const string prefix = "original_";
                if(!name.StartsWith(prefix)) continue;
                var originalName = name.Substring(prefix.Length);
                headers[originalName] = value;
            }
            return new DefaultResponse {
                Headers = headers,
                StatusCode = response.Headers.TryGetValue("pc_status", out var pcs) ? (HttpStatusCode)int.Parse(pcs) : response.StatusCode,
                Content = response.Content,
                Address = new Url(response.Headers.TryGetValue("url", out var u) ? u : url)
            };
        }

        private void AddGetParameter(StringBuilder sb, string name, string value, bool first = false) {
            if(!first) sb.Append("&");
            sb.Append(Uri.EscapeDataString(name));
            sb.Append("=");
            sb.Append(Uri.EscapeDataString(value));
        }
    }
}