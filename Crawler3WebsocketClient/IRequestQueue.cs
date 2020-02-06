using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Crawler3WebsocketClient {
    public interface IAsyncRequestQueue {
        Task EnqueueAsync(long crawlId, IEnumerable<string> urls);
        Task DeleteAsync(long crawlId, IEnumerable<string> urls);
        Task DeleteAsync(long crawlId, string url) => DeleteAsync(crawlId, new[] { url });
        IAsyncEnumerable<string> DequeueAsync(long crawlId, int maxUrls, DateTimeOffset jobTimeout);
    }
    public interface IRequestQueue : IAsyncRequestQueue {
        void Enqueue(long crawlId, IEnumerable<string> urls) => EnqueueAsync(crawlId, urls).GetAwaiter().GetResult();
        IList<string> Dequeue(long crawlId, int maxUrls, DateTimeOffset jobTimeout) => DequeueAsync(crawlId, maxUrls, jobTimeout).AsList();
        void Delete(long crawlId, string url) => DeleteAsync(crawlId, url).GetAwaiter().GetResult();
        void Delete(long crawlId, IEnumerable<string> urls) => DeleteAsync(crawlId, urls).GetAwaiter().GetResult();
    }

    public static class AsyncEnumerableExtensions {
        public static IList<T> AsList<T>(this IAsyncEnumerable<T> ae) => AsListAsync(ae).GetAwaiter().GetResult();

        public static async Task<IList<T>> AsListAsync<T>(this IAsyncEnumerable<T> ae) {
            var l = new List<T>();
            await foreach (var i in ae) {
                l.Add(i);
            }

            return l;
        }
    }

    public class InMemoryRequestQueue : IRequestQueue {
        internal static readonly IDictionary<long, Queue<string>> Storage = new Dictionary<long, Queue<string>>();

        internal static readonly List<(string url, long crawlId, DateTimeOffset timeout)> Jobs =
            new List<(string url, long crawlId, DateTimeOffset timeout)>();

        internal static readonly SemaphoreSlim Sem = new SemaphoreSlim(1);

        private Queue<string> GetQueueByCrawlId(long crawlId) {
            if (!Storage.TryGetValue(crawlId, out var queue)) {
                queue = new Queue<string>();
                Storage[crawlId] = queue;
            }

            return queue;
        }

        private async Task Requeue() {
            IList<(string url, long crawlId, DateTimeOffset timeout)> timedOut;
            await Sem.WaitAsync();
            try {
                if(!Jobs.Any()) return;
                var now = DateTimeOffset.UtcNow;
                timedOut = Jobs.Where(j => j.timeout > now).ToList();
                foreach (var j in timedOut) {
                    Jobs.Remove(j);
                    GetQueueByCrawlId(j.crawlId).Enqueue(j.url);
                }
            }
            finally {
                Sem.Release();
            }

            foreach (var (url, crawlId, timeout) in timedOut) await EnqueueAsync(crawlId, new[] {url});
        }

        public async Task DeleteAsync(long crawlId, IEnumerable<string> urls) {
            await Sem.WaitAsync();

            try {
                foreach (var j in Jobs.Where(j => j.crawlId == crawlId && urls.Contains(j.url)).ToList()) {
                    Jobs.Remove(j);
                }
            }
            finally
            {
                Sem.Release();
            }
        }

        public async Task EnqueueAsync(long crawlId, IEnumerable<string> urls) {
            await Sem.WaitAsync();

            try {
                var queue = GetQueueByCrawlId(crawlId);

                foreach (var url in urls) queue.Enqueue(url);
            }
            finally {
                Sem.Release();
            }
        }

        public async IAsyncEnumerable<string> DequeueAsync(long crawlId, int maxUrls, DateTimeOffset jobTimeout) {
            await Requeue();
            await Sem.WaitAsync();
            try {
                var queue = GetQueueByCrawlId(crawlId);

                for (var i = 0; i < maxUrls; i++)
                    if (queue.TryDequeue(out var url)) {
                        Jobs.Add((url, crawlId, jobTimeout));
                        yield return url;
                    }
            }
            finally {
                Sem.Release();
            }
        }
    }
}