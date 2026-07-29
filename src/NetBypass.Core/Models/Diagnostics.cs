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

public sealed record NetworkDiagnosticProgress(
    string ServiceId,
    string ServiceName,
    ProbeStage Stage,
    ProbeStatus? Status,
    string Message);

public sealed record ServiceDiagnosticResult(
    string ServiceId,
    string ServiceName,
    string TargetAddress,
    bool IsReachable,
    IReadOnlyList<string> ResolvedAddresses,
    IReadOnlyList<ProbeResult> Probes,
    DateTimeOffset CheckedAt,
    string? SelectedAddress = null,
    string? SelectionReason = null,
    bool UsedPreviousSelection = false,
    IReadOnlyList<EndpointCandidateResult>? Candidates = null,
    int AttemptCount = 1,
    int MaximumAttempts = 1)
{
    private string BaseSummary => IsReachable
        ? SelectionReason ?? "TCP и TLS доступны"
        : Probes.LastOrDefault(probe => probe.Status == ProbeStatus.Failed)?.Message
          ?? "Проверка не пройдена";

    public string Summary => IsReachable && AttemptCount > 1
        ? $"{BaseSummary} Сервис доступен с {AttemptCount}-й попытки."
        : !IsReachable && MaximumAttempts > 1
            ? $"{BaseSummary} Выполнено попыток: {AttemptCount} из {MaximumAttempts}."
            : BaseSummary;
}

public sealed record DiagnosticSnapshot(
    DateTimeOffset CreatedAt,
    IReadOnlyList<ServiceDiagnosticResult> Services);

public sealed record EndpointCandidateResult(
    string Address,
    string Host,
    bool IsReachable,
    bool IsPreviousSelection,
    TimeSpan? TcpLatency,
    TimeSpan? TlsLatency,
    string Reason);
