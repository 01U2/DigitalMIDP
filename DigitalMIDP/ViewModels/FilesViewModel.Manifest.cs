using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Manifests;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.ViewModels;

public sealed partial class FilesViewModel
{
    private long _editSession;
    private long _highestRevision;
    private readonly Dictionary<(string File, int Id), PendingEdit> _pendingEdits = new();
    private sealed record PendingEdit(string Folder, string Urn, string Tip, MetadataFieldDisplayDto Field, long Revision, string? Confirmed);

    private IReadOnlyDictionary<int, string?> EffectiveValues(ManifestFile file)
    {
        var values = new Dictionary<int, string?>(file.Values);
        foreach (var field in _pendingEdits.Where(p => p.Key.File == file.Item.Id))
            values[field.Key.Id] = field.Value.Confirmed;
        return values;
    }

    public void OpenAttributeEditor(EditCustomAttributesViewModel editor, IReadOnlyList<ItemDto> selected)
    {
        _dispatcher.VerifyAccess();
        var manifest = CurrentManifest;
        if (manifest is null || manifest.ProjectId != CurrentProjectId || manifest.HubId != CurrentHubId || selected.Count == 0)
            throw new InvalidOperationException("Select files from an available project manifest.");

        var targets = new Dictionary<string, AttributeEditTarget>(StringComparer.Ordinal);
        foreach (var row in selected)
        {
            var folder = manifest.Folders.SingleOrDefault(f => f.FolderId == row.ParentId);
            var file = folder?.Files.SingleOrDefault(f => f.Item.Id == row.Id);
            if (file == null || string.IsNullOrWhiteSpace(row.ItemUrn) || file.Item.ItemUrn != row.ItemUrn ||
                string.IsNullOrWhiteSpace(row.TipVersionUrn) || file.Item.TipVersionUrn != row.TipVersionUrn ||
                !targets.TryAdd(row.ItemUrn, new(file.Item, folder!.Definitions, EffectiveValues(file))))
                throw new InvalidOperationException("File identity or version is missing or changed. Refresh and reselect the files.");
        }
        var session = _editSession;
        bool SameProject() => !_disposed && session == _editSession && CurrentManifest?.ProjectId == manifest.ProjectId && CurrentManifest.HubId == manifest.HubId;
        bool IsCurrent() => SameProject() && ReferenceEquals(manifest, CurrentManifest) && ManifestStatusText == null;
        editor.Initialize(manifest.ProjectId, targets.Values.ToArray(), IsCurrent);
        if (editor.Fields.Count == 0)
            throw new InvalidOperationException("The selected files have no compatible editable attributes. Select files from one folder.");
        editor.AttributesUpdated += confirmed =>
        {
            if (!SameProject()) return;
            foreach (var (urn, values) in confirmed)
            {
                var target = targets[urn];
                var folder = CurrentManifest!.Folders.SingleOrDefault(f => f.FolderId == target.Item.ParentId);
                var now = folder?.Files.SingleOrDefault(f => f.Item.Id == target.Item.Id && f.Item.ItemUrn == urn && f.Item.TipVersionUrn == target.Item.TipVersionUrn);
                if (now == null) continue;
                var accepted = new Dictionary<string, string?>();
                foreach (var (name, value) in values)
                {
                    var field = target.Definitions.Single(d => d.Name == name);
                    var edit = new PendingEdit(folder!.FolderId, urn, now.Item.TipVersionUrn!, field, manifest.Revision, value);
                    if (Reconcile(edit, folder, now, CurrentManifest.Revision) is not { } pending) continue;
                    _pendingEdits[(now.Item.Id, field.AttributeId)] = pending;
                    accepted[name] = value;
                }
                if (accepted.Count > 0) ApplyUpdatedCustomAttributes(urn, accepted);
            }
        };
    }

    private static PendingEdit? Reconcile(PendingEdit edit, ManifestFolder folder, ManifestFile file, long revision)
    {
        if (edit.Revision != revision || edit.Folder != folder.FolderId || edit.Urn != file.Item.ItemUrn || edit.Tip != file.Item.TipVersionUrn ||
            !folder.Definitions.Any(f => f.AttributeId == edit.Field.AttributeId && f.Name == edit.Field.Name && f.Type == edit.Field.Type)) return null;
        return edit;
    }

    public Task ActivateProjectAsync(LocalManifestProject? project)
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return Task.CompletedTask;
        ResetForSignOut();
        _activeProject = project;
        if (project is null) return Task.CompletedTask;
        CurrentHubId = project.HubId;
        CurrentProjectId = project.ProjectId;
        CurrentHubName = project.HubName;
        CurrentProjectName = project.ProjectName;
        CurrentFolderName = "Project Files";
        return StartWatchAsync(project, refresh: false);
    }

    [RelayCommand]
    private Task RefreshAsync() => !_disposed && _activeProject is { } project
        ? StartWatchAsync(project, refresh: true) : Task.CompletedTask;

    private Task StartWatchAsync(LocalManifestProject project, bool refresh)
    {
        _loadFolderItemsCts?.Cancel();
        var session = _loadFolderItemsCts = new CancellationTokenSource();
        IsLoading = true;
        IsCancellingLoad = false;
        LoadingStatusText = "Reading local manifest…";
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = ObserveAsync();
        return first.Task;

        async Task PublishAsync(LocalManifestSnapshot snapshot)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                if (session.IsCancellationRequested || _loadFolderItemsCts != session) return;
                ApplySnapshot(snapshot);
                IsLoading = false;
                LoadingStatusText = null;
                first.TrySetResult();
            });
        }

        async Task ObserveAsync()
        {
            try
            {
                if (refresh) await PublishAsync(await _reader.ReadAsync(project, session.Token).ConfigureAwait(false));
                session.Token.ThrowIfCancellationRequested();
                await foreach (var snapshot in _reader.WatchAsync(project, session.Token).ConfigureAwait(false))
                    await PublishAsync(snapshot).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (session.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watching local manifest for {ProjectId}", project.ProjectId);
                await _dispatcher.InvokeAsync(() =>
                {
                    if (!session.IsCancellationRequested && _loadFolderItemsCts == session)
                        ManifestStatusText = $"Local manifest {(CurrentManifest is null ? "unavailable" : "stale")}: {ex.Message}";
                });
            }
            finally
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    if (_loadFolderItemsCts == session)
                    {
                        _loadFolderItemsCts = null;
                        IsLoading = IsCancellingLoad = false;
                        LoadingStatusText = null;
                    }
                });
                session.Dispose();
                first.TrySetResult();
            }
        }
    }

    private void ApplySnapshot(LocalManifestSnapshot snapshot)
    {
        if (snapshot.Manifest is { } incoming && (incoming.HubId != CurrentHubId || incoming.ProjectId != CurrentProjectId || incoming.Revision < _highestRevision)) return;
        ManifestStatusText = snapshot.Status switch
        {
            LocalManifestStatus.Available => null,
            LocalManifestStatus.Stale => $"Local manifest stale (revision {snapshot.Manifest?.Revision}): {snapshot.Reason}",
            _ => $"Local manifest unavailable: {snapshot.Reason}"
        };
        if (CurrentManifest?.Revision == snapshot.Manifest?.Revision) return;
        CurrentManifest = snapshot.Manifest;
        if (CurrentManifest is { } current) _highestRevision = current.Revision;
        var selectedId = SelectedItem?.Id;
        var rows = new ObservableCollection<ItemDto>();
        AvailableMetadataFields.Clear();
        _customAttributesByItemUrn.Clear();
        var folders = CurrentManifest?.Folders ?? [];
        var retained = new HashSet<(string File, int Id)>();
        foreach (var group in folders.SelectMany(f => f.Definitions).GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            var options = group.SelectMany(d => d.Options).DistinctBy(o => o.Value, StringComparer.OrdinalIgnoreCase).ToArray();
            AvailableMetadataFields.Add(group.First() with { Options = options, AttributeId = 0 });
        }
        foreach (var folder in folders)
        {
            var fields = folder.Definitions.ToDictionary(f => f.AttributeId);
            foreach (var file in folder.Files)
            {
                var values = file.Values.ToDictionary(v => fields[v.Key].Name, v => v.Value, StringComparer.OrdinalIgnoreCase);
                foreach (var id in fields.Keys)
                {
                    var key = (file.Item.Id, id);
                    if (_pendingEdits.TryGetValue(key, out var edit) && Reconcile(edit, folder, file, CurrentManifest!.Revision) is { } pending)
                    {
                        retained.Add(key);
                        _pendingEdits[key] = pending;
                        values[fields[id].Name] = pending.Confirmed;
                    }
                }
                if (values.TryGetValue("Description", out var description))
                    values[ColumnKeys.DeliverableTitle] = description;
                var row = file.Item with { CustomAttributes = values };
                if (row.ItemUrn is { } urn) _customAttributesByItemUrn[urn] = values;
                rows.Add(row);
            }
        }
        if (CurrentManifest != null)
            foreach (var key in _pendingEdits.Keys.Except(retained).ToArray()) _pendingEdits.Remove(key);
        Items = rows;
        _fileFilterViewModel.Rebuild(rows, GridMetadataFields);
        ItemsView?.Refresh();
        SelectedItem = rows.FirstOrDefault(i => i.Id == selectedId);
        OnPropertyChanged(nameof(ShowEmptyState));
    }
}
