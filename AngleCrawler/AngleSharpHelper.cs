using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Dom.Events;
using AngleSharp.Html.Dom;
using AngleSharp.Io;

namespace AngleCrawler
{
    public static class AngleSharpHelper
    {

        public static async Task<ExtendedDocument> GetDocumentAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
        {
            // https://github.com/aspnet/Docs/blob/01b78e00ecd1184c6a5089934a91cf43b1229ef9/aspnetcore/test/integration-tests/samples/2.x/IntegrationTestsSample/tests/RazorPagesProject.Tests/Helpers/HtmlHelpers.cs
            var content = await response.Content.ReadAsStringAsync();
            var document = await BrowsingContext.New()
                                                .OpenAsync(ResponseFactory, cancellationToken);

            var responseHeaders = new HttpResponseMessage(HttpStatusCode.OK).Headers;
            foreach (var header in response.Headers) responseHeaders.TryAddWithoutValidation(header.Key, header.Value);
            foreach (var header in response.Content.Headers) responseHeaders.TryAddWithoutValidation(header.Key, header.Value);

            return new ExtendedDocument
            {
                Document = (IHtmlDocument)document,
                Headers = responseHeaders,
            };

            void ResponseFactory(VirtualResponse htmlResponse)
            {
                htmlResponse
                   .Address(response.RequestMessage.RequestUri)
                   .Status(response.StatusCode);

                MapHeaders(response.Headers);
                MapHeaders(response.Content.Headers);

                htmlResponse.Content(content);

                void MapHeaders(HttpHeaders headers)
                {
                    foreach (var header in headers)
                    {
                        foreach (var value in header.Value)
                        {
                            htmlResponse.Header(header.Key, value);
                        }
                    }
                }
            }
        }

        public static IBrowsingContext DefaultContext(string userAgent = null, IDictionary<string, string> headers = null) {
            userAgent ??= $"Mozilla/5.0 (compatible; crawler)";
            headers ??= new Dictionary<string, string> {{"accept", "*/*"}};
            var config = Configuration.Default;
            if (string.IsNullOrWhiteSpace(userAgent)) userAgent = null;
            var requester = new CustomRequester(userAgent, headers);
            config = config.With(requester);
            config = config.WithDefaultLoader();
            return BrowsingContext.New(config);
        }

        public static async Task<ExtendedDocument> OpenAsyncExt(this IBrowsingContext context, string address, string referer = null, CancellationToken ct = default)
        {
            var requester = context.GetService<CustomRequester>();
            var request = DocumentRequest.Get(new Url(address), null, referer);
            var document = (IHtmlDocument)await context.OpenAsync(request, ct);
            return new ExtendedDocument
            {
                Headers = requester.LastHeaders,
                StatusCode = requester.LastStatusCode,
                Document = document,
            };
        }

        public static Task<ExtendedDocument> OpenAsync(string address)
        {
                return DefaultContext().OpenAsyncExt(address);
        }

        public class ExtendedDocument
        {
            public IHtmlDocument Document { get; set; }
            public int StatusCode { get; set; }
            public HttpHeaders Headers { get; set; }
        }

        public class CustomRequester : IRequester
        {
            private readonly IRequester _dhr;
            public CustomRequester(string userAgent, IDictionary<string, string> headers, IWebProxy proxy = null)
            {
                _dhr = new DefaultHttpRequester(userAgent, s => {
                    if (headers != null && headers.Any())
                    {
                        foreach (var header in headers)
                        {
                            s.Headers.Add(header.Key, header.Value);
                        }
                        if (proxy != null) s.Proxy = proxy;
                        //proxy = new WebProxy("localhost:8888");
                    }
                });
                _dhr.Requesting += (sender, ev) => Requesting?.Invoke(sender, ev);
                _dhr.Requested += (sender, ev) => Requested?.Invoke(sender, ev);
            }

            public int LastStatusCode { get; private set; }
            public HttpHeaders LastHeaders { get; } = new HttpResponseMessage(HttpStatusCode.OK).Headers;
            public async Task<IResponse> RequestAsync(Request request, CancellationToken cancel)
            {
                LastHeaders.Clear();
                LastStatusCode = 408;
                var response = await _dhr.RequestAsync(request, cancel);
                if (response != null)
                {
                    LastStatusCode = (int)response.StatusCode;
                    foreach (var h in response.Headers)
                    {
                        LastHeaders.TryAddWithoutValidation(h.Key, h.Value);
                    }
                }
                return response;
            }

            public void AddEventListener(string type, DomEventHandler callback = null, bool capture = false) => _dhr.AddEventListener(type, callback, capture);
            public void RemoveEventListener(string type, DomEventHandler callback = null, bool capture = false) => _dhr.RemoveEventListener(type, callback, capture);
            public void InvokeEventListener(Event ev) => _dhr.InvokeEventListener(ev);
            public bool Dispatch(Event ev) => _dhr.Dispatch(ev);
            public bool SupportsProtocol(string protocol) => _dhr.SupportsProtocol(protocol);
            public event DomEventHandler Requesting;
            public event DomEventHandler Requested;
        }
    }
}