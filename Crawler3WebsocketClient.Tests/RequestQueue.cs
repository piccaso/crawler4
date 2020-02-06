using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Crawler3WebsocketClient.Tests
{
    public class RequestQueue
    {
        [Test]
        public async Task Rq1Async() {
            IRequestQueue rq = new InMemoryRequestQueue();
            await rq.EnqueueAsync(1, new []{"j1","j2","j3"});
            var d0 = rq.Dequeue(1, 1, DateTimeOffset.UtcNow.AddSeconds(1));
            rq.Delete(1, d0);
            rq.Dequeue(1, 1, DateTimeOffset.UtcNow.AddSeconds(-1));
            rq.Enqueue(1, new []{"j4", "j5"});

            var d1 = rq.Dequeue(1, 10, DateTimeOffset.UtcNow.AddSeconds(10));
            Assert.AreEqual(3, d1.Count);
        }

        [Test]
        public async Task Rq2Async()
        {
            IRequestQueue rq = new InMemoryRequestQueue();
            await rq.EnqueueAsync(1, new[] { "j1", "j2", "j3" });
            var d0 = new List<string>();
            await foreach (var d in rq.DequeueAsync(1, 1, DateTimeOffset.UtcNow.AddSeconds(1))) {
                d0.Add(d);
            }
            rq.Delete(1, d0);
            rq.Dequeue(1, 1, DateTimeOffset.UtcNow.AddSeconds(-1));
            rq.Enqueue(1, new[] { "j4", "j5" });

            var d1 = rq.Dequeue(1, 10, DateTimeOffset.UtcNow.AddSeconds(10));
            Assert.AreEqual(3, d1.Count);
        }
    }
}
