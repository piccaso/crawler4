using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Crawler3WebsocketClient.Tests {
    public class Tests {
        private LambdaLogger _logger;
        private Uri _serverUrl;
        [SetUp]
        public void Setup() {
            var conf = new TestConfiguration();

            _logger = new LambdaLogger(TestContext.WriteLine);
            _serverUrl = new Uri(conf["CrawlerWebsocketUrl"]);
        }

        [Test]
        public async Task Test1() {


            var client = new WebsocketJsonClient(_serverUrl, _logger);

            //client.OnEdge += (e) => {
            //    TestContext.WriteLine($"{e.Parent} >>[{e.Relation}]>> {e.Child}");
            //};
            client.OnNode += (e) => {
                TestContext.WriteLine($"{e.Status} {e.Title}");
            };

            await client.ConnectAsync();

            await client.SendAsync(new CrawlerConfig {
                CheckExternalLinks = false,
                FollowInternalLinks = false,
                MaxConcurrency = 2,
                MaxRequestsPerCrawl = 10_9999,
                RequestQueue = {
                    "https://expired.badssl.com/",
                    "https://html5test.com/",
                    "https://ld.m.887.at/stream",
                },
                UrlFilter = "[.*]"
            });


            var ex = await client.ReceiveAllAsync();
            Assert.IsNull(ex);
        }
    }
}