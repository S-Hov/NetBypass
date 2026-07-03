using System.Text.Json;
using NetBypass.Core.Models;

namespace NetBypass.Core.Services;

public sealed record EndpointSelection(
    string ServiceId,
    string Address,
    string Host,
    DateTimeOffset SelectedAt,
    string Reason);

public sealed class EndpointSelectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public EndpointSelectionStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetBypass",
            "endpoint-selections.json");
    }

    public IReadOnlyDictionary<string, EndpointSelection> Load()
    {
        if (!File.Exists(_path))
            return new Dictionary<string, EndpointSelection>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var selections = JsonSerializer.Deserialize<IReadOnlyList<EndpointSelection>>(
                File.ReadAllText(_path),
                JsonOptions) ?? [];

            return selections
                .GroupBy(item => item.ServiceId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(item => item.SelectedAt).First(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, EndpointSelection>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SaveFromDiagnostics(IEnumerable<ServiceDiagnosticResult> results)
    {
        var existing = Load().ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);

        foreach (var result in results.Where(item => item.IsReachable))
        {
            var selectedAddress = result.SelectedAddress ?? result.TargetAddress;
            var candidate = result.Candidates?
                .FirstOrDefault(item => string.Equals(
                    item.Address,
                    selectedAddress,
                    StringComparison.OrdinalIgnoreCase));
            var host = candidate?.Host
                ?? result.Probes.FirstOrDefault(probe => probe.Stage == ProbeStage.Tls)?.Message
                ?? result.ServiceName;

            existing[result.ServiceId] = new EndpointSelection(
                result.ServiceId,
                selectedAddress,
                host,
                result.CheckedAt,
                result.SelectionReason ?? result.Summary);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(
            _path,
            JsonSerializer.Serialize(
                existing.Values.OrderBy(item => item.ServiceId, StringComparer.OrdinalIgnoreCase),
                JsonOptions));
    }
}
