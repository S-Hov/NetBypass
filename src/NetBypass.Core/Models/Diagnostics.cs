namespace NetBypass.Core.Models;

public enum ProbeStage
{
    Dns,
    Tcp,
    Tls,
    Http
}

public enum ProbeStatus
{
    Success,
    Warning,
    Failed,
    Skipped
}

public sealed record ProbeResult(
    ProbeStage Stage,
    ProbeStatus Status,
    TimeSpan? Latency,
    string? Address,
    string? ErrorCode,
    string Message,
    DateTimeOffset CheckedAt);

public sealed record ServiceDiagnosticResult(
    string ServiceId,
    string ServiceName,
    string TargetAddress,
    bool IsReachable,
    IReadOnlyList<string> ResolvedAddresses,
    IReadOnlyList<ProbeResult> Probes,
    DateTimeOffset CheckedAt)
{
    public string Summary => IsReachable
        ? "TCP и TLS доступны"
        : Probes.LastOrDefault(probe => probe.Status == ProbeStatus.Failed)?.Message
          ?? "Проверка не пройдена";
}

public sealed record DiagnosticSnapshot(
    DateTimeOffset CreatedAt,
    IReadOnlyList<ServiceDiagnosticResult> Services);
