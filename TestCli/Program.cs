using System;
using System.Text;
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
            var settings = new TestConfiguration();
            var logger = new LambdaLogger(null, WriteLine, WriteLine);
            using var client = new WebsocketJsonClient(new Uri(settings.CrawlerWebsocketUrl), logger);
            using var db = new Db("test.db");
            var baseUrl = "https://ld.m.887.at/p/";
            var crawlId = db.NewCrawl(baseUrl);

            WriteLine($"{crawlId}: {baseUrl}");

            client.OnStatus += (s) => {
                WriteLine($"pending: {s.PendingRequestCount}, handled: {s.HandledRequestCount}");
            };
            client.OnEot += () => { WriteLine("Done"); };

            client.OnNode += (n) => {
                db.StoreNode(crawlId, n); 
                WriteLine($"N {n.Url}");
            };
            //client.OnEdge += (e) => {
            //    db.StoreEdge(crawlId, e); 
            //    WriteLine($"E {e.Parent} -> {e.Child}");
            //};
            
            
            await client.SendAsync(new CrawlerConfig {
                CheckExternalLinks = false,
                FollowInternalLinks = true,
                MaxConcurrency = 2,
                MaxRequestsPerCrawl = 10,
                TakeScreenshots = false,
                RequestQueue = {baseUrl},
                UrlFilter = $"{baseUrl}[.*]",
            });

            await client.ReceiveAllAsync();
        }
    }
}
