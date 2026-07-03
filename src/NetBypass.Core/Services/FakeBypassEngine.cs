using NetBypass.Core.Models;

namespace NetBypass.Core.Services;

public sealed class FakeBypassEngine : IBypassEngine
{
    private readonly List<EngineLogEntry> _logs = [];
    private BypassEngineState _state = BypassEngineState.Available;

    public string Id => "fake";
    public string DisplayName => "Тестовый движок";
    public BypassEngineKind Kind => BypassEngineKind.AntiDpi;
    public IReadOnlyList<string> SupportedServiceIds { get; } = ["openai", "discord", "youtube"];
    public IReadOnlyList<EngineProfile> Profiles { get; } =
    [
        new EngineProfile(
            "fake-basic",
            "Тестовый Anti-DPI профиль",
            ["openai", "discord", "youtube"],
            [])
    ];

    public Task<EngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new EngineAvailability(_state, "Тестовый движок доступен."));

    public Task<EngineRunResult> StartAsync(
        EngineProfile profile,
        CancellationToken cancellationToken)
    {
        _state = BypassEngineState.Running;
        _logs.Add(new EngineLogEntry(
            DateTimeOffset.UtcNow,
            "Info",
            $"Запущен профиль {profile.Name}."));
        return Task.FromResult(new EngineRunResult(true, "Тестовый движок запущен.", 1));
    }

    public Task<BypassEngineState> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_state);

    public Task<EngineStopResult> StopAsync(CancellationToken cancellationToken)
    {
        _state = BypassEngineState.Stopped;
        _logs.Add(new EngineLogEntry(DateTimeOffset.UtcNow, "Info", "Тестовый движок остановлен."));
        return Task.FromResult(new EngineStopResult(true, "Тестовый движок остановлен."));
    }

    public Task<EngineCleanupResult> CleanupAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new EngineCleanupResult(
            true,
            ["Тестовый процесс не запущен.", "Временные файлы не создавались."],
            []));

    public Task<IReadOnlyList<EngineLogEntry>> GetLogsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EngineLogEntry>>(_logs.ToArray());
}
