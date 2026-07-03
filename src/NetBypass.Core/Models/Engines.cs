namespace NetBypass.Core.Models;

public enum BypassEngineKind
{
    AntiDpi
}

public enum BypassEngineState
{
    NotInstalled,
    Available,
    Running,
    Stopped,
    Unavailable
}

public sealed record EngineProfile(
    string Id,
    string Name,
    IReadOnlyList<string> ServiceIds,
    IReadOnlyList<string> Arguments);

public sealed record EngineAvailability(
    BypassEngineState State,
    string Message,
    string? ExecutablePath = null);

public sealed record EngineRunResult(
    bool IsStarted,
    string Message,
    int? ProcessId = null);

public sealed record EngineStopResult(
    bool IsStopped,
    string Message);

public sealed record EngineCleanupResult(
    bool IsClean,
    IReadOnlyList<string> CompletedChecks,
    IReadOnlyList<string> Issues);

public sealed record EngineLogEntry(
    DateTimeOffset CreatedAt,
    string Level,
    string Message);
