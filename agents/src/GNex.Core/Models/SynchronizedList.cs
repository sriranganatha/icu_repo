using System.Collections;

namespace GNex.Core.Models;

/// <summary>
/// Thread-safe list wrapper using a ReaderWriterLockSlim for concurrent read/write access.
/// Parallel agents can safely iterate, add, and mutate items without corrupting the collection.
/// </summary>
public sealed class SynchronizedList<T> : IList<T>, IReadOnlyList<T>
{
    private readonly List<T> _inner;
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);

    public SynchronizedList() => _inner = [];
    public SynchronizedList(IEnumerable<T> items) => _inner = [.. items];

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try { return _inner.Count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    public bool IsReadOnly => false;

    public T this[int index]
    {
        get
        {
            _lock.EnterReadLock();
            try { return _inner[index]; }
            finally { _lock.ExitReadLock(); }
        }
        set
        {
            _lock.EnterWriteLock();
            try { _inner[index] = value; }
            finally { _lock.ExitWriteLock(); }
        }
    }

    public void Add(T item)
    {
        _lock.EnterWriteLock();
        try { _inner.Add(item); }
        finally { _lock.ExitWriteLock(); }
    }

    public bool Remove(T item)
    {
        _lock.EnterWriteLock();
        try { return _inner.Remove(item); }
        finally { _lock.ExitWriteLock(); }
    }

    public void Clear()
    {
        _lock.EnterWriteLock();
        try { _inner.Clear(); }
        finally { _lock.ExitWriteLock(); }
    }

    public bool Contains(T item)
    {
        _lock.EnterReadLock();
        try { return _inner.Contains(item); }
        finally { _lock.ExitReadLock(); }
    }

    public int IndexOf(T item)
    {
        _lock.EnterReadLock();
        try { return _inner.IndexOf(item); }
        finally { _lock.ExitReadLock(); }
    }

    public void Insert(int index, T item)
    {
        _lock.EnterWriteLock();
        try { _inner.Insert(index, item); }
        finally { _lock.ExitWriteLock(); }
    }

    public void RemoveAt(int index)
    {
        _lock.EnterWriteLock();
        try { _inner.RemoveAt(index); }
        finally { _lock.ExitWriteLock(); }
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        _lock.EnterReadLock();
        try { _inner.CopyTo(array, arrayIndex); }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>Returns a point-in-time snapshot safe for iteration without holding the lock.</summary>
    public List<T> Snapshot()
    {
        _lock.EnterReadLock();
        try { return [.. _inner]; }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>Thread-safe LINQ-friendly enumerator that works on a snapshot.</summary>
    public IEnumerator<T> GetEnumerator() => Snapshot().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
