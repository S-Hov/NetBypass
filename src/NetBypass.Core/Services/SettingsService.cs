using System.Text.Json;

namespace NetBypass.Core.Services;

public sealed record AppSettings(
    HashSet<string>? SelectedModuleIds,
    HashSet<string>? SelectedAntiDpiServiceIds = null,
    bool StartWithWindows = false);

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsService(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetBypass", "settings.json");
    }

    public AppSettings? Load()
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(
        IEnumerable<string> selectedIds,
        IEnumerable<string>? selectedAntiDpiServiceIds = null,
        bool? startWithWindows = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var effectiveStartWithWindows = startWithWindows ?? Load()?.StartWithWindows ?? false;
        var settings = new AppSettings(
            selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase),
            selectedAntiDpiServiceIds?.ToHashSet(StringComparer.OrdinalIgnoreCase),
            effectiveStartWithWindows);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
