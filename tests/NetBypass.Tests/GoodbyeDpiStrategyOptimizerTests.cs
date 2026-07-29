using NetBypass.Core.Models;
using NetBypass.Core.Services;
using Xunit;

namespace NetBypass.Tests;

public sealed class GoodbyeDpiStrategyOptimizerTests
{
    [Fact]
    public async Task EnableBestAsync_SelectsWorkingFallbackProfile()
    {
        var fixture = new OptimizerFixture();
        var optimizer = fixture.CreateOptimizer();

        var result = await optimizer.EnableBestAsync(["youtube"]);

        Assert.True(result.IsSuccessful);
        Assert.False(result.UsedSavedSelection);
        Assert.Equal("working", result.Profile?.Id);
        Assert.Equal(2, result.Attempts.Count);
        Assert.False(result.Attempts[0].IsViable);
        Assert.True(result.Attempts[1].IsViable);
        Assert.Contains("-6", fixture.Runner.LastStartArguments);
        Assert.Equal("working", fixture.Store.Load()?.ProfileId);
        Assert.Equal("203.0.113.10", result.Addresses?["youtube"]);
        Assert.Equal("203.0.113.10", fixture.Store.Load()?.Addresses?["youtube"]);
    }

    [Fact]
    public async Task EnableBestAsync_ReusesVerifiedSavedProfile()
    {
        var fixture = new OptimizerFixture();
        fixture.Store.Save(new AntiDpiStrategySelection(
            1,
            fixture.Catalog.CatalogVersion,
            fixture.Catalog.Engine,
            fixture.Catalog.EngineVersion,
            "working",
            ["youtube"],
            100,
            DateTimeOffset.UtcNow));
        var optimizer = fixture.CreateOptimizer();

        var result = await optimizer.EnableBestAsync(["youtube"]);

        Assert.True(result.IsSuccessful);
        Assert.True(result.UsedSavedSelection);
        Assert.Equal("working", result.Profile?.Id);
        Assert.Single(result.Attempts);
        Assert.Equal(1, fixture.Probe.CallCount);
        Assert.Equal("203.0.113.10", fixture.Store.Load()?.Addresses?["youtube"]);
    }

    [Fact]
    public void CatalogService_RejectsUnknownArgumentTemplate()
    {
        var catalog = CreateCatalog() with
        {
            Profiles =
            [
                new AntiDpiStrategyProfile(
                    "invalid",
                    "Invalid",
                    ["-5", "{unknown}"],
                    1,
                    "low")
            ]
        };

        Assert.Throws<InvalidDataException>(() =>
            AntiDpiStrategyCatalogService.Validate(catalog));
    }

    [Fact]
    public void CatalogService_LoadsBundledGoodbyeDpiCatalog()
    {
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "NetBypass.App",
            "EngineProfiles",
            "goodbyedpi.json");

        var catalog = new AntiDpiStrategyCatalogService().Load(path);

        Assert.Equal("goodbyedpi", catalog.Engine);
        Assert.Equal("0.2.2", catalog.EngineVersion);
        Assert.True(catalog.Profiles.Count >= 6);
        Assert.Contains(catalog.Targets, target => target.ServiceId == "youtube");
        Assert.Contains(catalog.Targets, target => target.IsControl);
    }

    [Fact]
    public void CatalogService_LoadsBundledZapret2Catalog()
    {
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "NetBypass.App",
            "EngineProfiles",
            "zapret2.json");

        var catalog = new AntiDpiStrategyCatalogService().Load(path);

        Assert.Equal("zapret2", catalog.Engine);
        Assert.Equal(Zapret2InstallService.EngineVersion, catalog.EngineVersion);
        Assert.Equal(10, catalog.Profiles.Count);
        Assert.All(catalog.Profiles, profile => Assert.False(profile.SupportsQuic));
        Assert.Contains(catalog.Targets, target => target.ServiceId == "discord");
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "NetBypass.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Не удалось найти корень репозитория NetBypass.");
    }

    private static AntiDpiStrategyCatalog CreateCatalog() =>
        new(
            1,
            7,
            "goodbyedpi",
            "0.2.2",
            [
                new AntiDpiStrategyProfile("blocked", "Blocked", ["-5", "--blacklist", "{blacklist}"], 1, "low"),
                new AntiDpiStrategyProfile("working", "Working", ["-6", "--blacklist", "{blacklist}"], 2, "medium")
            ],
            [
                new AntiDpiProbeTarget("youtube", "YouTube", "youtube.example", 443, new HashSet<int> { 200 }),
                new AntiDpiProbeTarget("control", "Control", "control.example", 443, new HashSet<int> { 200 }, true)
            ]);

    private sealed class OptimizerFixture
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"NetBypass.Tests-{Guid.NewGuid():N}");

        public OptimizerFixture()
        {
            var engineDirectory = Path.Combine(_directory, "engine", "x86_64");
            Directory.CreateDirectory(engineDirectory);
            File.WriteAllText(Path.Combine(engineDirectory, "goodbyedpi.exe"), "demo");
            Catalog = CreateCatalog();
            Runner = new FakeCommandRunner();
            Probe = new ProfileAwareProbe(Runner);
            Store = new AntiDpiStrategySelectionStore(Path.Combine(_directory, "selection.json"));
            var install = new GoodbyeDpiInstallService(Path.Combine(_directory, "engine"));
            Runtime = new GoodbyeDpiRuntimeService(
                install,
                Path.Combine(_directory, "runtime"),
                Runner);
        }

        public AntiDpiStrategyCatalog Catalog { get; }
        public FakeCommandRunner Runner { get; }
        public ProfileAwareProbe Probe { get; }
        public AntiDpiStrategySelectionStore Store { get; }
        public GoodbyeDpiRuntimeService Runtime { get; }

        public GoodbyeDpiStrategyOptimizer CreateOptimizer() =>
            new(Runtime, Catalog, Probe, Store);
    }

    private sealed class ProfileAwareProbe(FakeCommandRunner runner) : IAntiDpiStrategyProbe
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<AntiDpiTargetProbeResult>> ProbeAsync(
            IReadOnlyCollection<string> selectedServiceIds,
            IReadOnlyList<AntiDpiProbeTarget> targets,
            IReadOnlyDictionary<string, string>? preferredAddresses,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var works = runner.LastStartArguments.Contains("-6");
            IReadOnlyList<AntiDpiTargetProbeResult> results = targets.Select(target =>
                new AntiDpiTargetProbeResult(
                    target.ServiceId,
                    target.Name,
                    target.IsControl,
                    target.IsControl || works,
                    target.IsControl || works,
                    "203.0.113.10",
                    TimeSpan.FromMilliseconds(5),
                    TimeSpan.FromMilliseconds(10),
                    target.IsControl || works ? "ok" : "blocked")).ToArray();
            return Task.FromResult(results);
        }
    }

    private sealed class FakeCommandRunner : ISystemCommandRunner
    {
        public List<string> LastStartArguments { get; } = [];
        private bool _running;

        public bool IsAdministrator() => true;

        public Task<CommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            bool requireAdministrator = false) =>
            Task.FromResult(new CommandResult(0, string.Empty));

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
            bool requireAdministrator = false)
        {
            LastStartArguments.Clear();
            LastStartArguments.AddRange(arguments);
            _running = true;
            return Task.FromResult(new CommandResult(0, string.Empty, 42));
        }

        public void StopProcessesByPath(string executablePath) => _running = false;

        public bool IsProcessRunning(string executablePath) => _running;
    }
}
