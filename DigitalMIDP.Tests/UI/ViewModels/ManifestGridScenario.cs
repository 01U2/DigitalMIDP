using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Application.Manifests;
using DigitalMIDP.Application.UseCases;
using DigitalMIDP.ViewModels;
using DigitalMIDP.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace DigitalMIDP.Tests.UI.ViewModels;

internal static class ManifestGridScenario
{
    internal static Task RunAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await ExerciseWindow(); done.TrySetResult(); }
                catch (Exception ex) { done.TrySetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }

    private static async Task ExerciseWindow()
    {
        var root = Directory.CreateTempSubdirectory();
        var hub = new HubDto { Id = "hub-id", Name = "Hub name", Type = "hubs" };
        var a = new ProjectDto { Id = Guid.NewGuid().ToString(), Name = "Project A" };
        var b = new ProjectDto { Id = Guid.NewGuid().ToString(), Name = "Project B" };
        using var local = CompositionRootTests.BuildServices(root.FullName).BuildServiceProvider();
        var reader = new DelayedRead(local.GetRequiredService<ILocalManifestReader>());
        var services = CompositionRootTests.BuildServices(root.FullName);
        services.AddSingleton(sp => new FilterPreferencesStore(sp.GetRequiredService<ILogger<FilterPreferencesStore>>(), Path.Combine(root.FullName, "filters.json")));
        services.AddSingleton(sp => new ColumnPreferencesStore(sp.GetRequiredService<ILogger<ColumnPreferencesStore>>(), Path.Combine(root.FullName, "columns.json")));
        var aps = Substitute.For<IDataManagementService>();
        var catalog = Substitute.For<IFileListingUseCase>();
        var cde = Substitute.For<ICdeStructureUseCase>();
        var token = Substitute.For<ITokenProvider>();
        token.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("test");
        catalog.GetProjectsAsync(hub.Id, "test", Arg.Any<CancellationToken>()).Returns(new GetProjectsResponseDto { Projects = [a,b] });
        services.AddSingleton<ILocalManifestReader>(reader).AddSingleton(aps).AddSingleton(catalog).AddSingleton(cde).AddSingleton(token);
        using var provider = services.BuildServiceProvider();
        var main = provider.GetRequiredService<MainViewModel>();
        var files = main.FilesViewModel;
        var window = new MainWindow(main) { WindowState = System.Windows.WindowState.Normal, ShowActivated = false, ShowInTaskbar = false };
        var uiThread = Environment.CurrentManagedThreadId;
        var updates = new List<int>();
        files.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(files.Items)) updates.Add(Environment.CurrentManagedThreadId); };
        try
        {
            var path = WriteCatalog(root.FullName, hub, a, 1, true);
            main.IsAuthenticated = true;
            main.HubSelectionViewModel.Hubs = [hub];
            window.Show();
            var hubs = (ComboBox)window.FindName("HubSelector");
            var projects = (ComboBox)window.FindName("ProjectSelector");
            var grid = (DataGrid)window.FindName("FilesDataGrid");
            var refresh = (Button)window.FindName("RefreshDataButton");
            hubs.SelectedItem = hub;
            await Until(() => projects.Items.Count == 2);
            Assert.True(projects.IsEnabled);
            catalog.ClearReceivedCalls(); token.ClearReceivedCalls();
            projects.SelectedItem = a;
            await Until(() => grid.Items.Count == 2);
            Assert.Equal(a.Id, files.CurrentManifest!.ProjectId);
            Assert.Contains(grid.Columns, c => c.Header is AttributeFilterViewModel { ColumnKey: "Title" });
            var row = files.Items.First();
            grid.SelectedItem = row;
            Assert.Equal("Drawing A", row.CustomAttributes!["Deliverable Title"]);
            Assert.Equal("Separate title", row.CustomAttributes["Title"]);
            Assert.Contains(files.SelectedItemMetadata, f => f.FieldName == "Description" && f.DisplayValue == "Drawing A");
            files.FileFilterViewModel.SearchText = "Drawing";
            files.FileFilterViewModel.TitleFilter!.Options.Single(o => o.Value == "Drawing B").IsChecked = false;
            Assert.Single(grid.Items.Cast<ItemDto>());
            var editor = provider.GetRequiredService<EditCustomAttributesViewModel>();
            files.OpenAttributeEditor(editor, [row]);
            Assert.Empty(aps.ReceivedCalls());
            Assert.Empty(token.ReceivedCalls());
            Assert.Empty(catalog.ReceivedCalls());
            Assert.Empty(cde.ReceivedCalls());
            var description = editor.Fields.Single(f => f.Name == "Description");
            Assert.Equal("Drawing A", description.Value);
            description.Value = "Drawing edited";
            aps.UpdateItemCustomAttributesAsync(a.Id, "tip:A", Arg.Any<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(), Arg.Any<CancellationToken>())
                .Returns(new List<CustomAttributeUpdateResultDto> { new(1, "Description", "STRING", "Drawing edited") });
            await editor.SaveCommand.ExecuteAsync(null);
            Assert.True(editor.SaveSucceeded, editor.ErrorMessage);
            var refreshWatchReady = reader.NextWatchReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await files.RefreshCommand.ExecuteAsync(null);
            await refreshWatchReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var edited = files.Items.First(i => i.Id == row.Id);
            Assert.Equal("Drawing edited", edited.CustomAttributes!["Deliverable Title"]);
            var reopened = provider.GetRequiredService<EditCustomAttributesViewModel>();
            aps.ClearReceivedCalls();
            files.OpenAttributeEditor(reopened, [edited]);
            Assert.Empty(aps.ReceivedCalls());
            Assert.Empty(token.ReceivedCalls());
            Assert.Equal("Drawing edited", reopened.Fields.Single(f => f.Name == "Description").Value);
            Assert.Equal("Drawing A", files.CurrentManifest!.Folders[0].Files[0].Values[1]);
            async Task<Func<Task>> HoldSave(EditCustomAttributesViewModel pending)
            {
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var reply = new TaskCompletionSource<IReadOnlyList<CustomAttributeUpdateResultDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
                aps.UpdateItemCustomAttributesAsync(a.Id, "tip:A", Arg.Any<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(), Arg.Any<CancellationToken>())
                    .Returns(_ => { entered.TrySetResult(); return reply.Task; });
                pending.Fields.Single(f => f.Name == "Description").Value = "Late write";
                var saving = pending.SaveCommand.ExecuteAsync(null);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return async () =>
                {
                    reply.SetResult([new(1, "Description", "STRING", "Late write")]);
                    await saving;
                    Assert.False(pending.SaveSucceeded);
                    Assert.Contains("changed during save", pending.ErrorMessage);
                    Assert.Equal("Drawing A", files.Items.Single(i => i.Id == row.Id).CustomAttributes!["Deliverable Title"]);
                };
            }
            var finishRevisionSave = await HoldSave(reopened);
            WriteCatalog(root.FullName, hub, a, 2, true);
            await Until(() => files.CurrentManifest?.Revision == 2);
            await finishRevisionSave();
            Assert.Equal("Drawing A", Assert.Single(grid.Items.Cast<ItemDto>()).CustomAttributes!["Deliverable Title"]);
            var switching = provider.GetRequiredService<EditCustomAttributesViewModel>();
            files.OpenAttributeEditor(switching, [files.Items.Single(i => i.Id == row.Id)]);
            var finishProjectSave = await HoldSave(switching);
            aps.ClearReceivedCalls();
            File.WriteAllText(path, "{");
            Assert.Same(files.RefreshCommand, refresh.Command);
            await files.RefreshCommand.ExecuteAsync(null);
            Assert.Contains("stale", files.ManifestStatusText);
            Assert.Single(grid.Items.Cast<ItemDto>());
            reader.DelayNext = true;
            var late = files.RefreshCommand.ExecuteAsync(null);
            await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            projects.SelectedItem = b;
            await Until(() => files.ManifestStatusText?.Contains("unavailable") == true);
            Assert.True(reader.DelayedToken.IsCancellationRequested);
            Assert.Empty(grid.Items.Cast<ItemDto>());
            WriteCatalog(root.FullName, hub, b, 2, false);
            await Until(() => files.CurrentManifest?.ProjectId == b.Id);
            await finishProjectSave();
            reader.Release.TrySetResult();
            await late;
            Assert.Null(files.ManifestStatusText);
            Assert.Equal(2, grid.Items.Count);
            Assert.DoesNotContain(grid.Columns, c => c.Header is AttributeFilterViewModel { ColumnKey: "Title" });
            projects.SelectedIndex = -1;
            Assert.Null(files.CurrentManifest);
            Assert.Empty(grid.Items.Cast<ItemDto>());
            Assert.NotEmpty(updates);
            Assert.All(updates, id => Assert.Equal(uiThread, id));
            Assert.Empty(catalog.ReceivedCalls()); Assert.Empty(aps.ReceivedCalls());
            Assert.Empty(cde.ReceivedCalls()); Assert.Empty(token.ReceivedCalls());
        }
        finally
        {
            reader.Release.TrySetResult();
            main.Dispose(); window.Close(); root.Delete(true);
        }
    }

    private static string WriteCatalog(string root, HubDto hub, ProjectDto project, long revision, bool withTitle)
    {
        var dir = Directory.CreateDirectory(Path.Combine(root, hub.Name, project.Name, "Project Files", "00 - PROJECT Files", "Project-Template"));
        var path = Path.Combine(dir.FullName, ProjectManifest.FileName);
        ManifestFolder Folder(string name, int id)
        {
            List<MetadataFieldDisplayDto> definitions = [new("Description", "STRING", null, true, [], null, id)];
            var values = new Dictionary<int,string?> { [id] = "Drawing " + name };
            if (withTitle) { definitions.Add(new("Title", "STRING", null, true, [], null, id+1)); values[id+1] = "Separate title"; }
            var item = new ItemDto { Id = name, ItemUrn = "urn:" + name, TipVersionUrn = "tip:" + name, Name = name + ".rvt", ParentId = name, Type = ItemType.File, PathInProject = name + "/" + name + ".rvt" };
            return new(name, name, null, name, definitions, [new(item, values)]);
        }
        File.WriteAllText(path + ".tmp", ProjectManifestSerializer.Serialize(new(1, hub.Id, project.Id, revision,
            DateTimeOffset.UtcNow, [Folder("A", 1), Folder("B", 2)])));
        File.Move(path + ".tmp", path, true);
        return path;
    }

    private sealed class DelayedRead(ILocalManifestReader inner) : ILocalManifestReader
    {
        public bool DelayNext;
        public TaskCompletionSource? NextWatchReady;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken DelayedToken;
        public async Task<LocalManifestSnapshot> ReadAsync(LocalManifestProject project, CancellationToken ct)
        {
            var result = await inner.ReadAsync(project, ct);
            if (DelayNext) { DelayedToken = ct; Started.TrySetResult(); await Release.Task; }
            return result;
        }
        public async IAsyncEnumerable<LocalManifestSnapshot> WatchAsync(LocalManifestProject project,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var ready = Interlocked.Exchange(ref NextWatchReady, null);
            await foreach (var snapshot in inner.WatchAsync(project, ct))
            {
                ready?.TrySetResult();
                yield return snapshot;
            }
        }
    }

    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate()) await Task.Delay(20, timeout.Token);
    }
}
