using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Crawler3WebsocketClient;

namespace SqliteRequestQueue {
    public class SqliteRequestQueue : IRequestQueue {
        private readonly RequestQueueDb _db;

        public SqliteRequestQueue(string databasePath) {
            _db = new RequestQueueDb(databasePath);
            _db.CreateTablesAsync().GetAwaiter().GetResult();
        }

        public Task EnqueueAsync(long crawlId, IEnumerable<string> urls) => _db.EnqueueAsync(crawlId, urls);

        public async Task DeleteAsync(long crawlId, IEnumerable<string> urls) {
            foreach (var url in urls) {
                await _db.DeleteJobAsync(crawlId, url);
            }
        }

        public async IAsyncEnumerable<string> DequeueAsync(long crawlId, int maxUrls, DateTimeOffset jobTimeout) {
            await _db.RequeueTimedOutJobsAsync(crawlId);
            var urls = new List<string>();
            await foreach (var url in _db.DequeueAsync(crawlId, maxUrls)) {
                urls.Add(url);
            }

            await _db.InsertJobsAsync(crawlId, urls, jobTimeout);
            foreach (var url in urls) {
                yield return url;
            }
        }
    }
}