using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Crawler3WebsocketClient.Tests
{
    public class RequestQueueTests
    {
        [Test]
        public void InMemorySyncQueue() {
            IRequestQueue rq = new InMemoryRequestQueue();
            rq.Enqueue(1, new []{"j1","j2","j3"});
            var d0 = rq.Dequeue(1, 1, DateTimeOffset.UtcNow.AddSeconds(1));
            rq.Delete(1, d0);
            rq.Dequeue(1, 1, DateTimeOffset.UtcNow.AddSeconds(-1));
            rq.Enqueue(1, new []{"j4", "j5"});

            var d1 = rq.Dequeue(1, 10, DateTimeOffset.UtcNow.AddSeconds(10));
            Assert.AreEqual(3, d1.Count);
        }

        [Test]
        public async Task SqliteFile() {
            var tempFile = Path.GetTempFileName();
            File.Delete(tempFile);
            var databasePath = $"{tempFile}.db";
            IRequestQueue rq = new SqliteRequestQueue.SqliteRequestQueue(databasePath);
            await RqTestDefaultWorkflow(rq, 1);
            File.Delete(databasePath);
        }

        [Test]
        public async Task SqliteInMemory()
        {
            IRequestQueue rq = new SqliteRequestQueue.SqliteRequestQueue(":memory:");
            await RqTestDefaultWorkflow(rq, 1);
        }

        [Test]
        public async Task InMemory()
        {
            IRequestQueue rq = new InMemoryRequestQueue();
            await RqTestDefaultWorkflow(rq, 1);
        }

        private async Task RqTestDefaultWorkflow(IRequestQueue rq, long crawlId) {

            var sw = new Stopwatch();
            sw.Start();

            // Enqueue 3 Urls
            await rq.EnqueueAsync(crawlId, new[] { "j1", "j2", "j3" });

            // Dequeue and finish 1
            var d0 = new List<string>();
            await foreach (var d in rq.DequeueAsync(1, 1, DateTimeOffset.UtcNow.AddSeconds(1)))
            {
                TestContext.WriteLine($"Processing... {d}");
                await rq.DeleteAsync(crawlId, d);
            }

            // Dequeue 1 and fail to finish in time
            rq.Dequeue(crawlId, 1, DateTimeOffset.UtcNow.AddSeconds(-1));

            // Enqueue 2 more
            rq.Enqueue(crawlId, new[] { "j4", "j5" });

            // Dequeue up to 10
            var d1 = rq.Dequeue(crawlId, 10, DateTimeOffset.UtcNow.AddSeconds(10));

            // 3 should be left
            Assert.AreEqual(3, d1.Count);

            // Queue should be empty
            var d2 = rq.Dequeue(crawlId, 10, DateTimeOffset.UtcNow.AddSeconds(10));
            Assert.AreEqual(0, d2.Count);

            sw.Stop();
            TestContext.WriteLine($"ElapsedMilliseconds: {sw.ElapsedMilliseconds}");
        }
    }
}
