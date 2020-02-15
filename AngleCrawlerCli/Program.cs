using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AngleCrawler;

namespace AngleCrawlerCli
{
    class Program
    {
        static async Task Main(string[] args) {
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) => {
                if (cts.IsCancellationRequested) return;
                cts.Cancel(false);
                e.Cancel = true;
            };

            //var baseUrl = "https://www.acolono.com/";
            //var baseUrl = "https://orf.at/";
            var baseUrl = "https://www.ichkoche.at/";
            //var baseUrl = "https://www.ichkoche.at/facebook-login";
            //var baseUrl = "https://failblog.cheezburger.com/";
            //var baseUrl = "https://ld.m.887.at/p";
            //var baseUrl = "https://endlq9qkj597t.x.pipedream.net/";
            var config = new CrawlerConfig {
                CancellationToken = cts.Token,
                UrlFilter = $"[^]{baseUrl}[.*]",
                //UrlFilter = "https://[[^/]*][\\.?]orf.at/[.*]",
                MaxConcurrency = Environment.ProcessorCount * 2,
                MaxRequestsPerCrawl = 100,
                RequestHeaders = {
                    {"accept","text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9"},
                    {"accept-language", "en-US,en;q=0.9,de;q=0.8,de-AT;q=0.7"},
                    {"user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/80.0.3987.87 Safari/537.36 Edg/80.0.361.50" },
                }
            };
            using var handler = new HttpClientHandler {
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true,
                CookieContainer = new CookieContainer()
            };

            using var httpClient = new HttpClient(handler);
            using var crawler = new Crawler(config, new RendertronConcurrentCrawlerRequester(httpClient));
            var consumerTask = ConsumeCrawlerResultsAsync(crawler.ResultsChannel.Reader);
            var crawlerTask = crawler.CrawlAsync();
            await crawler.EnqueueAsync(baseUrl);
            crawler.OnStatus += OnStatusAction;

            await Task.WhenAll(consumerTask, crawlerTask);

        }

        private static void OnStatusAction((long channelSize, long activeWorkers, long requestCount) args) {
            Console.WriteLine($"q:{args.channelSize}, w:{args.activeWorkers}, cnt:{args.requestCount}");
        }

        static async Task ConsumeCrawlerResultsAsync(ChannelReader<(CrawlerNode node, IList<CrawlerEdge> edges)> results) {
            var cnt = 0;
            await foreach (var (node, edges) in results.ReadAllAsync()) {
                if (Debugger.IsAttached && node.Url.Contains("facebook.com")) {
                    Debugger.Break();
                }
                var redirect = edges.FirstOrDefault(e => e.Relation == "redirect");
                if (redirect != null) {
                    Console.WriteLine($"30X {redirect.Parent} -> {redirect.Child}");
                }
                Console.WriteLine($"{node.Status:000}{(node.External ? " X":"")} {node.Url} edges={edges.Count} time={node.LoadTimeSeconds:0.0}s");
                cnt++;
            }

            Console.WriteLine($"Pages Crawled: {cnt}");
        }
    }
}
