using System.Collections.Concurrent;
using System.Net;
using Battleship.Server.RateLimiting;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Battleship.Server.Tests;

public sealed class IpRateLimiterTests
{
    [Fact]
    public void TryAcquire_WithinPermits_ReturnsTrue()
    {
        var limiter = new IpRateLimiter();
        Assert.True(limiter.TryAcquire("create", "a", 2));
        Assert.True(limiter.TryAcquire("create", "a", 2));
    }

    [Fact]
    public void TryAcquire_ExceedsPermits_ReturnsFalse()
    {
        var limiter = new IpRateLimiter();
        Assert.True(limiter.TryAcquire("create", "a", 1));
        Assert.False(limiter.TryAcquire("create", "a", 1));
    }

    [Fact]
    public void TryAcquire_AfterOneMinute_WindowResets()
    {
        var limiter = new IpRateLimiter();
        Assert.True(limiter.TryAcquire("create", "a", 1));
        Assert.False(limiter.TryAcquire("create", "a", 1));
        SetWindow(limiter, "create:a", startedAt: DateTimeOffset.UtcNow.AddSeconds(-61));
        Assert.True(limiter.TryAcquire("create", "a", 1));
    }

    [Fact]
    public void TryAcquire_ScopesAreIndependent()
    {
        var limiter = new IpRateLimiter();
        Assert.True(limiter.TryAcquire("create", "a", 1));
        Assert.True(limiter.TryAcquire("connect", "a", 1));
    }

    [Fact]
    public void TryAcquire_KeysAreIndependent()
    {
        var limiter = new IpRateLimiter();
        Assert.True(limiter.TryAcquire("create", "a", 1));
        Assert.True(limiter.TryAcquire("create", "b", 1));
    }

    [Fact]
    public void TryAcquire_ZeroOrNegativePermits_Denied()
    {
        var limiter = new IpRateLimiter();
        Assert.False(limiter.TryAcquire("create", "a", 0));
        Assert.False(limiter.TryAcquire("create", "a", -1));
    }

    [Fact]
    public void TryAcquire_Concurrent_NeverExceedsPermits()
    {
        var limiter = new IpRateLimiter();
        var allowed = 0;
        Parallel.For(0, 100, _ =>
        {
            if (limiter.TryAcquire("create", "a", 10))
            {
                Interlocked.Increment(ref allowed);
            }
        });
        Assert.Equal(10, allowed);
    }

    [Fact]
    public void Cleanup_RemovesStaleWindows()
    {
        var limiter = new IpRateLimiter();
        Assert.True(limiter.TryAcquire("create", "a", 1));
        SetWindow(limiter, "create:a", lastSeen: DateTimeOffset.UtcNow.AddMinutes(-11));
        limiter.Cleanup(TimeSpan.FromMinutes(10));
        Assert.True(limiter.TryAcquire("create", "a", 1));
    }

    [Fact]
    public void Cleanup_KeepsRecentWindows()
    {
        var limiter = new IpRateLimiter();
        Assert.True(limiter.TryAcquire("create", "a", 1));
        limiter.Cleanup(TimeSpan.FromMinutes(10));
        Assert.False(limiter.TryAcquire("create", "a", 1));
    }

    [Fact]
    public void ClientKey_UsesRemoteIpAddress()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
        Assert.Equal("10.1.2.3", IpRateLimiter.ClientKey(context));
    }

    [Fact]
    public void ClientKey_NullRemoteIp_ReturnsUnknown()
    {
        Assert.Equal("unknown", IpRateLimiter.ClientKey(new DefaultHttpContext()));
    }

    [Fact]
    public void ClientKey_IgnoresXForwardedForHeader()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
        context.Request.Headers["X-Forwarded-For"] = "1.1.1.1";
        Assert.Equal("10.1.2.3", IpRateLimiter.ClientKey(context));
    }

    private static void SetWindow(IpRateLimiter limiter, string key, DateTimeOffset? startedAt = null, DateTimeOffset? lastSeen = null)
    {
        var field = typeof(IpRateLimiter).GetField("_windows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var windows = (System.Collections.IEnumerable)field.GetValue(limiter)!;
        foreach (var entry in windows)
        {
            var entryType = entry.GetType();
            if ((string)entryType.GetProperty("Key")!.GetValue(entry)! != key)
            {
                continue;
            }

            var window = entryType.GetProperty("Value")!.GetValue(entry)!;
            var type = window.GetType();
            if (startedAt is not null)
            {
                type.GetProperty("StartedAt")!.SetValue(window, startedAt.Value);
            }

            if (lastSeen is not null)
            {
                type.GetProperty("LastSeen")!.SetValue(window, lastSeen.Value);
            }

            return;
        }
    }
}

public sealed class TokenBucketTests
{
    [Fact]
    public void New_StartsAtBurstCapacity()
    {
        var bucket = new TokenBucket(3, 0.01);
        Assert.True(bucket.TryTake());
        Assert.True(bucket.TryTake());
        Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());
    }

    [Fact]
    public void TryTake_Empty_ReturnsFalse()
    {
        var bucket = new TokenBucket(1, 0.01);
        Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());
    }

    [Fact]
    public async Task TryTake_RefillsAtConfiguredRate()
    {
        var bucket = new TokenBucket(1, 100);
        Assert.True(bucket.TryTake());
        await Task.Delay(30);
        Assert.True(bucket.TryTake());
    }

    [Fact]
    public async Task Refill_NeverExceedsCapacity()
    {
        var bucket = new TokenBucket(2, 100);
        await Task.Delay(100);
        Assert.True(bucket.TryTake());
        Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());
    }

    [Fact]
    public void Ctor_ClampsBurstToOne()
    {
        var bucket = new TokenBucket(0, 0.01);
        Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());
    }

    [Fact]
    public async Task Ctor_ClampsRateToMinimum()
    {
        var bucket = new TokenBucket(1, 0);
        Assert.True(bucket.TryTake());
        await Task.Delay(100);
        Assert.False(bucket.TryTake());
    }

    [Fact]
    public void TryTake_Concurrent_NeverOverdraws()
    {
        var bucket = new TokenBucket(5, 0.01);
        var allowed = 0;
        Parallel.For(0, 50, _ =>
        {
            if (bucket.TryTake())
            {
                Interlocked.Increment(ref allowed);
            }
        });
        Assert.Equal(5, allowed);
    }
}
