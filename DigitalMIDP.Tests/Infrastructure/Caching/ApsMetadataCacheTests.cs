using DigitalMIDP.Infrastructure.Caching;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DigitalMIDP.Tests.Infrastructure.Caching;

/// <summary>
/// Tests for <see cref="ApsMetadataCache"/>, which is what stops the application re-asking APS for
/// project configuration and file paths it already has.
/// </summary>
public sealed class ApsMetadataCacheTests
{
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    private static ApsMetadataCache CreateCache() =>
        new(Substitute.For<ILogger<ApsMetadataCache>>());

    [Fact]
    public async Task GetOrAddAsync_WithRepeatedCalls_RunsTheFactoryOnce()
    {
        var cache = CreateCache();
        var calls = 0;

        for (var i = 0; i < 5; i++)
        {
            var value = await cache.GetOrAddAsync(
                "b.p1", "key", OneMinute,
                _ => { calls++; return Task.FromResult("fetched"); },
                CancellationToken.None);

            value.Should().Be("fetched");
        }

        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrAddAsync_WithConcurrentCallersOnAColdKey_RunsTheFactoryOnce()
    {
        var cache = CreateCache();
        var calls = 0;
        var release = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, 30)
            .Select(_ => cache.GetOrAddAsync(
                "b.p1", "key", OneMinute,
                async _ =>
                {
                    Interlocked.Increment(ref calls);
                    await release.Task;
                    return "fetched";
                },
                CancellationToken.None))
            .ToList();

        release.SetResult();
        var values = await Task.WhenAll(tasks);

        // The stampede case, and the reason entries hold the in-flight task rather than a finished
        // value: the real callers fan out over every file in a project and would otherwise all miss
        // the same cold key at the same instant and all fetch it.
        values.Should().AllBe("fetched");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrAddAsync_AfterExpiry_FetchesAgain()
    {
        var cache = CreateCache();
        var calls = 0;

        Task<string> Fetch(CancellationToken _)
        {
            calls++;
            return Task.FromResult("v" + calls);
        }

        // A lifetime already in the past, so the entry is stale the moment it is written.
        (await cache.GetOrAddAsync("b.p1", "key", TimeSpan.Zero, Fetch, CancellationToken.None))
            .Should().Be("v1");
        (await cache.GetOrAddAsync("b.p1", "key", TimeSpan.Zero, Fetch, CancellationToken.None))
            .Should().Be("v2");

        calls.Should().Be(2);
    }

    [Fact]
    public async Task GetOrAddAsync_WhenTheFactoryThrows_DoesNotCacheTheFailure()
    {
        var cache = CreateCache();
        var calls = 0;

        Task<string> Fetch(CancellationToken _)
        {
            calls++;
            return calls == 1
                ? Task.FromException<string>(new InvalidOperationException("APS blipped"))
                : Task.FromResult("recovered");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => cache.GetOrAddAsync("b.p1", "key", OneMinute, Fetch, CancellationToken.None));

        // A transient APS error must not poison the key for the rest of the entry's lifetime - it
        // would turn one dropped connection into minutes of a project looking broken.
        (await cache.GetOrAddAsync("b.p1", "key", OneMinute, Fetch, CancellationToken.None))
            .Should().Be("recovered");

        calls.Should().Be(2);
    }

    [Fact]
    public async Task GetOrAddAsync_WhenOneCallerCancels_StillServesTheOthers()
    {
        var cache = CreateCache();
        var release = new TaskCompletionSource();
        using var cts = new CancellationTokenSource();

        var cancelling = cache.GetOrAddAsync(
            "b.p1", "key", OneMinute,
            async _ => { await release.Task; return "fetched"; },
            cts.Token);

        var patient = cache.GetOrAddAsync(
            "b.p1", "key", OneMinute,
            _ => Task.FromResult("never reached"),
            CancellationToken.None);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelling);

        release.SetResult();

        // The shared fetch is not bound to whichever caller happened to start it, so one caller
        // abandoning it (a project switch, say) does not fail everybody else waiting on it.
        (await patient).Should().Be("fetched");
    }

    [Fact]
    public async Task InvalidateProject_DropsOnlyThatProjectsEntries()
    {
        var cache = CreateCache();
        var calls = new Dictionary<string, int>();

        async Task<string> Get(string projectId, string key)
        {
            return await cache.GetOrAddAsync(
                projectId, key, OneMinute,
                _ =>
                {
                    calls[key] = calls.GetValueOrDefault(key) + 1;
                    return Task.FromResult(key);
                },
                CancellationToken.None);
        }

        await Get("b.p1", "p1-schema");
        await Get("b.p2", "p2-schema");
        await Get(null!, "global");

        cache.InvalidateProject("b.p1");

        await Get("b.p1", "p1-schema");
        await Get("b.p2", "p2-schema");
        await Get(null!, "global");

        // This is what makes a manual Refresh honest - it re-reads the project the user is looking
        // at - without throwing away everything cached for every other project.
        calls["p1-schema"].Should().Be(2);
        calls["p2-schema"].Should().Be(1);
        calls["global"].Should().Be(1);
    }

    [Fact]
    public async Task InvalidateProject_IsCaseInsensitiveAndIgnoresBlankIds()
    {
        var cache = CreateCache();
        var calls = 0;

        Task<string> Fetch(CancellationToken _) { calls++; return Task.FromResult("v"); }

        await cache.GetOrAddAsync("b.Project1", "key", OneMinute, Fetch, CancellationToken.None);

        cache.InvalidateProject("   ");
        await cache.GetOrAddAsync("b.Project1", "key", OneMinute, Fetch, CancellationToken.None);
        calls.Should().Be(1);

        // ACC project ids are handled case-insensitively throughout this codebase, so invalidation
        // must be too - otherwise a Refresh could silently fail to clear anything.
        cache.InvalidateProject("b.project1");
        await cache.GetOrAddAsync("b.Project1", "key", OneMinute, Fetch, CancellationToken.None);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Clear_DropsEverything()
    {
        var cache = CreateCache();
        var calls = 0;

        Task<string> Fetch(CancellationToken _) { calls++; return Task.FromResult("v"); }

        await cache.GetOrAddAsync("b.p1", "a", OneMinute, Fetch, CancellationToken.None);
        await cache.GetOrAddAsync("b.p2", "b", OneMinute, Fetch, CancellationToken.None);

        cache.Clear();

        await cache.GetOrAddAsync("b.p1", "a", OneMinute, Fetch, CancellationToken.None);
        await cache.GetOrAddAsync("b.p2", "b", OneMinute, Fetch, CancellationToken.None);

        // Called on sign-out: everything in here was read with the outgoing user's credentials.
        calls.Should().Be(4);
    }

    [Fact]
    public async Task TryGet_ReportsAHitOnlyForAFinishedFetch()
    {
        var cache = CreateCache();
        var release = new TaskCompletionSource();

        cache.TryGet<string>("key", out _).Should().BeFalse("nothing has been cached yet");

        var pending = cache.GetOrAddAsync(
            "b.p1", "key", OneMinute,
            async _ => { await release.Task; return "fetched"; },
            CancellationToken.None);

        // Reported as a miss while still in flight. Callers use TryGet to decide what they still
        // have to request, so blocking on a pending entry would defeat the point - they fetch
        // alongside it instead.
        cache.TryGet<string>("key", out _).Should().BeFalse("the fetch has not finished");

        release.SetResult();
        await pending;

        cache.TryGet<string>("key", out var value).Should().BeTrue();
        value.Should().Be("fetched");
    }

    [Fact]
    public void TryGet_WithAnExpiredEntry_ReportsAMiss()
    {
        var cache = CreateCache();

        cache.Set("b.p1", "key", TimeSpan.Zero, "stale");

        cache.TryGet<string>("key", out _).Should().BeFalse();
    }

    [Fact]
    public void Set_StoresAValueTheCallerFetchedItself()
    {
        var cache = CreateCache();

        cache.Set("b.p1", "key", OneMinute, "value");

        cache.TryGet<string>("key", out var value).Should().BeTrue();
        value.Should().Be("value");
    }

    [Fact]
    public async Task GetOrAddAsync_WithTheSameKeyButADifferentType_RefetchesRatherThanCrashing()
    {
        var cache = CreateCache();

        await cache.GetOrAddAsync(
            "b.p1", "key", OneMinute, _ => Task.FromResult("a string"), CancellationToken.None);

        // A key reused for two types is a caller bug, but it must not take down a file listing: the
        // entry is replaced and the fetch repeated.
        var number = await cache.GetOrAddAsync(
            "b.p1", "key", OneMinute, _ => Task.FromResult(42), CancellationToken.None);

        number.Should().Be(42);
    }

    [Fact]
    public async Task GetOrAddAsync_WithNullValue_IsStillCached()
    {
        var cache = CreateCache();
        var calls = 0;

        Task<string?> Fetch(CancellationToken _) { calls++; return Task.FromResult<string?>(null); }

        (await cache.GetOrAddAsync("b.p1", "key", OneMinute, Fetch, CancellationToken.None))
            .Should().BeNull();
        (await cache.GetOrAddAsync("b.p1", "key", OneMinute, Fetch, CancellationToken.None))
            .Should().BeNull();

        // A successful fetch that produced nothing is a real answer and is cached as one. (The path
        // metadata path deliberately returns null WITHOUT caching for its own reason - see
        // GetItemPathMetadataInternalAsync - which is a decision made there, not here.)
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrAddAsync_WithMissingArguments_Throws()
    {
        var cache = CreateCache();

        await Assert.ThrowsAsync<ArgumentException>(() => cache.GetOrAddAsync(
            "b.p1", "", OneMinute, _ => Task.FromResult("v"), CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentNullException>(() => cache.GetOrAddAsync<string>(
            "b.p1", "key", OneMinute, null!, CancellationToken.None));
    }
}
