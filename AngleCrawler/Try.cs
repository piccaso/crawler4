using System;
using System.Threading;
using System.Threading.Tasks;

namespace AngleCrawler {
    public class Try {
        public static T Harder<T>(int howHard, Func<T> func, int delayBetween = 500) {
            while (true)
                try {
                    return func();
                }
                catch {
                    howHard--;
                    if (howHard > 0) {
                        Thread.Sleep(delayBetween);
                        continue;
                    }

                    throw;
                }
        }

        public static async Task<T> HarderAsync<T>(int howHard, Func<Task<T>> func, int delayBetween = 500, CancellationToken ct = default) {
            while (true)
                try {
                    return await func();
                }
                catch {
                    howHard--;
                    if (howHard > 0 && !ct.IsCancellationRequested) {
                        await Task.Delay(delayBetween, ct);
                        continue;
                    }

                    throw;
                }
        }
    }
}