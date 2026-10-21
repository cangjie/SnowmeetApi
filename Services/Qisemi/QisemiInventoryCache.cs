#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SnowmeetApi.Services.Qisemi;

// Process-local, bounded LRU. The payload is a complete search, never one page.
// No credentials or responses are persisted. Failed/cancelled builds are not retained.
public sealed class QisemiInventoryCache
{
    private sealed record Entry(object Value, long Bytes, DateTimeOffset Expires, LinkedListNode<string> Node);
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> lru = new();
    private readonly SemaphoreSlim[] builders = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly SemaphoreSlim buildSlots = new(2, 2);
    private readonly TimeProvider clock;
    private readonly TimeSpan lifetime;
    private readonly long byteLimit;
    private readonly int entryLimit;
    private long bytes, buildCount, hitCount;

    public QisemiInventoryCache(TimeProvider? clock = null, TimeSpan? lifetime = null,
        long byteLimit = 64L * 1024 * 1024, int entryLimit = 32)
    {
        this.clock = clock ?? TimeProvider.System;
        this.lifetime = lifetime ?? TimeSpan.FromMinutes(5);
        if (this.lifetime <= TimeSpan.Zero || byteLimit <= 0 || entryLimit <= 0) throw new ArgumentOutOfRangeException(nameof(lifetime));
        this.byteLimit = byteLimit; this.entryLimit = entryLimit;
    }

    public long BuildCount => Interlocked.Read(ref buildCount);
    public long HitCount => Interlocked.Read(ref hitCount);
    public int EntryCount { get { lock (gate) return entries.Count; } }
    public long EstimatedBytes { get { lock (gate) return bytes; } }

    private bool TryGet<T>(string key, out T value)
    {
        lock (gate)
        {
            foreach (string expired in entries.Where(x => x.Value.Expires <= clock.GetUtcNow()).Select(x => x.Key).ToArray()) Remove(expired);
            if (entries.TryGetValue(key, out var entry))
            {
                lru.Remove(entry.Node); lru.AddLast(entry.Node);
                value = (T)entry.Value; Interlocked.Increment(ref hitCount); return true;
            }
        }
        value = default!; return false;
    }

    private void Remove(string key)
    {
        if (entries.Remove(key, out var entry)) { bytes -= entry.Bytes; lru.Remove(entry.Node); }
    }

    public async Task<T> GetOrCreateAsync<T>(string key, bool refresh,
        Func<Task<(T Value, long Bytes, bool Cacheable)>> build, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!refresh && TryGet<T>(key, out var found)) return found;
        var builder = builders[(uint)StringComparer.Ordinal.GetHashCode(key) % (uint)builders.Length];
        await builder.WaitAsync(ct);
        try
        {
            if (refresh) { lock (gate) Remove(key); }
            else if (TryGet<T>(key, out found)) return found;
            // Bound transient build memory/SQL load too, across distinct searches.
            await buildSlots.WaitAsync(ct);
            try
            {
                Interlocked.Increment(ref buildCount);
                var result = await build(); ct.ThrowIfCancellationRequested();
                if (result.Cacheable && result.Bytes > 0 && result.Bytes <= byteLimit)
                {
                    lock (gate)
                    {
                        Remove(key);
                        while (entries.Count >= entryLimit || bytes + result.Bytes > byteLimit) Remove(lru.First!.Value);
                        var node = lru.AddLast(key);
                        entries.Add(key, new(result.Value!, result.Bytes, clock.GetUtcNow() + lifetime, node)); bytes += result.Bytes;
                    }
                }
                return result.Value;
            }
            finally { buildSlots.Release(); }
        }
        finally { builder.Release(); }
    }
}
