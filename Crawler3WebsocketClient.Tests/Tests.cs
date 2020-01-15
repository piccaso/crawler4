using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Crawler3WebsocketClient.Tests {
    public class Tests {
        private TestLogger _logger;
        private Uri _serverUrl;
        [SetUp]
        public void Setup() {
            var conf = new TestConfiguration();

            _logger = new TestLogger(TestContext.WriteLine);
            _serverUrl = new Uri(conf["Url"]);
        }

        [Test]
        public async Task Test1() {


            var client = new WebsocketJsonClient(_serverUrl, _logger);

            await client.SendAsync(new CrawlerConfig {
                CheckExternalLinks = false,
                FollowInternalLinks = true,
                MaxConcurrency = 2,
                MaxRequestsPerCrawl = 999,
                RequestQueue = new List<string> {
                    "https://www.acolono.com/"
                },
                UrlFilter = "https://www.acolono.com/[.*]"
            });

            client.OnEdge += (e) => {
                TestContext.WriteLine($"{e.Parent} >>[{e.Relation}]>> {e.Child}");
            };
            client.OnNode += (e) => {
                TestContext.WriteLine($"{e.Status} {e.Title}");
            };

            await client.ConnectAsync();
            await client.ReceiveAllAsync();
        }
    }
}