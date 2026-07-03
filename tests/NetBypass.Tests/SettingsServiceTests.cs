using System.Text.Json;
using NetBypass.Core.Services;
using Xunit;

namespace NetBypass.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void Save_PersistsHostsAndAntiDpiSelection()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "settings.json");
        var service = new SettingsService(path);

        service.Save(["openai"], ["youtube", "discord"]);
        var loaded = service.Load();

        Assert.NotNull(loaded);
        Assert.Contains("openai", loaded.SelectedModuleIds!);
        Assert.Contains("youtube", loaded.SelectedAntiDpiServiceIds!);
        Assert.Contains("discord", loaded.SelectedAntiDpiServiceIds!);
    }

    [Fact]
    public void Load_AllowsSettingsWithoutAntiDpiSelection()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            SelectedModuleIds = new[] { "openai" }
        }));
        var service = new SettingsService(path);

        var loaded = service.Load();

        Assert.NotNull(loaded);
        Assert.Contains("openai", loaded.SelectedModuleIds!);
        Assert.Null(loaded.SelectedAntiDpiServiceIds);
    }
}
