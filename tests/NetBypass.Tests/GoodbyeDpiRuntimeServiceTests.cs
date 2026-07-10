using NetBypass.Core.Services;
using Xunit;

namespace NetBypass.Tests;

public sealed class GoodbyeDpiRuntimeServiceTests
{
    [Fact]
    public async Task EnableAsync_WritesBlacklistAndStartsCompatibleProcess()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var executableDirectory = Path.Combine(directory, "engine");
        Directory.CreateDirectory(executableDirectory);
        File.WriteAllText(Path.Combine(executableDirectory, "goodbyedpi.exe"), "demo");
        var installService = new GoodbyeDpiInstallService(executableDirectory);
        var runner = new FakeCommandRunner();
        var runtime = new GoodbyeDpiRuntimeService(
            installService,
            Path.Combine(directory, "runtime"),
            runner);

        var result = await runtime.EnableAsync(["youtube", "discord"]);

        Assert.True(result.IsStarted);
        Assert.Contains("--blacklist", runner.StartArguments);
        Assert.Contains("-5", runner.StartArguments);
        Assert.DoesNotContain("-9", runner.StartArguments);
        Assert.Contains("--dns-addr", runner.StartArguments);
        Assert.Contains("77.88.8.8", runner.StartArguments);
        Assert.True(runtime.IsEnabled());
        var blacklist = File.ReadAllLines(runtime.BlacklistPath);
        Assert.Contains("youtube.com", blacklist);
        Assert.Contains("discord.com", blacklist);
    }

    [Fact]
    public async Task DisableAsync_RemovesRuntimeFilesAndStopsProcess()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var executableDirectory = Path.Combine(directory, "engine");
        Directory.CreateDirectory(executableDirectory);
        File.WriteAllText(Path.Combine(executableDirectory, "goodbyedpi.exe"), "demo");
        var installService = new GoodbyeDpiInstallService(executableDirectory);
        var runner = new FakeCommandRunner();
        var runtime = new GoodbyeDpiRuntimeService(
            installService,
            Path.Combine(directory, "runtime"),
            runner);
        await runtime.EnableAsync(["youtube"]);

        var result = await runtime.DisableAsync();

        Assert.True(result.IsStopped);
        Assert.False(runtime.IsEnabled());
        Assert.Contains(GoodbyeDpiRuntimeService.LegacyTaskName, runner.RunArguments);
        Assert.Contains("WinDivert1.4", runner.RunArguments);
        Assert.Contains("delete", runner.RunArguments);
        Assert.EndsWith("goodbyedpi.exe", runner.StoppedPath);
    }

    [Fact]
    public void BuildBlacklist_IgnoresUnknownServices()
    {
        var blacklist = GoodbyeDpiRuntimeService.BuildBlacklist(["youtube", "unknown"]);

        Assert.Contains("youtube.com", blacklist);
        Assert.DoesNotContain("unknown", blacklist);
    }

    [Fact]
    public async Task EnableAsync_ElevatesEngineWhenAppIsNotAdministrator()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var executableDirectory = Path.Combine(directory, "engine");
        Directory.CreateDirectory(executableDirectory);
        File.WriteAllText(Path.Combine(executableDirectory, "goodbyedpi.exe"), "demo");
        var installService = new GoodbyeDpiInstallService(executableDirectory);
        var runner = new FakeCommandRunner { IsAdmin = false };
        var runtime = new GoodbyeDpiRuntimeService(
            installService,
            Path.Combine(directory, "runtime"),
            runner);

        var result = await runtime.EnableAsync(["youtube"]);

        Assert.True(result.IsStarted);
        Assert.True(runner.WasStartedElevated);
    }

    [Fact]
    public async Task EnableAsync_CleansRuntimeFilesWhenProcessExitsImmediately()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var executableDirectory = Path.Combine(directory, "engine");
        Directory.CreateDirectory(executableDirectory);
        File.WriteAllText(Path.Combine(executableDirectory, "goodbyedpi.exe"), "demo");
        var installService = new GoodbyeDpiInstallService(executableDirectory);
        var runner = new FakeCommandRunner
        {
            IsAdmin = true,
            StartExitCode = 1
        };
        var runtime = new GoodbyeDpiRuntimeService(
            installService,
            Path.Combine(directory, "runtime"),
            runner);

        var result = await runtime.EnableAsync(["youtube"]);

        Assert.False(result.IsStarted);
        Assert.False(runtime.IsEnabled());
        Assert.False(File.Exists(runtime.BlacklistPath));
    }


    private sealed class FakeCommandRunner : ISystemCommandRunner
    {
        public List<string> RunArguments { get; } = [];
        public List<string> StartArguments { get; } = [];
        public bool IsAdmin { get; init; } = true;
        public bool WasRunElevated { get; private set; }
        public bool WasStartedElevated { get; private set; }
        public bool IsProcessAlive { get; private set; }
        public int StartExitCode { get; init; }
        public string StoppedPath { get; private set; } = string.Empty;

        public bool IsAdministrator() => IsAdmin;

        public Task<CommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            bool requireAdministrator = false)
        {
            WasRunElevated = requireAdministrator;
            RunArguments.AddRange(arguments);
            return Task.FromResult(new CommandResult(0, string.Empty, 1));
        }

        public Task<CommandResult> RunPowerShellScriptAsync(
            string scriptPath,
            CancellationToken cancellationToken,
            bool requireAdministrator = false)
        {
            WasRunElevated = requireAdministrator;
            RunArguments.Add(scriptPath);
            return Task.FromResult(new CommandResult(0, string.Empty, 1));
        }

        public Task<CommandResult> StartDetachedAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            CancellationToken cancellationToken,
            bool requireAdministrator = false)
        {
            WasStartedElevated = requireAdministrator;
            StartArguments.AddRange(arguments);
            IsProcessAlive = StartExitCode == 0;
            return Task.FromResult(new CommandResult(StartExitCode, string.Empty, 2));
        }

        public void StopProcessesByPath(string executablePath)
        {
            StoppedPath = executablePath;
            IsProcessAlive = false;
        }

        public bool IsProcessRunning(string executablePath) => IsProcessAlive;
    }
}
