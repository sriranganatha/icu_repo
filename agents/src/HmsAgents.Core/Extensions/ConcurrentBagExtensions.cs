using System.Collections.Concurrent;

namespace HmsAgents.Core.Extensions;

public static class ConcurrentBagExtensions
{
    public static void AddRange<T>(this ConcurrentBag<T> bag, IEnumerable<T> items)
    {
        foreach (var item in items)
            bag.Add(item);
    }

    /// <summary>
    /// Removes all items matching the predicate by swapping the bag contents.
    /// Not atomic — use only when no other thread is mutating the bag concurrently.
    /// </summary>
    public static void RemoveAll<T>(this ConcurrentBag<T> bag, Predicate<T> match)
    {
        var kept = bag.Where(item => !match(item)).ToList();
        while (bag.TryTake(out _)) { }
        foreach (var item in kept)
            bag.Add(item);
    }
}
