using System.Text.Json;
using System.Text.Json.Serialization;
using NetBypass.Core.Models;

namespace NetBypass.Core.Services;

public sealed class ServiceProfileLoader(ModuleLoader? moduleLoader = null)
{
    private readonly ModuleLoader _moduleLoader = moduleLoader ?? new ModuleLoader();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public IReadOnlyList<ServiceProfile> LoadDirectory(string directory)
    {
        var profileDirectory = Path.Combine(
            Path.GetDirectoryName(directory) ?? string.Empty,
            "Profiles");

        if (Directory.Exists(profileDirectory))
        {
            var profiles = Directory.EnumerateFiles(
                    profileDirectory,
                    "*.json",
                    SearchOption.TopDirectoryOnly)
                .Select(LoadJsonProfile)
                .OrderBy(profile => profile.Category, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            if (profiles.Length > 0)
                return profiles;
        }

        return _moduleLoader.LoadDirectory(directory)
            .Select(CreateProfile)
            .ToArray();
    }

    public static ServiceProfile CreateProfile(ServiceModule module)
    {
        var healthChecks = module.Entries
            .GroupBy(entry => entry.Address, StringComparer.OrdinalIgnoreCase)
            .Select(group => new HealthCheckDefinition(
                TargetAddress: group.Key,
                Host: group.First().Hostname,
                Port: 443,
                Protocol: "https",
                AcceptedHttpStatuses: Enumerable.Range(200, 300).ToHashSet()))
            .ToArray();

        return new ServiceProfile(
            SchemaVersion: 1,
            Module: module,
            Strategies: ["adaptive-hosts"],
            HealthChecks: healthChecks,
            RelayCandidates: []);
    }

    private static ServiceProfile LoadJsonProfile(string path)
    {
        var config = JsonSerializer.Deserialize<ServiceProfileJson>(
            File.ReadAllText(path),
            JsonOptions) ?? throw new FormatException($"{path}: пустой JSON-профиль.");

        Require(config.SchemaVersion == 1, path, "поддерживается только schemaVersion: 1.");
        Require(!string.IsNullOrWhiteSpace(config.Id), path, "отсутствует поле id.");
        Require(!string.IsNullOrWhiteSpace(config.Name), path, "отсутствует поле name.");
        Require(!string.IsNullOrWhiteSpace(config.Category), path, "отсутствует поле category.");
        Require(config.Hosts is { Count: > 0 }, path, "профиль не содержит hosts.");

        var module = new ServiceModule(
            config.Id!,
            config.Name!,
            config.Category!,
            config.Default,
            config.Hosts!.Select(entry => ModuleLoader.CreateHostEntry(
                    path,
                    RequireValue(entry.Address, path, "hosts[].address"),
                    RequireValue(entry.Hostname, path, "hosts[].hostname")))
                .ToArray(),
            path);

        var healthChecks = config.HealthChecks is { Count: > 0 }
            ? config.HealthChecks.Select(check => new HealthCheckDefinition(
                    RequireValue(check.TargetAddress, path, "healthChecks[].targetAddress"),
                    RequireValue(check.Host, path, "healthChecks[].host"),
                    check.Port ?? 443,
                    string.IsNullOrWhiteSpace(check.Protocol) ? "https" : check.Protocol!,
                    (check.AcceptedHttpStatuses is { Count: > 0 }
                        ? check.AcceptedHttpStatuses
                        : Enumerable.Range(200, 300)).ToHashSet()))
                .ToArray()
            : CreateProfile(module).HealthChecks;

        var relayCandidates = (config.RelayCandidates ?? [])
            .Select(candidate => new RelayCandidate(
                RequireValue(candidate.Address, path, "relayCandidates[].address"),
                RequireValue(candidate.Host, path, "relayCandidates[].host"),
                candidate.Port ?? 443,
                string.IsNullOrWhiteSpace(candidate.Protocol) ? "https" : candidate.Protocol!,
                candidate.Priority ?? 100))
            .ToArray();

        return new ServiceProfile(
            config.SchemaVersion,
            module,
            config.Strategies is { Count: > 0 } ? config.Strategies : ["adaptive-hosts"],
            healthChecks,
            relayCandidates);
    }

    private static string RequireValue(string? value, string path, string field) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new FormatException($"{path}: отсутствует поле {field}.");

    private static void Require(bool condition, string path, string message)
    {
        if (!condition)
            throw new FormatException($"{path}: {message}");
    }

    private sealed record ServiceProfileJson(
        int SchemaVersion,
        string? Id,
        string? Name,
        string? Category,
        [property: JsonPropertyName("default")] bool Default,
        IReadOnlyList<string>? Strategies,
        IReadOnlyList<HostEntryJson>? Hosts,
        IReadOnlyList<HealthCheckJson>? HealthChecks,
        IReadOnlyList<RelayCandidateJson>? RelayCandidates);

    private sealed record HostEntryJson(string? Address, string? Hostname);

    private sealed record HealthCheckJson(
        string? TargetAddress,
        string? Host,
        int? Port,
        string? Protocol,
        IReadOnlyList<int>? AcceptedHttpStatuses);

    private sealed record RelayCandidateJson(
        string? Address,
        string? Host,
        int? Port,
        string? Protocol,
        int? Priority);
}
