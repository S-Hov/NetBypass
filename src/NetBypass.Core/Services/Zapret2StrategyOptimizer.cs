using NetBypass.Core.Models;

namespace NetBypass.Core.Services;

public sealed class Zapret2StrategyOptimizer(
    Zapret2RuntimeService runtimeService,
    AntiDpiStrategyCatalog catalog,
    IAntiDpiStrategyProbe? probe = null,
    AntiDpiStrategySelectionStore? selectionStore = null)
{
    private readonly IAntiDpiStrategyProbe _probe = probe ?? new GoodbyeDpiStrategyProbe();
    private readonly AntiDpiStrategySelectionStore _selectionStore = selectionStore ?? new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetBypass",
            "zapret2-strategy.json"));

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
            progress?.Report($"Проверяем сохранённую стратегию zapret2 «{savedProfile.Name}»...");
            var savedAttempt = await RunAndProbeAsync(
                savedProfile,
                serviceIds,
                saved!.Addresses,
                progress,
                cancellationToken);
            if (savedAttempt.IsViable)
            {
                var savedAddresses = BuildAddressMap(savedAttempt);
                _selectionStore.Save(saved with
                {
                    Score = savedAttempt.Score,
                    VerifiedAt = DateTimeOffset.UtcNow,
                    Addresses = savedAddresses
                });
                return new AntiDpiOptimizationResult(
                    true,
                    $"zapret2 работает со стратегией «{savedProfile.Name}».",
                    savedProfile,
                    true,
                    [savedAttempt],
                    savedAddresses);
            }

            progress?.Report("Сохранённая стратегия zapret2 больше не работает. Запускаем подбор.");
        }

        var attempts = new List<AntiDpiStrategyAttempt>();
        var profiles = catalog.Profiles
            .Where(profile => savedProfile is null || !string.Equals(
                profile.Id,
                savedProfile.Id,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(profile => profile.Priority)
            .ThenBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var index = 0; index < profiles.Length; index++)
        {
            var profile = profiles[index];
            progress?.Report(
                $"zapret2: стратегия {index + 1} из {profiles.Length} — «{profile.Name}»...");
            var attempt = await RunAndProbeAsync(
                profile,
                serviceIds,
                preferredAddresses: null,
                progress,
                cancellationToken);
            attempts.Add(attempt);
            progress?.Report(attempt.IsViable
                ? $"✓ «{profile.Name}»: проверки пройдены."
                : $"× «{profile.Name}»: {attempt.Message}");

            // Каталог идёт от щадящих стратегий к более агрессивным. Первый
            // полностью рабочий результат лучше долгого перебора десятков вариантов.
            if (attempt.IsViable)
                break;
        }

        var bestAttempt = attempts
            .Where(attempt => attempt.IsViable)
            .OrderByDescending(attempt => attempt.Score)
            .FirstOrDefault();
        if (bestAttempt is null)
        {
            await runtimeService.DisableAsync(cancellationToken);
            _selectionStore.Clear();
            return new AntiDpiOptimizationResult(
                false,
                $"Проверено стратегий zapret2: {attempts.Count}. Рабочий профиль не найден.",
                null,
                false,
                attempts);
        }

        var bestProfile = profiles.First(profile => profile.Id == bestAttempt.ProfileId);
        var addresses = BuildAddressMap(bestAttempt);
        _selectionStore.Save(new AntiDpiStrategySelection(
            1,
            catalog.CatalogVersion,
            catalog.Engine,
            catalog.EngineVersion,
            bestProfile.Id,
            serviceIds,
            bestAttempt.Score,
            DateTimeOffset.UtcNow,
            addresses));

        return new AntiDpiOptimizationResult(
            true,
            $"zapret2 проверен. Выбрана стратегия «{bestProfile.Name}».",
            bestProfile,
            false,
            attempts,
            addresses);
    }

    private async Task<AntiDpiStrategyAttempt> RunAndProbeAsync(
        AntiDpiStrategyProfile profile,
        IReadOnlyList<string> serviceIds,
        IReadOnlyDictionary<string, string>? preferredAddresses,
        IProgress<string>? progress,
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

        var targets = await _probe.ProbeAsync(
            serviceIds,
            catalog.Targets,
            preferredAddresses,
            progress,
            cancellationToken);
        var selected = serviceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredTargets = targets.Where(target =>
            target.IsControl || selected.Contains(target.ServiceId)).ToArray();
        var viable = requiredTargets.Length > 0 && requiredTargets.All(target => target.IsReachable);
        var score = viable
            ? requiredTargets.Sum(target => target.IsControl ? 20 : 100)
              + requiredTargets.Count(target => target.IsHttpSuccessful) * 10
            : 0;
        var failed = requiredTargets
            .Where(target => !target.IsReachable)
            .Select(target => target.Name)
            .ToArray();
        return new AntiDpiStrategyAttempt(
            profile.Id,
            profile.Name,
            true,
            viable,
            score,
            targets,
            viable
                ? "Все обязательные TCP/TLS-проверки пройдены."
                : failed.Length == 0
                    ? "Нет результатов обязательных проверок."
                    : $"Недоступны: {string.Join(", ", failed)}.");
    }

    private bool Matches(AntiDpiStrategySelection selection, IReadOnlyList<string> serviceIds) =>
        selection.SchemaVersion == 1
        && selection.CatalogVersion == catalog.CatalogVersion
        && string.Equals(selection.Engine, catalog.Engine, StringComparison.OrdinalIgnoreCase)
        && string.Equals(selection.EngineVersion, catalog.EngineVersion, StringComparison.OrdinalIgnoreCase)
        && selection.ServiceIds.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(serviceIds);

    private static Dictionary<string, string> BuildAddressMap(AntiDpiStrategyAttempt attempt) =>
        attempt.Targets
            .Where(target => !target.IsControl && target.IsReachable && target.Address is not null)
            .ToDictionary(
                target => target.ServiceId,
                target => target.Address!,
                StringComparer.OrdinalIgnoreCase);
}
