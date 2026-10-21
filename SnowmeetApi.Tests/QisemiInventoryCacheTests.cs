using SnowmeetApi.Services.Qisemi;

namespace SnowmeetApi.Tests;

public class QisemiInventoryCacheTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task ConcurrentReadersShareBuildAndCancelledWaiterDoesNotCancelItsOwner()
    {
        var cache = new QisemiInventoryCache();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(string, long, bool)> Build() { entered.TrySetResult(); await release.Task; return ("complete", 100, true); }
        var owner = cache.GetOrCreateAsync("query", false, Build, default); await entered.Task;
        using var cancel = new CancellationTokenSource();
        var cancelled = cache.GetOrCreateAsync("query", false, Build, cancel.Token); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        var readers = Enumerable.Range(0, 8).Select(_ => cache.GetOrCreateAsync("query", false, Build, default)).ToArray();
        release.SetResult(); Assert.Equal("complete", await owner);
        Assert.All(await Task.WhenAll(readers), value => Assert.Equal("complete", value));
        Assert.Equal(1, cache.BuildCount); Assert.Equal(8, cache.HitCount);
    }

    [Fact]
    public async Task AbsoluteExpiryRefreshAndLruLimitsAreEnforced()
    {
        var clock = new Clock(); var cache = new QisemiInventoryCache(clock, TimeSpan.FromMinutes(5), 20, 2);
        Task<string> Read(string key, bool refresh = false) => cache.GetOrCreateAsync(key, refresh,
            () => Task.FromResult((Guid.NewGuid().ToString(), 10L, true)), default);
        string a = await Read("a"); string b = await Read("b");
        Assert.Equal(a, await Read("a")); await Read("c"); // b is least recently used.
        Assert.NotEqual(b, await Read("b")); Assert.Equal(2, cache.EntryCount); Assert.Equal(20, cache.EstimatedBytes);
        string before = await Read("b"); Assert.NotEqual(before, await Read("b", true));
        string fresh = await Read("b"); clock.Now += TimeSpan.FromMinutes(5);
        Assert.NotEqual(fresh, await Read("b")); Assert.Equal(1, cache.EntryCount);
    }

    [Fact]
    public async Task FailedUnstableAndOversizeResultsAreNeverCached()
    {
        var cache = new QisemiInventoryCache(byteLimit: 10);
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrCreateAsync<string>("failed", false,
            () => throw new InvalidOperationException("failed build"), default));
        foreach (var (key, bytes, stable) in new[] { ("failed", 1L, true), ("unstable", 1L, false), ("oversize", 11L, true) })
            Assert.Equal(key, await cache.GetOrCreateAsync(key, false, () => Task.FromResult((key, bytes, stable)), default));
        Assert.Equal(1, cache.EntryCount); Assert.Equal(1, cache.EstimatedBytes);
        Assert.Equal(4, cache.BuildCount);
    }
}
