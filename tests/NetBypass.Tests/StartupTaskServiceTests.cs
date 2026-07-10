using NetBypass.Core.Services;
using Xunit;

namespace NetBypass.Tests;

public sealed class StartupTaskServiceTests
{
    [Fact]
    public async Task SetEnabledAsync_CreatesElevatedBackgroundTask()
    {
        var executable = Path.GetTempFileName();
        var runner = new FakeCommandRunner();
        var service = new StartupTaskService(runner);

        var result = await service.SetEnabledAsync(true, executable);

        Assert.True(result.IsSuccess);
        Assert.Contains("/Create", runner.Arguments);
        Assert.Contains("ONLOGON", runner.Arguments);
        Assert.Contains("HIGHEST", runner.Arguments);
        Assert.Contains(runner.Arguments, argument =>
            argument.Contains(StartupTaskService.BackgroundArgument, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetEnabledAsync_DeletesStartupTask()
    {
        var runner = new FakeCommandRunner();
        var service = new StartupTaskService(runner);

        var result = await service.SetEnabledAsync(false, string.Empty);

        Assert.True(result.IsSuccess);
        Assert.Contains("/Delete", runner.Arguments);
        Assert.Contains(StartupTaskService.TaskName, runner.Arguments);
    }

    private sealed class FakeCommandRunner : ISystemCommandRunner
    {
        public List<string> Arguments { get; } = [];

        public bool IsAdministrator() => true;

        public Task<CommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            bool requireAdministrator = false)
        {
            Arguments.AddRange(arguments);
            return Task.FromResult(new CommandResult(0, string.Empty));
        }

        public Task<CommandResult> RunPowerShellScriptAsync(
            string scriptPath,
            CancellationToken cancellationToken,
            bool requireAdministrator = false) =>
            Task.FromResult(new CommandResult(0, string.Empty));

        public Task<CommandResult> StartDetachedAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            CancellationToken cancellationToken,
            bool requireAdministrator = false) =>
            Task.FromResult(new CommandResult(0, string.Empty));

        public void StopProcessesByPath(string executablePath)
        {
        }

        public bool IsProcessRunning(string executablePath) => false;
    }
}
