using System.Text.Json;
using NetBypass.Core.Models;

namespace NetBypass.Core.Services;

public sealed class AntiDpiStrategyCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AntiDpiStrategyCatalog Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Каталог Anti-DPI стратегий не найден.", path);

        var catalog = JsonSerializer.Deserialize<AntiDpiStrategyCatalog>(
            File.ReadAllText(path),
            JsonOptions)
            ?? throw new InvalidDataException("Каталог Anti-DPI стратегий пуст.");

        Validate(catalog);
        return catalog;
    }

    public static void Validate(AntiDpiStrategyCatalog catalog)
    {
        if (catalog.SchemaVersion != 1)
            throw new InvalidDataException($"Версия схемы стратегий {catalog.SchemaVersion} не поддерживается.");
        if (!string.Equals(catalog.Engine, "goodbyedpi", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Сейчас поддерживается каталог только для GoodbyeDPI.");
        if (string.IsNullOrWhiteSpace(catalog.EngineVersion))
            throw new InvalidDataException("В каталоге не указана версия движка.");
        if (catalog.Profiles.Count == 0)
            throw new InvalidDataException("Каталог не содержит стратегий.");
        if (catalog.Targets.Count == 0)
            throw new InvalidDataException("Каталог не содержит целей для проверки.");

        var duplicateProfile = catalog.Profiles
            .GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateProfile is not null)
            throw new InvalidDataException($"Стратегия {duplicateProfile.Key} объявлена несколько раз.");

        foreach (var profile in catalog.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id)
                || string.IsNullOrWhiteSpace(profile.Name)
                || profile.Arguments.Count == 0)
            {
                throw new InvalidDataException("У каждой стратегии должны быть id, имя и аргументы.");
            }

            foreach (var argument in profile.Arguments)
            {
                if (argument.Contains('{')
                    && !string.Equals(argument, "{blacklist}", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Стратегия {profile.Id} содержит неизвестный шаблон: {argument}.");
                }
            }
        }

        var duplicateTarget = catalog.Targets
            .GroupBy(target => target.ServiceId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateTarget is not null)
            throw new InvalidDataException($"Цель {duplicateTarget.Key} объявлена несколько раз.");
    }
}

public sealed class AntiDpiStrategySelectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public AntiDpiStrategySelectionStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetBypass",
            "anti-dpi-strategy.json");
    }

    public AntiDpiStrategySelection? Load()
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<AntiDpiStrategySelection>(File.ReadAllText(_path));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(AntiDpiStrategySelection selection)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(selection, JsonOptions));
    }

    public void Clear()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }
}
