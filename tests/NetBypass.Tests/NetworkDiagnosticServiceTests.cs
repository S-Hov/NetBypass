using System.Net;
using NetBypass.Core.Models;
using NetBypass.Core.Services;
using Xunit;

namespace NetBypass.Tests;

public sealed class NetworkDiagnosticServiceTests
{
    private static readonly ServiceProfile Profile = new(
        1,
        new ServiceModule(
            "demo",
            "Demo",
            "Test",
            false,
            [new HostEntry("203.0.113.10", "demo.example")],
            "demo.hosts"),
        ["adaptive-hosts"],
        [new HealthCheckDefinition(
            "203.0.113.10",
            "demo.example",
            443,
            "https",
            Enumerable.Range(200, 300).ToHashSet())],
        []);

    [Fact]
    public async Task DiagnoseAsync_WhenTcpAndTlsSucceed_ReturnsReachable()
    {
        var service = new NetworkDiagnosticService(
            new FakeResolver([IPAddress.Parse("104.18.1.1")]),
            new FakeProbe(
            [
                Result(ProbeStage.Tcp, ProbeStatus.Success),
                Result(ProbeStage.Tls, ProbeStatus.Success),
                Result(ProbeStage.Http, ProbeStatus.Warning)
            ]));

        var result = await service.DiagnoseAsync(Profile);

        Assert.True(result.IsReachable);
        Assert.Equal("203.0.113.10", result.TargetAddress);
        Assert.Equal("104.18.1.1", Assert.Single(result.ResolvedAddresses));
    }

    [Fact]
    public async Task DiagnoseAsync_WhenTlsFails_ReturnsUnavailable()
    {
        var service = new NetworkDiagnosticService(
            new FakeResolver([]),
            new FakeProbe(
            [
                Result(ProbeStage.Tcp, ProbeStatus.Success),
                Result(ProbeStage.Tls, ProbeStatus.Failed)
            ]));

        var result = await service.DiagnoseAsync(Profile);

        Assert.False(result.IsReachable);
        Assert.Contains(result.Probes, probe =>
            probe.Stage == ProbeStage.Dns && probe.Status == ProbeStatus.Warning);
    }

    [Fact]
    public async Task DiagnoseAsync_WhenPreviousSelectionWorks_ReusesIt()
    {
        var profile = Profile with
        {
            HealthChecks =
            [
                new HealthCheckDefinition(
                    "203.0.113.10",
                    "demo.example",
                    443,
                    "https",
                    Enumerable.Range(200, 300).ToHashSet()),
                new HealthCheckDefinition(
                    "203.0.113.20",
                    "demo.example",
                    443,
                    "https",
                    Enumerable.Range(200, 300).ToHashSet())
            ]
        };
        var probe = new AddressProbe(new Dictionary<string, IReadOnlyList<ProbeResult>>
        {
            ["203.0.113.20"] =
            [
                Result(ProbeStage.Tcp, ProbeStatus.Success),
                Result(ProbeStage.Tls, ProbeStatus.Success)
            ]
        });
        var service = new NetworkDiagnosticService(
            new FakeResolver([]),
            probe);

        var result = await service.DiagnoseAsync(
            profile,
            new EndpointSelection(
                "demo",
                "203.0.113.20",
                "demo.example",
                DateTimeOffset.UtcNow.AddHours(-1),
                "previous"));

        Assert.True(result.IsReachable);
        Assert.True(result.UsedPreviousSelection);
        Assert.Equal("203.0.113.20", result.SelectedAddress);
        Assert.Equal(["203.0.113.20"], probe.CheckedAddresses);
    }

    [Fact]
    public async Task DiagnoseAsync_WhenPreviousSelectionFails_SelectsReachableCandidate()
    {
        var profile = Profile with
        {
            HealthChecks =
            [
                new HealthCheckDefinition(
                    "203.0.113.10",
                    "demo.example",
                    443,
                    "https",
                    Enumerable.Range(200, 300).ToHashSet()),
                new HealthCheckDefinition(
                    "203.0.113.20",
                    "demo.example",
                    443,
                    "https",
                    Enumerable.Range(200, 300).ToHashSet())
            ]
        };
        var probe = new AddressProbe(new Dictionary<string, IReadOnlyList<ProbeResult>>
        {
            ["203.0.113.10"] =
            [
                Result(ProbeStage.Tcp, ProbeStatus.Failed)
            ],
            ["203.0.113.20"] =
            [
                Result(ProbeStage.Tcp, ProbeStatus.Success),
                Result(ProbeStage.Tls, ProbeStatus.Success)
            ]
        });
        var service = new NetworkDiagnosticService(
            new FakeResolver([]),
            probe);

        var result = await service.DiagnoseAsync(
            profile,
            new EndpointSelection(
                "demo",
                "203.0.113.10",
                "demo.example",
                DateTimeOffset.UtcNow.AddHours(-1),
                "previous"));

        Assert.True(result.IsReachable);
        Assert.False(result.UsedPreviousSelection);
        Assert.Equal("203.0.113.20", result.SelectedAddress);
        Assert.Contains("Прошлый адрес не прошёл проверку", result.SelectionReason);
    }

    [Fact]
    public void ServiceProfileLoader_CreatesVersionedProfile()
    {
        var profile = ServiceProfileLoader.CreateProfile(Profile.Module);

        Assert.Equal(1, profile.SchemaVersion);
        Assert.Equal("demo.example", Assert.Single(profile.HealthChecks).Host);
        Assert.Contains("adaptive-hosts", profile.Strategies);
        Assert.Empty(profile.RelayCandidates);
    }

    [Fact]
    public void ServiceProfileLoader_CreatesCheckForEveryUniqueAddress()
    {
        var module = Profile.Module with
        {
            Entries =
            [
                new HostEntry("203.0.113.10", "one.example"),
                new HostEntry("203.0.113.10", "two.example"),
                new HostEntry("203.0.113.20", "three.example")
            ]
        };

        var profile = ServiceProfileLoader.CreateProfile(module);

        Assert.Equal(2, profile.HealthChecks.Count);
        Assert.Contains(profile.HealthChecks, check =>
            check.TargetAddress == "203.0.113.20"
            && check.Host == "three.example");
    }

    [Fact]
    public void DiagnosticStore_RoundTripsSnapshot()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"NetBypass.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "diagnostics.json");

        try
        {
            var store = new DiagnosticStore(path);
            var result = new ServiceDiagnosticResult(
                "demo",
                "Demo",
                "203.0.113.10",
                true,
                ["104.18.1.1"],
                [Result(ProbeStage.Tls, ProbeStatus.Success)],
                DateTimeOffset.UtcNow);
            store.Save(new DiagnosticSnapshot(DateTimeOffset.UtcNow, [result]));

            var loaded = store.Load();

            Assert.NotNull(loaded);
            Assert.True(Assert.Single(loaded.Services).IsReachable);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EndpointSelectionStore_SavesReachableSelections()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"NetBypass.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "endpoint-selections.json");

        try
        {
            var store = new EndpointSelectionStore(path);
            store.SaveFromDiagnostics(
            [
                new ServiceDiagnosticResult(
                    "demo",
                    "Demo",
                    "203.0.113.20",
                    true,
                    [],
                    [Result(ProbeStage.Tls, ProbeStatus.Success)],
                    DateTimeOffset.UtcNow,
                    "203.0.113.20",
                    "selected",
                    false,
                    [new EndpointCandidateResult(
                        "203.0.113.20",
                        "demo.example",
                        true,
                        false,
                        TimeSpan.FromMilliseconds(10),
                        TimeSpan.FromMilliseconds(20),
                        "TCP и TLS доступны")])
            ]);

            var selections = store.Load();

            var selection = Assert.Single(selections).Value;
            Assert.Equal("203.0.113.20", selection.Address);
            Assert.Equal("demo.example", selection.Host);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ProbeResult Result(ProbeStage stage, ProbeStatus status) =>
        new(
            stage,
            status,
            TimeSpan.FromMilliseconds(10),
            "203.0.113.10",
            null,
            status.ToString(),
            DateTimeOffset.UtcNow);

    private sealed class FakeResolver(IReadOnlyList<IPAddress> addresses) : IDohResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(
            string hostname,
            CancellationToken cancellationToken) =>
            Task.FromResult(addresses);
    }

    private sealed class FakeProbe(IReadOnlyList<ProbeResult> results) : IEndpointProbe
    {
        public Task<IReadOnlyList<ProbeResult>> ProbeAsync(
            HealthCheckDefinition healthCheck,
            IPAddress targetAddress,
            CancellationToken cancellationToken) =>
            Task.FromResult(results);
    }

    private sealed class AddressProbe(
        IReadOnlyDictionary<string, IReadOnlyList<ProbeResult>> resultsByAddress) : IEndpointProbe
    {
        private readonly List<string> _checkedAddresses = [];

        public IReadOnlyList<string> CheckedAddresses => _checkedAddresses;

        public Task<IReadOnlyList<ProbeResult>> ProbeAsync(
            HealthCheckDefinition healthCheck,
            IPAddress targetAddress,
            CancellationToken cancellationToken)
        {
            var address = targetAddress.ToString();
            _checkedAddresses.Add(address);
            return Task.FromResult(resultsByAddress.TryGetValue(address, out var result)
                ? result
                : [Result(ProbeStage.Tcp, ProbeStatus.Failed)]);
        }
    }
}
