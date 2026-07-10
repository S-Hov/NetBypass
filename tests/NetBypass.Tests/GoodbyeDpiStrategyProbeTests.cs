using System.Net;
using NetBypass.Core.Models;
using NetBypass.Core.Services;
using Xunit;

namespace NetBypass.Tests;

public sealed class GoodbyeDpiStrategyProbeTests
{
    [Fact]
    public async Task ProbeAsync_SelectsReachableAlternativeEdgeAddress()
    {
        var original = IPAddress.Parse("203.0.113.10");
        var edge = IPAddress.Parse("203.0.113.20");
        var resolver = new FakeAddressResolver(new Dictionary<string, IReadOnlyList<IPAddress>>
        {
            ["www.youtube.com"] = [original],
            ["www.google.com"] = [edge]
        });
        var endpointProbe = new AddressProbe(edge);
        var probe = new GoodbyeDpiStrategyProbe(endpointProbe, resolver);
        var target = new AntiDpiProbeTarget(
            "youtube",
            "YouTube",
            "www.youtube.com",
            443,
            [200],
            CandidateHosts: ["www.google.com"]);

        var results = await probe.ProbeAsync(
            ["youtube"],
            [target],
            preferredAddresses: null,
            progress: null,
            CancellationToken.None);

        var result = Assert.Single(results);
        Assert.True(result.IsReachable);
        Assert.True(result.IsHttpSuccessful);
        Assert.Equal(edge.ToString(), result.Address);
        Assert.Contains(original.ToString(), endpointProbe.CheckedAddresses);
        Assert.Contains(edge.ToString(), endpointProbe.CheckedAddresses);
    }

    private sealed class FakeAddressResolver(
        IReadOnlyDictionary<string, IReadOnlyList<IPAddress>> addresses) : IAntiDpiAddressResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(
            string hostname,
            CancellationToken cancellationToken) =>
            Task.FromResult(addresses.TryGetValue(hostname, out var result) ? result : []);
    }

    private sealed class AddressProbe(IPAddress reachableAddress) : IEndpointProbe
    {
        private readonly List<string> _checkedAddresses = [];
        public IReadOnlyList<string> CheckedAddresses => _checkedAddresses;

        public Task<IReadOnlyList<ProbeResult>> ProbeAsync(
            HealthCheckDefinition healthCheck,
            IPAddress targetAddress,
            CancellationToken cancellationToken)
        {
            _checkedAddresses.Add(targetAddress.ToString());
            var checkedAt = DateTimeOffset.UtcNow;
            if (!targetAddress.Equals(reachableAddress))
            {
                IReadOnlyList<ProbeResult> failed =
                [
                    new(
                        ProbeStage.Tcp,
                        ProbeStatus.Failed,
                        TimeSpan.FromMilliseconds(20),
                        targetAddress.ToString(),
                        "Timeout",
                        "TCP timeout",
                        checkedAt)
                ];
                return Task.FromResult(failed);
            }

            IReadOnlyList<ProbeResult> success =
            [
                new(ProbeStage.Tcp, ProbeStatus.Success, TimeSpan.FromMilliseconds(5), targetAddress.ToString(), null, "ok", checkedAt),
                new(ProbeStage.Tls, ProbeStatus.Success, TimeSpan.FromMilliseconds(10), targetAddress.ToString(), null, "ok", checkedAt),
                new(ProbeStage.Http, ProbeStatus.Success, TimeSpan.FromMilliseconds(10), targetAddress.ToString(), "200", "ok", checkedAt)
            ];
            return Task.FromResult(success);
        }
    }
}
