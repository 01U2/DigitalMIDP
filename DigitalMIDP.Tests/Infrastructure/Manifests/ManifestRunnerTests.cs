using System.IO;
using DigitalMIDP.Application.Manifests;
using DigitalMIDP.Infrastructure.Manifests;
using DigitalMIDP.Tests.UI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace DigitalMIDP.Tests.Infrastructure.Manifests;

public sealed class ManifestRunnerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly IManifestPublisher publisher = Substitute.For<IManifestPublisher>();
    private readonly Clock clock = new();
    private string FilePath => Path.Combine(directory, "enrollment.json");

    private ServiceProvider Build()
    {
        var services = CompositionRootTests.BuildServices();
        services.AddSingleton(new ManifestEnrollmentStore(FilePath));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(publisher);
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task RestartRestoresEnrollmentAndStaggersFirstChecks()
    {
        using (var provider = Build())
        {
            var runner = provider.GetRequiredService<IManifestRunner>();
            await runner.StartAsync();
            await runner.EnableAsync("hub", "a");
            await runner.EnableAsync("hub", "b", TimeSpan.FromMinutes(2));
            await runner.StopAsync();
            clock.Advance(TimeSpan.FromHours(1));
            Assert.Empty(publisher.ReceivedCalls());
        }
        using var restarted = Build();
        var restored = restarted.GetRequiredService<IManifestRunner>();
        await restored.StartAsync();
        var rows = restored.GetSnapshot().OrderBy(r => r.NextCheckUtc).ToArray();
        Assert.Equal(TimeSpan.FromMinutes(10), rows[0].Interval);
        Assert.Equal(TimeSpan.FromMinutes(2), rows[1].Interval);
        Assert.True(rows[0].NextCheckUtc < rows[1].NextCheckUtc);
        await restored.DisableAsync("hub", "a");
        await restored.StopAsync();
        await restored.StartAsync();
        Assert.Equal("b", Assert.Single(restored.GetSnapshot()).ProjectId);
        await restored.StopAsync();
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task TimerAndManualWaitersShareCheckUntilDisabled()
    {
        using var provider = Build();
        var runner = provider.GetRequiredService<IManifestRunner>();
        using var scope = provider.CreateScope();
        Assert.Same(runner, scope.ServiceProvider.GetRequiredService<IManifestRunner>());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        publisher.CheckAsync("h", "p", Arg.Any<CancellationToken>()).Returns(async call =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
            return new ManifestCheckResult(clock.GetUtcNow(), clock.GetUtcNow(), false, 0);
        });
        await runner.StartAsync();
        await runner.EnableAsync("h", "p");
        clock.Advance(TimeSpan.FromSeconds(1));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(Assert.Single(runner.GetSnapshot()).IsChecking);
        var waiter = runner.CheckNowAsync("h", "p");
        using var cancel = new CancellationTokenSource();
        var other = runner.CheckNowAsync("h", "p", cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => other);
        Assert.False(waiter.IsCompleted);
        await runner.DisableAsync("h", "p");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Empty(runner.GetSnapshot());
        Assert.Single(publisher.ReceivedCalls());
    }

    [Fact]
    public async Task FailureDoesNotBlockPeerOrNextScheduledAttempt()
    {
        using var provider = Build();
        var runner = provider.GetRequiredService<IManifestRunner>();
        var result = new ManifestCheckResult(clock.GetUtcNow(), clock.GetUtcNow(), true, 7);
        publisher.CheckAsync("h", "a", Arg.Any<CancellationToken>()).Returns(
            Task.FromException<ManifestCheckResult>(new IOException()), Task.FromResult(result));
        publisher.CheckAsync("h", "b", Arg.Any<CancellationToken>()).Returns(Task.FromResult(result));
        await runner.StartAsync();
        await runner.EnableAsync("h", "a", TimeSpan.FromMinutes(3));
        await runner.EnableAsync("h", "b");
        await Assert.ThrowsAsync<IOException>(() => runner.CheckNowAsync("h", "a"));
        var failed = runner.GetSnapshot().Single(s => s.ProjectId == "a");
        Assert.Equal(nameof(IOException), failed.LastError);
        Assert.Equal(clock.GetUtcNow() + TimeSpan.FromMinutes(3), failed.NextCheckUtc);
        Assert.Equal(result, await runner.CheckNowAsync("h", "b"));
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.StatusChanged += (_, _) =>
        {
            if (runner.GetSnapshot().Any(s => s.ProjectId == "a" && s.LastSuccess == result && s.LastError is null))
                recovered.TrySetResult();
        };
        clock.Advance(TimeSpan.FromMinutes(3) - TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, publisher.ReceivedCalls().Count());
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await runner.StopAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UnixEpoch;
        private readonly List<TestTimer> timers = new();
        public override DateTimeOffset GetUtcNow() { lock (timers) return now; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new TestTimer(this, () => callback(state));
            lock (timers) timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }
        public void Advance(TimeSpan by)
        {
            TestTimer[] ready;
            lock (timers)
            {
                now += by;
                ready = timers.Where(t => t.Due <= now).ToArray();
                foreach (var timer in ready) timer.Due = null;
            }
            foreach (var timer in ready) timer.Fire();
        }
        private sealed class TestTimer(Clock clock, Action fire) : ITimer
        {
            internal DateTimeOffset? Due;
            internal void Fire() => fire();
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Assert.Equal(Timeout.InfiniteTimeSpan, period);
                lock (clock.timers) Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.now + dueTime;
                return true;
            }
            public void Dispose() { lock (clock.timers) clock.timers.Remove(this); }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
