namespace DigitalMIDP.Infrastructure.Caching;

/// <summary>
/// A short-lived cache for APS data that does not change from one minute to the next, shared across
/// the application.
///
/// <para>
/// This has to be a separate singleton rather than a field on <c>DataManagementService</c>:
/// that service is registered through <c>AddHttpClient</c>, which makes it <b>transient</b>, so a
/// per-instance dictionary would be thrown away after every call and cache nothing.
/// </para>
///
/// <para>
/// What belongs in here is project-level configuration and structure - a project's top folders, its
/// root folder, its custom attribute definitions, a file's path within the project. What does not is
/// anything a user edits and then expects to see: custom attribute <i>values</i> are read live every
/// time, and the grid's own in-memory copy is updated directly on save (see
/// <c>FilesViewModel.ApplyUpdatedCustomAttributes</c>).
/// </para>
/// </summary>
internal interface IApsMetadataCache
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or runs <paramref name="factory"/> and
    /// caches its result for <paramref name="lifetime"/>.
    ///
    /// <para>
    /// Concurrent callers asking for the same cold key are collapsed onto one execution of
    /// <paramref name="factory"/> - which is the point, since the callers here fan out over every
    /// file in a project and would otherwise all miss together and all fetch.
    /// </para>
    /// </summary>
    /// <param name="projectId">
    /// The project the entry belongs to, so <see cref="InvalidateProject"/> can drop everything for
    /// one project without flushing the rest. Entries that are not project-scoped pass null.
    /// </param>
    /// <param name="key">Cache key, unique across all callers (prefix it with the kind of data).</param>
    /// <param name="lifetime">How long the value stays usable.</param>
    /// <param name="factory">Fetches the value on a miss.</param>
    Task<T> GetOrAddAsync<T>(
        string? projectId,
        string key,
        TimeSpan lifetime,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the cached value for <paramref name="key"/> if it is present and still fresh.
    /// Used where the caller wants to batch together only the keys that actually missed, rather
    /// than fetching one at a time.
    /// </summary>
    bool TryGet<T>(string key, out T? value);

    /// <summary>Stores a value fetched by the caller itself (the counterpart to <see cref="TryGet"/>).</summary>
    void Set<T>(string? projectId, string key, TimeSpan lifetime, T value);

    /// <summary>
    /// Drops one entry by key.
    ///
    /// <para>
    /// Exists for the case where the value a fetch produced turns out not to be worth keeping - see
    /// <c>DataManagementService.GetCachedItemPathMetadataAsync</c>, which uses it so that "the path
    /// could not be read" is not pinned onto a file for the whole cache lifetime. Note this is about
    /// an unhelpful <i>successful</i> result; a fetch that actually faults is evicted automatically.
    /// </para>
    /// </summary>
    void Invalidate(string key);

    /// <summary>
    /// Drops every entry belonging to one project. Called when the user explicitly refreshes, and
    /// after this application changes something in the project that a cached entry would contradict.
    /// </summary>
    void InvalidateProject(string projectId);

    /// <summary>Drops everything - used on sign-out, so a second user's session starts clean.</summary>
    void Clear();
}
