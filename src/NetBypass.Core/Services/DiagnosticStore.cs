using System.Text.Json;
using NetBypass.Core.Models;

namespace NetBypass.Core.Services;

public sealed class DiagnosticStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public DiagnosticStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetBypass",
            "diagnostics.json");
    }

    public DiagnosticSnapshot? Load()
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<DiagnosticSnapshot>(
                File.ReadAllText(_path),
                JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(DiagnosticSnapshot snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(snapshot, JsonOptions));
    }
}
