using NetBypass.Core.Services;
using Xunit;

namespace NetBypass.Tests;

public sealed class Zapret2RuntimeServiceTests
{
    [Fact]
    public async Task EnableAsync_ValidatesConfigWritesHostlistAndStartsOwnProcess()
    {
        var fixture = CreateFixture();

        var result = await fixture.Runtime.EnableAsync(
            ["youtube", "discord"],
            ["--filter-tcp=443", "--hostlist={hostlist}", "--dpi-desync=multisplit"]);

        Assert.True(result.IsStarted);
        Assert.Contains("--dry-run", fixture.Runner.RunArguments);
        Assert.Contains("--comment=NetBypass", fixture.Runner.StartArguments);
        Assert.Contains("--wf-tcp-out=443", fixture.Runner.StartArguments);
        Assert.Contains(
            fixture.Runner.StartArguments,
            value => value.EndsWith(fixture.Runtime.HostlistPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("youtube.com", File.ReadAllLines(fixture.Runtime.HostlistPath));
        Assert.Contains("discord.com", File.ReadAllLines(fixture.Runtime.HostlistPath));
        Assert.True(fixture.Runtime.IsEnabled());
        Directory.Delete(fixture.Root, recursive: true);
    }

    [Fact]
    public async Task DisableAsync_StopsOnlyInstalledExecutableAndCleansRuntimeFiles()
    {
        var fixture = CreateFixture();
        await fixture.Runtime.EnableAsync(
            ["youtube"],
            ["--filter-tcp=443", "--hostlist={hostlist}", "--dpi-desync=multisplit"]);

        var result = await fixture.Runtime.DisableAsync();

        Assert.True(result.IsStopped);
        Assert.Equal(fixture.Install.ExecutablePath, fixture.Runner.StoppedPath);
        Assert.False(File.Exists(fixture.Runtime.HostlistPath));
        Assert.False(File.Exists(fixture.Runtime.ActiveConfigPath));
        Assert.DoesNotContain(
            fixture.Runner.RunArguments,
            value => value.Contains("delete", StringComparison.OrdinalIgnoreCase));
        Directory.Delete(fixture.Root, recursive: true);
    }

    [Fact]
    public void BuildArguments_RejectsRuntimeOwnedOptions()
    {
        var fixture = CreateFixture();

        Assert.Throws<InvalidDataException>(() => fixture.Runtime.BuildArguments(
            ["--wf-tcp-out=80"],
            fixture.Runtime.HostlistPath));

        Directory.Delete(fixture.Root, recursive: true);
    }

    private static Fixture CreateFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var install = new Zapret2InstallService(Path.Combine(root, "install"));
        var binaries = Path.GetDirectoryName(install.ExecutablePath)!;
        Directory.CreateDirectory(binaries);
        foreach (var file in new[] { "winws2.exe", "cygwin1.dll", "WinDivert.dll", "WinDivert64.sys" })
            File.WriteAllText(Path.Combine(binaries, file), "test");
        Directory.CreateDirectory(Path.GetDirectoryName(install.LuaLibraryPath)!);
        File.WriteAllText(install.LuaLibraryPath, "test");
        File.WriteAllText(install.LuaAntiDpiPath, "test");
        var runner = new FakeCommandRunner();
        return new Fixture(
            root,
            install,
            new Zapret2RuntimeService(install, Path.Combine(root, "runtime"), runner),
            runner);
    }

    private sealed record Fixture(
        string Root,
        Zapret2InstallService Install,
        Zapret2RuntimeService Runtime,
        FakeCommandRunner Runner);

    private sealed class FakeCommandRunner : ISystemCommandRunner
    {
        public List<string> RunArguments { get; } = [];
        public List<string> StartArguments { get; } = [];
        public bool IsAlive { get; private set; }
        public string StoppedPath { get; private set; } = string.Empty;

        public bool IsAdministrator() => true;

        public Task<CommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            bool requireAdministrator = false)
        {
            RunArguments.AddRange(arguments);
            return Task.FromResult(new CommandResult(0, string.Empty, 1));
        }

        public Task<CommandResult> RunPowerShellScriptAsync(
            string scriptPath,
            CancellationToken cancellationToken,
            bool requireAdministrator = false) =>
            Task.FromResult(new CommandResult(0, string.Empty, 1));

        public Task<CommandResult> StartDetachedAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            CancellationToken cancellationToken,
            bool requireAdministrator = false)
        {
            StartArguments.AddRange(arguments);
            IsAlive = true;
            return Task.FromResult(new CommandResult(0, string.Empty, 2));
        }

        public void StopProcessesByPath(string executablePath)
        {
            StoppedPath = executablePath;
            IsAlive = false;
        }

        public bool IsProcessRunning(string executablePath) => IsAlive;
    }
}
