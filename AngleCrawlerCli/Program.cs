using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AngleCrawler;
using Microsoft.Extensions.Configuration;

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

            //var baseUrl = "https://kraftner.com/";
            //var baseUrl = "https://www.acolono.com/";
            //var baseUrl = "https://orf.at/";
            var baseUrl = "https://www.ichkoche.at/";
            //var baseUrl = "https://www.ichkoche.at/facebook-login";
            //var baseUrl = "https://failblog.cheezburger.com/";
            //var baseUrl = "https://ld.m.887.at/p";
            //var baseUrl = "https://endlq9qkj597t.x.pipedream.net/";
            var config = new CrawlerConfig {
                UrlFilter = $"{baseUrl}[.*]",
                //UrlFilter = "https://[[^/]*][\\.?]orf.at/[.*]",
                ExcludeFilters = {
                    "[.*]//[[^/]+]/login?return=[.*]", 
                    "[.*]//[[^/]+]/facebook-login", 
                    "[.*]//[[^/]+]/print/", 
                    "[.*]//[[^/]+]/[.*]recipe_pdf[.*]",
                    "[.*]//[[^/]+]/recipe/[.*]/pdf",
                },
                MaxConcurrency = 2,
                MaxRequestsPerCrawl = 655_360,
            };
            var cc = new CookieContainer();
            using var handler = new HttpClientHandler {
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true,
                CookieContainer = cc,
            };
            using var httpClient = new HttpClient(handler);
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9");
            httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9,de;q=0.8,de-AT;q=0.7");
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/80.0.3987.87 Safari/537.36 Edg/80.0.361.50");

            var requester = new HttpClientConcurrentCrawlerRequester(httpClient);
            //var requester = new ZenscrapeConcurrentCrawlerRequester(httpClient, GetFromConfig("ZenscrapeApiKey"));
            //var requester = new PrerenderCloudConcurrentCrawlerRequester(httpClient);
            //var requester = new RendertronConcurrentCrawlerRequester(httpClient);
            //var requester = new ProxyCrawlConcurrentCrawlerRequester(httpClient, GetFromConfig("ProxyCrawlApiKey"));

            //var requestQueue = new ChannelRequestQueue<RequestUrl>();
            var requestQueue = new Utf8ChannelRequestQueue<RequestUrl>();
            //var requestQueue = new LockedRequestQueue();
            
            using var crawler = new Crawler(config, requester, requestQueue, cts.Token);
            var consumerTask = ConsumeCrawlerResultsAsync(crawler.ResultsChannelReader);
            var crawlerTask = crawler.CrawlAsync();
            await crawler.EnqueueAsync(baseUrl);
            crawler.OnStatus += OnStatusAction;
            Sw.Start();
            await Task.WhenAll(consumerTask, crawlerTask);
        }

        private static long _peakAllocatedBytes;

        private static void OnStatusAction((long channelSize, long activeWorkers, long requestCount) args) {
            TimeSpan? tr;
            try {
                tr = (Sw.Elapsed / args.requestCount) * args.channelSize;
            }
            catch {
                tr = null;
            }
            var ab = GC.GetTotalMemory(true);
            if (ab > _peakAllocatedBytes) _peakAllocatedBytes = ab;
            Console.WriteLine($"q:{args.channelSize}, w:{args.activeWorkers}, cnt:{args.requestCount} ab:{FormatBytes(ab)} tr:{tr}");
        }

        private static readonly Stopwatch Sw = new Stopwatch();

        static async Task ConsumeCrawlerResultsAsync(ChannelReader<CrawlerResult> results) {
            var cnt = 0;
            await foreach (var r in results.ReadAllAsync()) {
                //if (Debugger.IsAttached && node.Url.Contains("facebook.com")) {
                //    Debugger.Break();
                //}
                var redirect = r.Edges.FirstOrDefault(e => e.Relation == "redirect");
                if (redirect != null) {
                    Console.WriteLine($"30? {redirect.Parent} -> {redirect.Child}");
                }
                Console.WriteLine($"{r.Node.Status:000}{(r.Node.External ? " X":"")} {r.Node.Url} edges={r.Edges.Count} time={r.Node.LoadTimeSeconds:0.00}s");
                if(!string.IsNullOrEmpty(r.Node.Error)) Console.WriteLine($"ERR {r.Node.Error}");

                if(cnt % 200 == 0) PrintMemoryStatistics();

                if (Directory.Exists("store")) {
                    var fn = $"store/{cnt:x8}.json.gz";
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(r,
                        new JsonSerializerOptions {IgnoreNullValues = true, WriteIndented = true});
                    await using var file = File.OpenWrite(fn);
                    await using var stream = new GZipStream(file, CompressionMode.Compress);
                    await stream.WriteAsync(bytes);
                }

                cnt++;
            }
            Sw.Stop();

            Console.WriteLine($"Pages Crawled: {cnt}");
            Console.WriteLine($"Elapsed Time: {Sw.Elapsed}");
            Console.WriteLine($"Pages Crawled/Minute: {cnt / Sw.Elapsed.TotalMinutes}");
            PrintMemoryStatistics();
        }

        static void PrintMemoryStatistics() {
            var proc = Process.GetCurrentProcess();
            Console.WriteLine($"Process.PeakWorkingSet: {FormatBytes(proc.PeakWorkingSet64)}");
            if(proc.PeakPagedMemorySize64 > 0) Console.WriteLine($"Process.PeakPagedMemorySize: {FormatBytes(proc.PeakPagedMemorySize64)}");
            Console.WriteLine($"GC.peakAllocatedBytes: {FormatBytes(_peakAllocatedBytes)}");
            Console.WriteLine($"GC.AllAllocations: {FormatBytes(GC.GetTotalAllocatedBytes(true))}");
        }

        static string GetFromConfig(string key) =>
            new ConfigurationBuilder()
                .AddUserSecrets<Program>()
                .Build()[key];

        private static string FormatBytes(long bytes) {
            var len = Convert.ToDecimal(bytes);
            string[] sizes = {"B", "KB", "MB", "GB", "TB"};
            var order = 0;
            while (len >= 1024 && order < sizes.Length - 1) {
                order++;
                len /= 1024;
            }

            return $"{len:0.###} {sizes[order]}";
        }
    }
}
