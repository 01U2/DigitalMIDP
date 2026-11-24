using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DigitalMIDP.Application.Manifests;

namespace DigitalMIDP.Infrastructure.Manifests;

internal sealed partial class LocalManifestReader
{
    public async IAsyncEnumerable<LocalManifestSnapshot> WatchAsync(LocalManifestProject project,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var changes = Channel.CreateBounded<bool>(1);
        (LocalManifestStatus, long?, string?)? previous = null;
        while (true)
        {
            var watchers = new List<FileSystemWatcher>();
            try
            {
                var snapshot = await Task.Run(() => ReadCoreAsync(project, Attach, ct), ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                var key = (snapshot.Status, snapshot.Manifest?.Revision, snapshot.Reason);
                if (previous != key)
                {
                    previous = key;
                    yield return snapshot;
                }

                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromMinutes(10));
                try
                {
                    await changes.Reader.ReadAsync(deadline.Token).ConfigureAwait(false);
                    do { await Task.Delay(150, deadline.Token).ConfigureAwait(false); }
                    while (changes.Reader.TryRead(out _));
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            }
            finally
            {
                foreach (var watcher in watchers) watcher.Dispose();
            }

            void Attach(string path, Func<string?, bool> matches)
            {
                var watcher = new FileSystemWatcher
                {
                    NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.Attributes |
                        NotifyFilters.LastWrite | NotifyFilters.Size
                };
                watchers.Add(watcher);
                try { watcher.Path = path; }
                catch (ArgumentException) when (!Directory.Exists(path)) { return; }
                watcher.Changed += OnChange;
                watcher.Created += OnChange;
                watcher.Deleted += OnChange;
                watcher.Renamed += (_, e) => { if (matches(e.OldName) || matches(e.Name)) changes.Writer.TryWrite(true); };
                watcher.Error += (_, _) => changes.Writer.TryWrite(true);
                try { watcher.EnableRaisingEvents = true; }
                catch (System.ComponentModel.Win32Exception) { /* The periodic read remains active. */ }

                void OnChange(object sender, FileSystemEventArgs e)
                {
                    if (matches(e.Name)) changes.Writer.TryWrite(true);
                }
            }
        }
    }
}
