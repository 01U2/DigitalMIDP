using DigitalMIDP.Application.Manifests;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.Infrastructure.Manifests;

internal sealed class ManifestRunner(ManifestEnrollmentStore store, TimeProvider clock,
    Func<string, string, CancellationToken, Task<ManifestCheckResult>> publish, ILogger<ManifestRunner> logger) : IManifestRunner
{
    private sealed class Entry(Enrollment row)
    {
        internal Enrollment Row = row;
        internal readonly CancellationTokenSource Lifetime = new();
        internal ITimer? Timer;
        internal Task<ManifestCheckResult>? Active;
        internal ManifestProjectStatus Status = new(row.HubId, row.ProjectId, row.EffectiveInterval, false, null, null, null, null);
    }

    private readonly SemaphoreSlim lifecycle = new(1);
    private readonly object gate = new();
    private readonly Dictionary<(string, string), Entry> entries = new();
    private bool started;
    public event EventHandler? StatusChanged;

    public IReadOnlyList<ManifestProjectStatus> GetSnapshot()
    {
        lock (gate) return entries.Values.Select(e => e.Status).ToArray();
    }

    public async Task StartAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (started) return;
            var rows = await store.LoadAsync().ConfigureAwait(false);
            lock (gate)
            {
                started = true;
                foreach (var row in rows) Add(row);
            }
        }
        finally { lifecycle.Release(); }
        Notify();
    }

    public async Task EnableAsync(string hubId, string projectId, TimeSpan? interval = null)
    {
        var row = new Enrollment(hubId, projectId, interval);
        row.Validate();
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            RequireStarted();
            entries.TryGetValue((hubId, projectId), out var existing);
            if (existing?.Row == row) return;
            await store.SaveAsync(entries.Values.Where(e => e != existing).Select(e => e.Row).Append(row).ToArray()).ConfigureAwait(false);
            lock (gate)
            {
                if (existing is null) Add(row);
                else
                {
                    existing.Row = row;
                    existing.Status = existing.Status with { Interval = row.EffectiveInterval };
                    if (existing.Active is null) Arm(existing, row.EffectiveInterval);
                }
            }
        }
        finally { lifecycle.Release(); }
        Notify();
    }

    private void Add(Enrollment row)
    {
        var entry = new Entry(row);
        entries.Add((row.HubId, row.ProjectId), entry);
        entry.Timer = clock.CreateTimer(_ => OnTimer(entry), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Arm(entry, TimeSpan.FromSeconds(entries.Count));
    }

    private void Arm(Entry entry, TimeSpan delay)
    {
        entry.Status = entry.Status with { NextCheckUtc = clock.GetUtcNow() + delay };
        entry.Timer!.Change(delay, Timeout.InfiniteTimeSpan);
    }

    public Task<ManifestCheckResult> CheckNowAsync(string hubId, string projectId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            RequireStarted();
            if (!entries.TryGetValue((hubId, projectId), out var entry))
                throw new InvalidOperationException("Project is not enrolled.");
            return Begin(entry).WaitAsync(ct);
        }
    }

    private void OnTimer(Entry entry)
    {
        lock (gate)
        {
            if (entry.Timer is null || entry.Status.NextCheckUtc is not { } due) return;
            var remaining = due - clock.GetUtcNow();
            if (remaining > TimeSpan.Zero) entry.Timer.Change(remaining, Timeout.InfiniteTimeSpan);
            else _ = Begin(entry);
        }
    }

    private Task<ManifestCheckResult> Begin(Entry entry)
    {
        if (entry.Active is not null) return entry.Active;
        entry.Timer!.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        entry.Status = entry.Status with { IsChecking = true, LastAttemptUtc = clock.GetUtcNow(), NextCheckUtc = null };
        var active = Task.Run(() => RunAsync(entry));
        // Scheduled checks need their exception observed even when there is no manual waiter.
        _ = active.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return entry.Active = active;
    }

    private async Task<ManifestCheckResult> RunAsync(Entry entry)
    {
        ManifestCheckResult? result = null;
        string? error = null;
        try
        {
            Notify();
            var ct = entry.Lifetime.Token;
            ct.ThrowIfCancellationRequested();
            result = await publish(entry.Status.HubId, entry.Status.ProjectId, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (entry.Lifetime.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            logger.LogWarning("Manifest check failed for {ProjectId}: {Error}", entry.Status.ProjectId, error);
            throw;
        }
        finally
        {
            lock (gate)
            {
                entry.Active = null;
                entry.Status = entry.Status with { IsChecking = false, LastSuccess = result ?? entry.Status.LastSuccess, LastError = error };
                if (entry.Timer is not null) Arm(entry, entry.Row.EffectiveInterval);
            }
            Notify();
        }
    }

    public async Task DisableAsync(string hubId, string projectId)
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            RequireStarted();
            if (!entries.TryGetValue((hubId, projectId), out var entry)) return;
            await store.SaveAsync(entries.Values.Where(e => e != entry).Select(e => e.Row).ToArray()).ConfigureAwait(false);
            Task? active;
            lock (gate)
            {
                entries.Remove((hubId, projectId));
                active = Disarm(entry);
            }
            await CancelAndDrainAsync(entry, active).ConfigureAwait(false);
        }
        finally { lifecycle.Release(); }
        Notify();
    }

    public async Task StopAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            (Entry Entry, Task? Active)[] pending;
            lock (gate)
            {
                started = false;
                pending = entries.Values.Select(e => (e, Disarm(e))).ToArray();
                entries.Clear();
            }
            await Task.WhenAll(pending.Select(p => CancelAndDrainAsync(p.Entry, p.Active))).ConfigureAwait(false);
        }
        finally { lifecycle.Release(); }
        Notify();
    }

    private static Task? Disarm(Entry entry)
    {
        entry.Timer!.Dispose();
        entry.Timer = null;
        return entry.Active;
    }

    private static async Task CancelAndDrainAsync(Entry entry, Task? active)
    {
        try
        {
            await entry.Lifetime.CancelAsync().ConfigureAwait(false);
            if (active is not null) await active.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        finally { entry.Lifetime.Dispose(); }
    }

    private void RequireStarted()
    {
        if (!started) throw new InvalidOperationException("Start the manifest runner first.");
    }

    private void Notify()
    {
        if (StatusChanged is not { } handlers) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception ex) { logger.LogWarning(ex, "Manifest status observer failed"); }
        }
    }
}
