using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Crawler3WebsocketClient.Tests
{
    public class RequestQueueTests {
        private static long _crawlId;
        private long NextCrawlId() => Interlocked.Increment(ref _crawlId);

        [Test]
        public void InMemorySync() {
            IRequestQueue rq = new SqliteRequestQueue.SqliteRequestQueue(":memory:");
            var crawlId = NextCrawlId();
            rq.Enqueue(crawlId, new []{"j1","j2","j3"});
            var d0 = rq.Dequeue(crawlId, 1, DateTimeOffset.UtcNow.AddSeconds(1));
            rq.Delete(crawlId, d0);
            rq.Dequeue(crawlId, 1, DateTimeOffset.UtcNow.AddSeconds(-1));
            rq.Enqueue(crawlId, new []{"j4", "j5"});

            var d1 = rq.Dequeue(1, 10, DateTimeOffset.UtcNow.AddSeconds(10));
            Assert.AreEqual(4, d1.Count);
            rq.Delete(crawlId, d1);
        }

        private string GetTempDatabasePath() {
            var tempFile = Path.GetTempFileName();
            File.Delete(tempFile);
            return $"{tempFile}.db";
        }

        [Test]
        public async Task ConcurrentSqliteFileAsync() {
            var databasePath = GetTempDatabasePath();
            var tasks = Enumerable.Range(1, 3)
                .Select(i => RqTestDefaultWorkflow(new SqliteRequestQueue.SqliteRequestQueue(databasePath), NextCrawlId())).ToList();
            foreach (var task in tasks) {
                await task;
            }
        }

        [Test]
        public async Task SqliteFileAsync() {
            var databasePath = GetTempDatabasePath();
            IRequestQueue rq = new SqliteRequestQueue.SqliteRequestQueue(databasePath);
            await RqTestDefaultWorkflow(rq, NextCrawlId());
            try { File.Delete(databasePath); }
            catch {
                TestContext.WriteLine($"Cant delete {databasePath}");
            }
            
        }

        [Test]
        public async Task SqliteInMemoryAsync()
        {
            IRequestQueue rq = new SqliteRequestQueue.SqliteRequestQueue(":memory:");
            await RqTestDefaultWorkflow(rq, NextCrawlId());
        }

        private async Task RqTestDefaultWorkflow(IRequestQueue rq, long crawlId) {

            TestContext.WriteLine($"CrawlId = {crawlId}");

            var sw = new Stopwatch();
            sw.Start();

            // Enqueue 3 Urls
            await rq.EnqueueAsync(crawlId, new[] { "j1", "j2", "j3" });

            //Dequeue and finish 1
            await foreach (var d in rq.DequeueAsync(crawlId, 1, DateTimeOffset.UtcNow.AddSeconds(10)))
            {
                TestContext.WriteLine($"Processing... {d}");
                await Task.Delay(200);
                await rq.DeleteAsync(crawlId, d);
            }

            // Dequeue 1 and fail to finish in time
            await foreach (var d in rq.DequeueAsync(crawlId, 1, DateTimeOffset.UtcNow.AddSeconds(-1))) {
                TestContext.WriteLine($"Failing... {d}");
                await Task.Delay(200);
            }

            // Enqueue 2 more
            rq.Enqueue(crawlId, new[] { "j4", "j5" });

            // Dequeue up to 10
            var d1 = rq.Dequeue(crawlId, 10, DateTimeOffset.UtcNow.AddSeconds(10));

            foreach (var d in d1) {
                TestContext.WriteLine($"Processing... {d}");
            }
            rq.Delete(crawlId, d1);

            // that should hav been 3 from the queue and 1 from the failed jobs
            Assert.AreEqual(4, d1.Count);
            

            // Queue should be empty
            var d2 = rq.Dequeue(crawlId, 10, DateTimeOffset.UtcNow.AddSeconds(10));
            Assert.AreEqual(0, d2.Count, message: $"Unexpected: {string.Join(",", d2)}");

            sw.Stop();
            TestContext.WriteLine($"ElapsedMilliseconds: {sw.ElapsedMilliseconds}");
        }
    }
}
