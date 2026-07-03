using NetBypass.Core.Models;
using NetBypass.Core.Services;
using Xunit;

namespace NetBypass.Tests;

public sealed class BypassEngineTests
{
    [Fact]
    public async Task FakeBypassEngine_RunsStopsAndCleansUp()
    {
        IBypassEngine engine = new FakeBypassEngine();
        var profile = Assert.Single(engine.Profiles);

        var availability = await engine.CheckAvailabilityAsync(CancellationToken.None);
        var started = await engine.StartAsync(profile, CancellationToken.None);
        var runningStatus = await engine.GetStatusAsync(CancellationToken.None);
        var stopped = await engine.StopAsync(CancellationToken.None);
        var cleanup = await engine.CleanupAsync(CancellationToken.None);
        var logs = await engine.GetLogsAsync(CancellationToken.None);

        Assert.Equal(BypassEngineKind.AntiDpi, engine.Kind);
        Assert.Equal(BypassEngineState.Available, availability.State);
        Assert.True(started.IsStarted);
        Assert.Equal(BypassEngineState.Running, runningStatus);
        Assert.True(stopped.IsStopped);
        Assert.True(cleanup.IsClean);
        Assert.NotEmpty(logs);
    }
}
