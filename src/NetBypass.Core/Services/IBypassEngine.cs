using NetBypass.Core.Models;

namespace NetBypass.Core.Services;

public interface IBypassEngine
{
    string Id { get; }
    string DisplayName { get; }
    BypassEngineKind Kind { get; }
    IReadOnlyList<string> SupportedServiceIds { get; }
    IReadOnlyList<EngineProfile> Profiles { get; }

    Task<EngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);
    Task<EngineRunResult> StartAsync(EngineProfile profile, CancellationToken cancellationToken);
    Task<BypassEngineState> GetStatusAsync(CancellationToken cancellationToken);
    Task<EngineStopResult> StopAsync(CancellationToken cancellationToken);
    Task<EngineCleanupResult> CleanupAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<EngineLogEntry>> GetLogsAsync(CancellationToken cancellationToken);
}
