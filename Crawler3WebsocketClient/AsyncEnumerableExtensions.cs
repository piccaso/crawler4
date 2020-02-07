using System.Collections.Generic;
using System.Threading.Tasks;

namespace Crawler3WebsocketClient {
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
}