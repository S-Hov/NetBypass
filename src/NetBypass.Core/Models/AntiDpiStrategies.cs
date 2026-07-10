namespace NetBypass.Core.Models;

public sealed record AntiDpiStrategyCatalog(
    int SchemaVersion,
    int CatalogVersion,
    string Engine,
    string EngineVersion,
    IReadOnlyList<AntiDpiStrategyProfile> Profiles,
    IReadOnlyList<AntiDpiProbeTarget> Targets);

public sealed record AntiDpiStrategyProfile(
    string Id,
    string Name,
    IReadOnlyList<string> Arguments,
    int Priority,
    string Risk,
    bool SupportsQuic = false);

public sealed record AntiDpiProbeTarget(
    string ServiceId,
    string Name,
    string Host,
    int Port,
    HashSet<int> AcceptedHttpStatuses,
    bool IsControl = false,
    List<string>? CandidateHosts = null);

public sealed record AntiDpiTargetProbeResult(
    string ServiceId,
    string Name,
    bool IsControl,
    bool IsReachable,
    bool IsHttpSuccessful,
    string? Address,
    TimeSpan? TcpLatency,
    TimeSpan? TlsLatency,
    string Message);

public sealed record AntiDpiStrategyAttempt(
    string ProfileId,
    string ProfileName,
    bool ProcessStarted,
    bool IsViable,
    int Score,
    IReadOnlyList<AntiDpiTargetProbeResult> Targets,
    string Message);

public sealed record AntiDpiStrategySelection(
    int SchemaVersion,
    int CatalogVersion,
    string Engine,
    string EngineVersion,
    string ProfileId,
    IReadOnlyList<string> ServiceIds,
    int Score,
    DateTimeOffset VerifiedAt,
    Dictionary<string, string>? Addresses = null);

public sealed record AntiDpiOptimizationResult(
    bool IsSuccessful,
    string Message,
    AntiDpiStrategyProfile? Profile,
    bool UsedSavedSelection,
    IReadOnlyList<AntiDpiStrategyAttempt> Attempts,
    IReadOnlyDictionary<string, string>? Addresses = null);
