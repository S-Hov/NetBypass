using System.Net;
using NetBypass.Core.Models;

namespace NetBypass.Core.Services;

public interface IAntiDpiStrategyProbe
{
    Task<IReadOnlyList<AntiDpiTargetProbeResult>> ProbeAsync(
        IReadOnlyCollection<string> selectedServiceIds,
        IReadOnlyList<AntiDpiProbeTarget> targets,
        CancellationToken cancellationToken);
}

public sealed class GoodbyeDpiStrategyProbe(
    IEndpointProbe? endpointProbe = null,
    int maximumAddressesPerTarget = 2) : IAntiDpiStrategyProbe
{
    private readonly IEndpointProbe _endpointProbe = endpointProbe ?? new EndpointProbe(TimeSpan.FromSeconds(5));

    public async Task<IReadOnlyList<AntiDpiTargetProbeResult>> ProbeAsync(
        IReadOnlyCollection<string> selectedServiceIds,
        IReadOnlyList<AntiDpiProbeTarget> targets,
        CancellationToken cancellationToken)
    {
        var selected = selectedServiceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var effectiveTargets = targets
            .Where(target => target.IsControl || selected.Contains(target.ServiceId))
            .ToArray();
        var results = new List<AntiDpiTargetProbeResult>(effectiveTargets.Length);

        foreach (var target in effectiveTargets)
        {
            results.Add(await ProbeTargetAsync(target, cancellationToken));
        }

        return results;
    }

    private async Task<AntiDpiTargetProbeResult> ProbeTargetAsync(
        AntiDpiProbeTarget target,
        CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try
        {
            addresses = (await Dns.GetHostAddressesAsync(target.Host, cancellationToken))
                .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Take(maximumAddressesPerTarget)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is System.Net.Sockets.SocketException or OperationCanceledException)
        {
            return Failed(target, null, $"DNS-проверка не пройдена: {exception.Message}");
        }

        if (addresses.Length == 0)
            return Failed(target, null, "DNS не вернул IPv4-адресов.");

        AntiDpiTargetProbeResult? lastFailure = null;
        foreach (var address in addresses)
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
            var result = new AntiDpiTargetProbeResult(
                target.ServiceId,
                target.Name,
                target.IsControl,
                reachable,
                httpSuccessful,
                address.ToString(),
                tcp?.Latency,
                tls?.Latency,
                message);
            if (reachable)
                return result;
            lastFailure = result;
        }

        return lastFailure ?? Failed(target, null, "Проверка не пройдена.");
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
