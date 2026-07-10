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

        service.Save(["openai"], ["youtube", "discord"], startWithWindows: true);
        var loaded = service.Load();

        Assert.NotNull(loaded);
        Assert.Contains("openai", loaded.SelectedModuleIds!);
        Assert.Contains("youtube", loaded.SelectedAntiDpiServiceIds!);
        Assert.Contains("discord", loaded.SelectedAntiDpiServiceIds!);
        Assert.True(loaded.StartWithWindows);
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
        Assert.False(loaded.StartWithWindows);
    }

    [Fact]
    public void Save_PreservesExistingStartupSettingWhenNotSpecified()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "settings.json");
        var service = new SettingsService(path);
        service.Save(["openai"], ["youtube"], startWithWindows: true);

        service.Save(["discord"], ["discord"]);
        var loaded = service.Load();

        Assert.NotNull(loaded);
        Assert.True(loaded.StartWithWindows);
        Assert.Contains("discord", loaded.SelectedModuleIds!);
    }
}
