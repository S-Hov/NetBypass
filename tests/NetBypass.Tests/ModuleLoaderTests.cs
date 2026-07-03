using NetBypass.Core.Services;
using Xunit;

namespace NetBypass.Tests;

public sealed class ModuleLoaderTests
{
    [Fact]
    public void ServiceProfileLoader_LoadDirectory_PrefersJsonProfiles()
    {
        using var fixture = new ProfileFixture();
        fixture.WriteProfile(
            "json-demo",
            """
            {
              "schemaVersion": 1,
              "id": "json-demo",
              "name": "JSON Demo",
              "category": "Test",
              "default": true,
              "strategies": [ "adaptive-hosts" ],
              "hosts": [
                { "address": "1.2.3.4", "hostname": "Example.COM." }
              ],
              "healthChecks": [
                {
                  "targetAddress": "1.2.3.4",
                  "host": "example.com",
                  "port": 443,
                  "protocol": "https"
                }
              ],
              "relayCandidates": [
                {
                  "address": "5.6.7.8",
                  "host": "relay.example.com",
                  "port": 443,
                  "protocol": "https",
                  "priority": 10
                }
              ]
            }
            """);
        fixture.WriteModule(
            "legacy",
            "# id: legacy\n# name: Legacy\n# category: Test\n1.2.3.4 legacy.example");

        var profiles = new ServiceProfileLoader().LoadDirectory(fixture.ModulesPath);

        var profile = Assert.Single(profiles);
        Assert.Equal("json-demo", profile.Id);
        Assert.True(profile.Module.IsEnabledByDefault);
        Assert.Equal("example.com", profile.Module.Entries.Single().Hostname);
        Assert.Equal("1.2.3.4", profile.HealthChecks.Single().TargetAddress);
        Assert.Equal("5.6.7.8", profile.RelayCandidates.Single().Address);
    }

    [Fact]
    public void ServiceProfileLoader_LoadDirectory_FallsBackToHostsModules()
    {
        using var fixture = new ProfileFixture();
        fixture.WriteModule(
            "legacy",
            "# id: legacy\n# name: Legacy\n# category: Test\n1.2.3.4 legacy.example");

        var profiles = new ServiceProfileLoader().LoadDirectory(fixture.ModulesPath);

        var profile = Assert.Single(profiles);
        Assert.Equal("legacy", profile.Id);
        Assert.Equal("adaptive-hosts", Assert.Single(profile.Strategies));
        Assert.Empty(profile.RelayCandidates);
    }

    [Fact]
    public void ServiceProfileLoader_LoadDirectory_RejectsLocalJsonMappings()
    {
        using var fixture = new ProfileFixture();
        fixture.WriteProfile(
            "bad",
            """
            {
              "schemaVersion": 1,
              "id": "bad",
              "name": "Bad",
              "category": "Test",
              "hosts": [
                { "address": "127.0.0.1", "hostname": "example.com" }
              ]
            }
            """);

        Assert.Throws<FormatException>(() =>
            new ServiceProfileLoader().LoadDirectory(fixture.ModulesPath));
    }

    [Theory]
    [InlineData("0.0.0.0 example.com")]
    [InlineData("127.0.0.1 example.com")]
    [InlineData("192.168.1.10 example.com")]
    [InlineData("1.2.3.4 localhost")]
    public void LoadFile_RejectsLocalMappings(string entry)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, $"# id: test\n# name: Test\n# category: Test\n{entry}");
            Assert.Throws<FormatException>(() => new ModuleLoader().LoadFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class ProfileFixture : IDisposable
    {
        private readonly string _directory;

        public ProfileFixture()
        {
            _directory = Path.Combine(Path.GetTempPath(), $"NetBypass.Tests-{Guid.NewGuid():N}");
            ModulesPath = Path.Combine(_directory, "Modules");
            ProfilesPath = Path.Combine(_directory, "Profiles");
            Directory.CreateDirectory(ModulesPath);
            Directory.CreateDirectory(ProfilesPath);
        }

        public string ModulesPath { get; }
        public string ProfilesPath { get; }

        public void WriteModule(string id, string content) =>
            File.WriteAllText(Path.Combine(ModulesPath, $"{id}.hosts"), content);

        public void WriteProfile(string id, string content) =>
            File.WriteAllText(Path.Combine(ProfilesPath, $"{id}.json"), content);

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
