using System.Text.Json;

namespace NetBypass.Core.Services;

public sealed record AppSettings(
    HashSet<string>? SelectedModuleIds,
    HashSet<string>? SelectedAntiDpiServiceIds = null,
    bool StartWithWindows = false,
    bool MultiCheckEnabled = true,
    int DiagnosticAttempts = 3);

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
        bool? startWithWindows = null,
        bool? multiCheckEnabled = null,
        int? diagnosticAttempts = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var current = Load();
        var effectiveStartWithWindows = startWithWindows ?? current?.StartWithWindows ?? false;
        var effectiveMultiCheckEnabled = multiCheckEnabled ?? current?.MultiCheckEnabled ?? true;
        var effectiveDiagnosticAttempts = Math.Clamp(
            diagnosticAttempts ?? current?.DiagnosticAttempts ?? 3,
            2,
            10);
        var settings = new AppSettings(
            selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase),
            selectedAntiDpiServiceIds?.ToHashSet(StringComparer.OrdinalIgnoreCase),
            effectiveStartWithWindows,
            effectiveMultiCheckEnabled,
            effectiveDiagnosticAttempts);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
