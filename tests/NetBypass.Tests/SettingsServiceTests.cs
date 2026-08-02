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
        Assert.True(loaded.MultiCheckEnabled);
        Assert.Equal(3, loaded.DiagnosticAttempts);
        Assert.Equal("zapret2", loaded.SelectedAntiDpiEngineId);
    }

    [Fact]
    public void Save_PersistsSelectedAntiDpiEngine()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "settings.json");
        var service = new SettingsService(path);

        service.Save(["openai"], selectedAntiDpiEngineId: "zapret2");
        service.Save(["discord"]);

        Assert.Equal("zapret2", service.Load()!.SelectedAntiDpiEngineId);
    }

    [Fact]
    public void Save_PreservesExplicitGoodbyeDpiSelection()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "settings.json");
        var service = new SettingsService(path);

        service.Save(["openai"], selectedAntiDpiEngineId: "goodbyedpi");
        service.Save(["discord"]);

        Assert.Equal("goodbyedpi", service.Load()!.SelectedAntiDpiEngineId);
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
        Assert.True(loaded.MultiCheckEnabled);
        Assert.Equal(3, loaded.DiagnosticAttempts);
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

    [Fact]
    public void Save_PersistsAndPreservesDiagnosticRetrySettings()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "settings.json");
        var service = new SettingsService(path);
        service.Save(
            ["openai"],
            multiCheckEnabled: false,
            diagnosticAttempts: 5);

        service.Save(["discord"]);
        var loaded = service.Load();

        Assert.NotNull(loaded);
        Assert.False(loaded.MultiCheckEnabled);
        Assert.Equal(5, loaded.DiagnosticAttempts);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(20, 10)]
    public void Save_ClampsDiagnosticAttempts(int value, int expected)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "settings.json");
        var service = new SettingsService(path);

        service.Save(["openai"], diagnosticAttempts: value);

        Assert.Equal(expected, service.Load()!.DiagnosticAttempts);
    }
}
