using System;
using System.Collections.Generic;
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

            var baseUrl = "https://www.acolono.com/";
            var config = new CrawlerConfig {
                CancellationToken = cts.Token, // TODO: make it work
                UrlFilter = $"[^]{baseUrl}[.*]",
                MaxConcurrency = Environment.ProcessorCount,
                //MaxRequestsPerCrawl = 10, // TODO: make it work
            };

            using var crawler = new Crawler(config);
            var consumerTask = ConsumeCrawlerResultsAsync(crawler.ResultsChannel.Reader);
            var crawlerTask = crawler.CrawlAsync();
            await crawler.EnqueueAsync(baseUrl);
            crawler.OnStatus += OnStatusAction;

            await Task.WhenAll(consumerTask, crawlerTask);

        }

        private static void OnStatusAction((long channelSize, long activeWorkers, long requestCount) args) {
            Console.WriteLine($"cs:{args.channelSize}, w:{args.activeWorkers}, cnt:{args.requestCount}");
        }

        static async Task ConsumeCrawlerResultsAsync(ChannelReader<(CrawlerNode node, IList<CrawlerEdge> edges)> results) {
            var cnt = 0;
            await foreach (var (node, edges) in results.ReadAllAsync()) {
                Console.WriteLine($"{node.Url} ({edges.Count})");
                //node.Html = null;
                //node.Headers = null;
                //var js = System.Text.Json.JsonSerializer.Serialize(node, new JsonSerializerOptions{WriteIndented = true,  IgnoreNullValues = true});
                //Console.WriteLine(js);
                cnt++;
            }

            Console.WriteLine($"Pages Crawled: {cnt}");
        }
    }
}
