using NetBypass.Core.Models;

namespace NetBypass.Core.Services;

public sealed class GoodbyeDpiStrategyOptimizer(
    GoodbyeDpiRuntimeService runtimeService,
    AntiDpiStrategyCatalog catalog,
    IAntiDpiStrategyProbe? probe = null,
    AntiDpiStrategySelectionStore? selectionStore = null)
{
    private readonly IAntiDpiStrategyProbe _probe = probe ?? new GoodbyeDpiStrategyProbe();
    private readonly AntiDpiStrategySelectionStore _selectionStore = selectionStore ?? new();

    public async Task<AntiDpiOptimizationResult> EnableBestAsync(
        IEnumerable<string> selectedServiceIds,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var serviceIds = selectedServiceIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (serviceIds.Length == 0)
            return new AntiDpiOptimizationResult(false, "Anti-DPI сервисы не выбраны.", null, false, []);

        var saved = _selectionStore.Load();
        var savedProfile = saved is not null && Matches(saved, serviceIds)
            ? catalog.Profiles.FirstOrDefault(profile => string.Equals(
                profile.Id,
                saved.ProfileId,
                StringComparison.OrdinalIgnoreCase))
            : null;

        if (savedProfile is not null)
        {
            progress?.Report($"Проверяем сохранённую стратегию «{savedProfile.Name}»...");
            var savedAttempt = await RunAndProbeAsync(savedProfile, serviceIds, cancellationToken);
            if (savedAttempt.IsViable)
            {
                return new AntiDpiOptimizationResult(
                    true,
                    $"GoodbyeDPI работает со стратегией «{savedProfile.Name}».",
                    savedProfile,
                    true,
                    [savedAttempt]);
            }
        }

        var attempts = new List<AntiDpiStrategyAttempt>();
        var profiles = catalog.Profiles
            .OrderBy(profile => profile.Priority)
            .ThenBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var index = 0; index < profiles.Length; index++)
        {
            var profile = profiles[index];
            progress?.Report(
                $"Проверяем стратегию {index + 1} из {profiles.Length}: «{profile.Name}»...");
            attempts.Add(await RunAndProbeAsync(profile, serviceIds, cancellationToken));
        }

        var bestAttempt = attempts
            .Where(attempt => attempt.IsViable)
            .OrderByDescending(attempt => attempt.Score)
            .ThenBy(attempt => profiles.First(profile => profile.Id == attempt.ProfileId).Priority)
            .FirstOrDefault();
        if (bestAttempt is null)
        {
            await runtimeService.DisableAsync(cancellationToken);
            _selectionStore.Clear();
            var failedTargets = attempts
                .SelectMany(attempt => attempt.Targets)
                .Where(target => !target.IsControl && !target.IsReachable)
                .Select(target => target.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var failureSummary = failedTargets.Length == 0
                ? string.Empty
                : $" Не прошли TCP/TLS-проверку: {string.Join(", ", failedTargets)}.";
            return new AntiDpiOptimizationResult(
                false,
                $"Проверено стратегий: {profiles.Length}. Рабочий обход GoodbyeDPI не найден.{failureSummary}",
                null,
                false,
                attempts);
        }

        var bestProfile = profiles.First(profile => profile.Id == bestAttempt.ProfileId);
        if (!string.Equals(profiles[^1].Id, bestProfile.Id, StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report($"Включаем лучшую стратегию «{bestProfile.Name}»...");
            var start = await runtimeService.EnableAsync(
                serviceIds,
                bestProfile.Arguments,
                forceRestart: true,
                cancellationToken);
            if (!start.IsStarted)
            {
                return new AntiDpiOptimizationResult(
                    false,
                    start.Message,
                    null,
                    false,
                    attempts);
            }
        }

        _selectionStore.Save(new AntiDpiStrategySelection(
            1,
            catalog.CatalogVersion,
            catalog.Engine,
            catalog.EngineVersion,
            bestProfile.Id,
            serviceIds,
            bestAttempt.Score,
            DateTimeOffset.UtcNow));

        return new AntiDpiOptimizationResult(
            true,
            $"GoodbyeDPI проверен. Выбрана стратегия «{bestProfile.Name}» (оценка {bestAttempt.Score}).",
            bestProfile,
            false,
            attempts);
    }

    private async Task<AntiDpiStrategyAttempt> RunAndProbeAsync(
        AntiDpiStrategyProfile profile,
        IReadOnlyList<string> serviceIds,
        CancellationToken cancellationToken)
    {
        var started = await runtimeService.EnableAsync(
            serviceIds,
            profile.Arguments,
            forceRestart: true,
            cancellationToken);
        if (!started.IsStarted)
        {
            return new AntiDpiStrategyAttempt(
                profile.Id,
                profile.Name,
                false,
                false,
                0,
                [],
                started.Message);
        }

        var targets = await _probe.ProbeAsync(serviceIds, catalog.Targets, cancellationToken);
        var selected = serviceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredTargets = targets.Where(target =>
            target.IsControl || selected.Contains(target.ServiceId)).ToArray();
        var viable = requiredTargets.Length > 0 && requiredTargets.All(target => target.IsReachable);
        var score = CalculateScore(requiredTargets, viable);
        var failedNames = requiredTargets
            .Where(target => !target.IsReachable)
            .Select(target => target.Name)
            .ToArray();
        var message = viable
            ? "Все обязательные TCP/TLS-проверки пройдены."
            : failedNames.Length == 0
                ? "Нет результатов обязательных проверок."
                : $"Недоступны: {string.Join(", ", failedNames)}.";
        return new AntiDpiStrategyAttempt(
            profile.Id,
            profile.Name,
            true,
            viable,
            score,
            targets,
            message);
    }

    private bool Matches(AntiDpiStrategySelection selection, IReadOnlyList<string> serviceIds) =>
        selection.SchemaVersion == 1
        && selection.CatalogVersion == catalog.CatalogVersion
        && string.Equals(selection.Engine, catalog.Engine, StringComparison.OrdinalIgnoreCase)
        && string.Equals(selection.EngineVersion, catalog.EngineVersion, StringComparison.OrdinalIgnoreCase)
        && selection.ServiceIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(serviceIds);

    private static int CalculateScore(
        IReadOnlyCollection<AntiDpiTargetProbeResult> targets,
        bool viable)
    {
        if (!viable)
            return 0;

        var score = targets.Sum(target => target.IsControl ? 20 : 100);
        score += targets.Count(target => target.IsHttpSuccessful) * 10;
        var latency = targets
            .SelectMany(target => new[] { target.TcpLatency, target.TlsLatency })
            .Where(value => value.HasValue)
            .Sum(value => value!.Value.TotalMilliseconds);
        return Math.Max(1, score - (int)Math.Min(latency / 25, 50));
    }
}
