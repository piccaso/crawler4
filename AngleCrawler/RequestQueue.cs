using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AngleCrawler {
    public interface IRequestQueue<T> {
        Task<bool> EnqueueAsync(T requestUrl, CancellationToken cancellationToken);
        Task<(bool success, T item)> DequeueAsync(CancellationToken cancellationToken);
        Task<long> CountAsync(CancellationToken cancellationToken);
        Task<bool> CloseAsync(CancellationToken cancellationToken);
    }

    public class ChannelRequestQueue<T> : IRequestQueue<T> {
        private readonly Channel<T> _channel = Channel.CreateUnbounded<T>();
        private long _counter;

        public async Task<bool> EnqueueAsync(T requestUrl, CancellationToken cancellationToken) {
            try {
                await _channel.Writer.WriteAsync(requestUrl, cancellationToken);
                Interlocked.Increment(ref _counter);
                return true;
            }
            catch (ChannelClosedException) {
                return false;
            }
        }

        public async Task<(bool success, T item)> DequeueAsync(CancellationToken cancellationToken) {
            var item = default(T);
            while (await _channel.Reader.WaitToReadAsync(cancellationToken)) {
                if (!_channel.Reader.TryRead(out item)) continue;
                Interlocked.Decrement(ref _counter);
                return (true, item);
            }

            return (false, item);
        }

        public Task<long> CountAsync(CancellationToken cancellationToken) {
            var count = Interlocked.Read(ref _counter);
            return Task.FromResult(count);
        }

        public Task<bool> CloseAsync(CancellationToken cancellationToken) {
            var close = _channel.Writer.TryComplete();
            return Task.FromResult(close);
        }
    }

    public class LockedRequestQueue : IRequestQueue<RequestUrl> {
        private readonly Queue<RequestUrl> _queue = new Queue<RequestUrl>();
        private bool _closed = false;
        private readonly object _sync = new object();
        public Task<bool> EnqueueAsync(RequestUrl requestUrl, CancellationToken cancellationToken) {
            lock (_sync) {
                if (_closed) return Task.FromResult(false);
                _queue.Enqueue(requestUrl);
            }

            return Task.FromResult(true);
        }

        public async Task<(bool success, RequestUrl item)> DequeueAsync(CancellationToken cancellationToken) {
            while (!cancellationToken.IsCancellationRequested) {
                if (TryDequeue(out var item)) return (true, item);
                if (IsClosed()) break;
                await Task.Delay(250, cancellationToken);
            }
            return (false, default);
        }

        private bool IsClosed() {
            lock (_sync) {
                return _closed;
            }
        }

        private bool TryDequeue(out RequestUrl item) {
            lock (_sync) {
                return _queue.TryDequeue(out item);
            }
        }

        

        public Task<long> CountAsync(CancellationToken cancellationToken) {
            long count;
            lock (_sync) {
                count = _queue.Count;
            }

            return Task.FromResult(count);
        }

        public Task<bool> CloseAsync(CancellationToken cancellationToken) {
            lock (_sync) {
                _closed = true;
            }
            return Task.FromResult(true);
        }
    }
}