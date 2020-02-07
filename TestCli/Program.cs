using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Crawler3WebsocketClient;
using Crawler3WebsocketClient.Tests;

namespace TestCli {
    class Program {
        static readonly object Sync = new object();

        static void WriteLine(string str) => WriteLine((object)str);
        static void WriteLine(object str) {
            lock (Sync) {
                Console.WriteLine(str);
            }
        }

        static void Main(string[] args) {
            AsyncMain().GetAwaiter().GetResult();
        }

        static async Task AsyncMain() {

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) => {
                if (cts.IsCancellationRequested) return;
                cts.Cancel(false);
                e.Cancel = true;
            };

            var settings = new TestConfiguration();
            var logger = new LambdaLogger(WriteLine);
            
            using var db = new Db("test.db");
            //var baseUrl = "https://ld.m.887.at/p/";
            var baseUrl = "https://www.ichkoche.at/";
            //var baseUrl = "https://www.acolono.com/";
            //var baseUrl = "https://orf.at/";
            var crawlerConfig = new CrawlerConfig {
                CheckExternalLinks = false,
                FollowInternalLinks = false,
                //MaxConcurrency = 8,
                MaxRequestsPerCrawl = 1_000_000,
                TakeScreenShots = true,
                RequestQueue = {baseUrl},
                UrlFilter = $"[^]{baseUrl}[.*]",
            };
            var crawlId = db.NewCrawl(baseUrl, crawlerConfig);
            var eot = false;
            var nodesCount = 0L;
            while (!eot && !cts.Token.IsCancellationRequested) {
                try {
                    WriteLine($"{crawlId}: {baseUrl}");
                    //var purge = db.PurgeCrawl(crawlId);
                    //WriteLine("Purge Count: " + purge);
                    nodesCount = db.CountNodes(crawlId);

                    var edges = new List<CrawlerResponseEdge>();
                    var nodes = new List<CrawlerResponseNode>();

                    void FlushDb() {
                        var cnt = edges.Count * nodes.Count;
                        if(cnt < 1) return;
                        var sw = new Stopwatch();
                        sw.Start();
                        db.StoreEdges(crawlId, edges);
                        edges.Clear();
                        db.StoreNodes(crawlId, nodes);
                        nodes.Clear();
                        nodesCount = db.CountNodes(crawlId);
                        sw.Stop();
                        Console.WriteLine($"Stored {cnt} records in {sw.Elapsed.TotalSeconds:0.000}sec");
                    }

                    using var client = new WebsocketJsonClient(new Uri(settings.CrawlerWebsocketUrl), logger);
                    var crawlSw = new Stopwatch();
                    crawlSw.Start();

                    client.OnStatus += (s) => {
                        var avgPageSpeed = crawlSw.Elapsed.TotalSeconds / s.HandledRequestCount;
                        WriteLine($"pending: {s.PendingRequestCount}, handled: {s.HandledRequestCount}, nodes: {nodesCount}, buff: {client.JsonChannelSize}, avgSecondsPerPage:{avgPageSpeed:0.000}sec");
                        if(nodes.Count > 10) FlushDb();
                    };
                    client.OnEot += () => {
                        FlushDb();
                        WriteLine("Done");
                        eot = true;
                    };

                    client.OnNode += (n) => {
                        nodes.Add(n);
                        WriteLine($"LoadTime: {n.LoadTime:0.000}sec - {n.Url}");
                        nodesCount++;
                    };
                    client.OnEdges += (e) => {
                        edges.AddRange(e.Edges);
                    };

                    await client.SendAsync(crawlerConfig, cts.Token);
                    await client.ReceiveAllAsync(cancellationToken: cts.Token);
                    FlushDb();
                }
                catch (Exception e) {
                    //throw;
                    WriteLine(e);
                    await Task.Delay(5000, cts.Token);
                    //crawlerConfig.MaxConcurrency = 1;
                }
            }
        }
    }
}
