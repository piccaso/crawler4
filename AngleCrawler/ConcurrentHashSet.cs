using System;
using System.Collections.Generic;
using System.Threading;

namespace AngleCrawler {

    public interface IConcurrentUrlStore : IDisposable {
        bool Add(string url);
        int Count { get; }
    }

    public class ConcurrentUrlStore : IConcurrentUrlStore {
        private readonly ConcurrentHashSet<string> _hashSet = new ConcurrentHashSet<string>();
        public int Count => _hashSet.Count;
        public void Dispose() => _hashSet.Dispose();
        public bool Add(string url) => _hashSet.Add(url);
    }


    public class ConcurrentStruct<T> where T : struct {
        private readonly ReaderWriterLockSlim _rwLock = new ReaderWriterLockSlim();
        private T _value;

        public ConcurrentStruct() {}
        public ConcurrentStruct(T initialValue) {
            _value = initialValue;
        }

        public T Value {
            get {
                _rwLock.EnterReadLock();
                try { return _value; }
                finally {
                    if (_rwLock.IsReadLockHeld) _rwLock.ExitReadLock();
                }
            }
            set {
                _rwLock.EnterWriteLock();
                try { _value = value; }
                finally {
                    if(_rwLock.IsWriteLockHeld) _rwLock.ExitWriteLock();
                }
            }
        }
    }


    public class ConcurrentHashSet<T> : IDisposable {
        private readonly ReaderWriterLockSlim _rwLock = new ReaderWriterLockSlim();
        private readonly HashSet<T> _hashSet = new HashSet<T>();

        public bool Add(T item) {
            _rwLock.EnterWriteLock();

            try {
                return _hashSet.Add(item);
            }
            finally {
                if (_rwLock.IsWriteLockHeld) _rwLock.ExitWriteLock();
            }
        }

        public void Clear() {
            _rwLock.EnterWriteLock();

            try {
                _hashSet.Clear();
            }
            finally {
                if (_rwLock.IsWriteLockHeld) _rwLock.ExitWriteLock();
            }
        }

        public bool Contains(T item) {
            _rwLock.EnterReadLock();

            try {
                return _hashSet.Contains(item);
            }
            finally {
                if (_rwLock.IsReadLockHeld) _rwLock.ExitReadLock();
            }
        }


        public int Count {
            get {
                _rwLock.EnterReadLock();

                try {
                    return _hashSet.Count;
                }
                finally {
                    if (_rwLock.IsReadLockHeld) _rwLock.ExitReadLock();
                }
            }
        }

        public void Dispose() {
            _rwLock?.Dispose();
        }
    }
}