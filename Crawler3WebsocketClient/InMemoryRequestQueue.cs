using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Crawler3WebsocketClient {
    public class InMemoryRequestQueue : IRequestQueue {
        private static readonly IDictionary<long, Queue<string>> Storage = new Dictionary<long, Queue<string>>();

        private static readonly List<(string url, long crawlId, DateTimeOffset timeout)> Jobs =
            new List<(string url, long crawlId, DateTimeOffset timeout)>();

        private static readonly object Sync = new object();

        private Queue<string> GetQueueByCrawlId(long crawlId) {
            lock (Sync) {
                if (!Storage.TryGetValue(crawlId, out var queue)) {
                    queue = new Queue<string>();
                    Storage[crawlId] = queue;
                }

                return queue;
            }
        }

        private void Requeue() {
            lock (Sync) {
                if (!Jobs.Any()) return;
                var now = DateTimeOffset.UtcNow;
                var timedOut = Jobs.Where(j => j.timeout < now).ToList();
                foreach (var j in timedOut) {
                    Jobs.Remove(j);
                    GetQueueByCrawlId(j.crawlId).Enqueue(j.url);
                }

                foreach (var (url, crawlId, timeout) in timedOut) Enqueue(crawlId, new[] {url});
            }
        }

        public void Delete(long crawlId, IEnumerable<string> urls) {
            lock (Sync) {
                foreach (var j in Jobs.Where(j => j.crawlId == crawlId && urls.Contains(j.url)).ToList())
                    Jobs.Remove(j);
            }
        }

        public void Enqueue(long crawlId, IEnumerable<string> urls) {
            lock (Sync) {
                var queue = GetQueueByCrawlId(crawlId);
                foreach (var url in urls) queue.Enqueue(url);
            }
        }


        public IList<string> Dequeue(long crawlId, int maxUrls, DateTimeOffset jobTimeout) {
            Requeue();
            lock (Sync) {
                var urls = new List<string>();
                var queue = GetQueueByCrawlId(crawlId);

                for (var i = 0; i < maxUrls; i++)
                    if (queue.TryDequeue(out var url)) {
                        Jobs.Add((url, crawlId, jobTimeout));
                        urls.Add(url);
                    }

                return urls;
            }
        }

        #region Async wrappers

        public Task DeleteAsync(long crawlId, IEnumerable<string> urls) {
            Delete(crawlId, urls);
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(long crawlId, IEnumerable<string> urls) {
            Enqueue(crawlId, urls);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<string> DequeueAsync(long crawlId, int maxUrls, DateTimeOffset jobTimeout) {
            var urls = await Task.Run(() => Dequeue(crawlId, maxUrls, jobTimeout));
            foreach (var url in urls) yield return url;
        }

        #endregion
    }
}