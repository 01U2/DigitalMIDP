using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.Infrastructure.Caching;

/// <summary>
/// Default <see cref="IApsMetadataCache"/>: an in-memory dictionary of expiring entries, with
/// per-project invalidation.
///
/// <para>
/// An entry holds a <see cref="Lazy{T}"/> over the in-flight fetch rather than a finished value,
/// which is what collapses a stampede. The callers here fan out over every file in a project, so
/// without that they would all miss the same cold key at the same moment and all fetch it. The
/// <see cref="Lazy{T}"/> is forced by whichever caller wins the dictionary insert; the losers
/// discard their own unstarted one and await the winner's.
/// </para>
///
/// <para>
/// A fetch that faults is evicted, so a transient APS error is not cached for the rest of the
/// entry's lifetime.
/// </para>
/// </summary>
internal sealed class ApsMetadataCache : IApsMetadataCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ILogger<ApsMetadataCache> _logger;

    public ApsMetadataCache(ILogger<ApsMetadataCache> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task<T> GetOrAddAsync<T>(
        string? projectId,
        string key,
        TimeSpan lifetime,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var now = DateTimeOffset.UtcNow;

            if (_entries.TryGetValue(key, out var existing))
            {
                if (existing.ExpiresAt > now && existing.Lazy is Lazy<Task<T>> hit)
                {
                    return await AwaitEntryAsync(key, existing, hit, cancellationToken);
                }

                if (existing.ExpiresAt > now)
                {
                    // Same key used for two different types - a bug in the caller, not a runtime
                    // condition. Logged loudly, then handled by refetching rather than by throwing
                    // in the middle of a file listing.
                    _logger.LogWarning(
                        "Cache key '{Key}' holds {StoredType} but was requested as Task<{RequestedType}>; refetching",
                        key, existing.Lazy.GetType().Name, typeof(T).Name);
                }

                var replacement = NewEntry(projectId, lifetime, factory);

                if (_entries.TryUpdate(key, replacement, existing))
                {
                    return await AwaitEntryAsync(key, replacement, (Lazy<Task<T>>)replacement.Lazy, cancellationToken);
                }

                // Somebody else replaced it first; re-read and use theirs.
                continue;
            }

            var added = NewEntry(projectId, lifetime, factory);

            if (_entries.TryAdd(key, added))
            {
                return await AwaitEntryAsync(key, added, (Lazy<Task<T>>)added.Lazy, cancellationToken);
            }
        }
    }

    public bool TryGet<T>(string key, out T? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        value = default;

        if (!_entries.TryGetValue(key, out var entry) || entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        // Only a finished, successful fetch counts as a hit. Callers use TryGet to work out what
        // they still have to request, so reporting a pending entry as a hit - and then blocking on
        // it - would defeat the point; they treat it as a miss and fetch alongside it instead.
        if (entry.Lazy is Lazy<Task<T>> { IsValueCreated: true } lazy
            && lazy.Value.IsCompletedSuccessfully)
        {
            value = lazy.Value.Result;
            return true;
        }

        return false;
    }

    public void Set<T>(string? projectId, string key, TimeSpan lifetime, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var completed = new Lazy<Task<T>>(() => Task.FromResult(value));
        _ = completed.Value; // force, so TryGet sees it as a finished fetch immediately

        _entries[key] = new Entry(completed, DateTimeOffset.UtcNow + lifetime, projectId);
    }

    public void Invalidate(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        _entries.TryRemove(key, out _);
    }

    public void InvalidateProject(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        var removed = 0;

        foreach (var pair in _entries)
        {
            if (string.Equals(pair.Value.ProjectId, projectId, StringComparison.OrdinalIgnoreCase)
                && _entries.TryRemove(pair.Key, out _))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            _logger.LogDebug("Invalidated {Count} cached entr(ies) for project {ProjectId}", removed, projectId);
        }
    }

    public void Clear()
    {
        var count = _entries.Count;
        _entries.Clear();

        if (count > 0)
        {
            _logger.LogDebug("Cleared {Count} cached APS metadata entr(ies)", count);
        }
    }

    /// <summary>
    /// Builds an entry whose fetch has not started yet, so a caller that loses the insert race
    /// discards it without having issued a request.
    ///
    /// <para>
    /// The factory gets <see cref="CancellationToken.None"/> on purpose: the fetch is shared, and
    /// binding it to whichever caller happened to start it would let that caller's cancellation
    /// (a project switch, say) fail every other caller waiting on the same key. Each caller
    /// observes its own token separately in <see cref="AwaitEntryAsync"/>. An abandoned fetch
    /// therefore runs to completion - these are small project-level reads, bounded by the
    /// HttpClient timeout, and the result is still worth caching.
    /// </para>
    /// </summary>
    private static Entry NewEntry<T>(
        string? projectId,
        TimeSpan lifetime,
        Func<CancellationToken, Task<T>> factory) =>
        new(new Lazy<Task<T>>(() => factory(CancellationToken.None)),
            DateTimeOffset.UtcNow + lifetime,
            projectId);

    /// <summary>
    /// Awaits a shared fetch while still honouring this caller's own cancellation, and evicts the
    /// entry if the fetch failed.
    /// </summary>
    private async Task<T> AwaitEntryAsync<T>(
        string key,
        Entry entry,
        Lazy<Task<T>> lazy,
        CancellationToken cancellationToken)
    {
        try
        {
            return await lazy.Value.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // This caller gave up; the shared fetch is untouched and still useful to the others.
            throw;
        }
        catch
        {
            // Compare before removing, so a retry that has already installed a fresh entry is not
            // evicted by this failure's cleanup.
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            {
                _entries.TryRemove(key, out _);
            }

            throw;
        }
    }

    /// <summary>One cached fetch: the lazily-started task, when it expires, and whose project it is.</summary>
    private sealed record Entry(object Lazy, DateTimeOffset ExpiresAt, string? ProjectId);
}
