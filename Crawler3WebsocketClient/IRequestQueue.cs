using System;
using System.Collections.Generic;
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
}