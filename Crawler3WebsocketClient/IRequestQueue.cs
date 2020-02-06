using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Crawler3WebsocketClient {
    public interface IRequestQueue {
        Task EnqueueAsync(long crawlId, IEnumerable<string> urls);
        IAsyncEnumerable<string> DequeueAsync(long crawlId, int maxUrls);

        public void Enqueue(long crawlId, IEnumerable<string> urls) {
            EnqueueAsync(crawlId, urls).GetAwaiter().GetResult();
        }
    }

    public class InMemoryRequestQueue : IRequestQueue {
        internal static readonly IDictionary<long, Queue<string>> Storage = new Dictionary<long, Queue<string>>();
        internal static readonly SemaphoreSlim Sem = new SemaphoreSlim(1);

        public async Task EnqueueAsync(long crawlId, IEnumerable<string> urls) {
            await Sem.WaitAsync();

            try {
                if (!Storage.TryGetValue(crawlId, out var queue)) {
                    queue = new Queue<string>();
                    Storage[crawlId] = queue;
                }

                foreach (var url in urls) queue.Enqueue(url);
            }
            finally {
                Sem.Release();
            }
        }

        public async IAsyncEnumerable<string> DequeueAsync(long crawlId, int maxUrls) {
            await Sem.WaitAsync();
            try {
                if (Storage.TryGetValue(crawlId, out var queue))
                    for (var i = 0; i < maxUrls; i++)
                        if (queue.TryDequeue(out var url))
                            yield return url;
            }
            finally {
                Sem.Release();
            }
        }
    }
}