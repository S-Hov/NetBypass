using System.Net;
using NetBypass.Core.Models;

namespace NetBypass.Core.Services;

public interface IAntiDpiStrategyProbe
{
    Task<IReadOnlyList<AntiDpiTargetProbeResult>> ProbeAsync(
        IReadOnlyCollection<string> selectedServiceIds,
        IReadOnlyList<AntiDpiProbeTarget> targets,
        IReadOnlyDictionary<string, string>? preferredAddresses,
        IProgress<string>? progress,
        CancellationToken cancellationToken);
}

public interface IAntiDpiAddressResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string hostname,
        CancellationToken cancellationToken);
}

public sealed class SystemAntiDpiAddressResolver : IAntiDpiAddressResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string hostname,
        CancellationToken cancellationToken) =>
        await Dns.GetHostAddressesAsync(hostname, cancellationToken);
}

public sealed class GoodbyeDpiStrategyProbe(
    IEndpointProbe? endpointProbe = null,
    IAntiDpiAddressResolver? addressResolver = null,
    int maximumAddressesPerTarget = 24) : IAntiDpiStrategyProbe
{
    private readonly IEndpointProbe _endpointProbe = endpointProbe ?? new EndpointProbe(TimeSpan.FromSeconds(5));
    private readonly IAntiDpiAddressResolver _addressResolver = addressResolver ?? new SystemAntiDpiAddressResolver();

    public async Task<IReadOnlyList<AntiDpiTargetProbeResult>> ProbeAsync(
        IReadOnlyCollection<string> selectedServiceIds,
        IReadOnlyList<AntiDpiProbeTarget> targets,
        IReadOnlyDictionary<string, string>? preferredAddresses,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var selected = selectedServiceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var effectiveTargets = targets
            .Where(target => target.IsControl || selected.Contains(target.ServiceId))
            .ToArray();
        var results = new List<AntiDpiTargetProbeResult>(effectiveTargets.Length);

        foreach (var target in effectiveTargets)
        {
            string? preferredAddress = null;
            preferredAddresses?.TryGetValue(target.ServiceId, out preferredAddress);
            results.Add(await ProbeTargetAsync(
                target,
                preferredAddress,
                progress,
                cancellationToken));
        }

        return results;
    }

    private async Task<AntiDpiTargetProbeResult> ProbeTargetAsync(
        AntiDpiProbeTarget target,
        string? preferredAddress,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var addresses = new List<IPAddress>();
        if (IPAddress.TryParse(preferredAddress, out var preferred))
            addresses.Add(preferred);

        foreach (var hostname in new[] { target.Host }
                     .Concat(target.CandidateHosts ?? [])
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                addresses.AddRange((await _addressResolver.ResolveAsync(hostname, cancellationToken))
                    .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork));
            }
            catch (System.Net.Sockets.SocketException)
            {
                // Остальные источники кандидатов могут быть доступны.
            }
        }

        addresses = addresses
            .Distinct()
            .Take(maximumAddressesPerTarget)
            .ToList();
        if (addresses.Count == 0)
            return Failed(target, null, "DNS не вернул IPv4-адресов.");

        progress?.Report($"{target.Name}: проверяем edge-адресов — {addresses.Count}.");
        var checks = await Task.WhenAll(addresses.Select(address =>
            ProbeAddressAsync(target, address, cancellationToken)));
        var selected = checks
            .Where(result => result.IsReachable)
            .OrderByDescending(result => result.IsHttpSuccessful)
            .ThenBy(result => (result.TcpLatency ?? TimeSpan.MaxValue)
                              + (result.TlsLatency ?? TimeSpan.MaxValue))
            .FirstOrDefault();
        if (selected is not null)
        {
            progress?.Report($"{target.Name}: найден рабочий IP {selected.Address}.");
            return selected;
        }

        return checks.LastOrDefault()
               ?? Failed(target, null, "Проверка не пройдена.");
    }

    private async Task<AntiDpiTargetProbeResult> ProbeAddressAsync(
        AntiDpiProbeTarget target,
        IPAddress address,
        CancellationToken cancellationToken)
    {
        var definition = new HealthCheckDefinition(
            address.ToString(),
            target.Host,
            target.Port,
            "https",
            target.AcceptedHttpStatuses);
        var probes = await _endpointProbe.ProbeAsync(definition, address, cancellationToken);
        var tcp = probes.FirstOrDefault(probe => probe.Stage == ProbeStage.Tcp);
        var tls = probes.FirstOrDefault(probe => probe.Stage == ProbeStage.Tls);
        var http = probes.FirstOrDefault(probe => probe.Stage == ProbeStage.Http);
        var reachable = tcp?.Status == ProbeStatus.Success && tls?.Status == ProbeStatus.Success;
        var httpSuccessful = http?.Status == ProbeStatus.Success;
        var message = reachable
            ? httpSuccessful
                ? "TCP, TLS и HTTP доступны."
                : "TCP и TLS доступны; HTTP вернул нестандартный ответ."
            : probes.LastOrDefault(probe => probe.Status == ProbeStatus.Failed)?.Message
              ?? "Проверка не пройдена.";
        return new AntiDpiTargetProbeResult(
            target.ServiceId,
            target.Name,
            target.IsControl,
            reachable,
            httpSuccessful,
            address.ToString(),
            tcp?.Latency,
            tls?.Latency,
            message);
    }

    private static AntiDpiTargetProbeResult Failed(
        AntiDpiProbeTarget target,
        string? address,
        string message) =>
        new(
            target.ServiceId,
            target.Name,
            target.IsControl,
            false,
            false,
            address,
            null,
            null,
            message);
}
