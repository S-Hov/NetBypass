using System.IO.Compression;
using NetBypass.Core.Services;
using Xunit;

namespace NetBypass.Tests;

public sealed class Zapret2InstallServiceTests
{
    [Fact]
    public async Task InstallAsync_ExtractsExpectedPackage()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var packageRoot = Path.Combine(directory, "source", $"zapret2-v{Zapret2InstallService.EngineVersion}");
        CreatePackage(packageRoot);
        var archivePath = Path.Combine(directory, "zapret2.zip");
        ZipFile.CreateFromDirectory(Path.Combine(directory, "source"), archivePath);
        var service = new Zapret2InstallService(
            Path.Combine(directory, "install"),
            downloadUrl: new Uri(archivePath).AbsoluteUri,
            expectedArchiveSha256: null,
            requiredFileHashes: new Dictionary<string, string>());

        var result = await service.InstallAsync();

        Assert.True(result.IsInstalled);
        Assert.True(service.IsInstalled());
        Assert.EndsWith("winws2.exe", result.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task InstallAsync_RejectsArchivePathTraversal()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        var archivePath = Path.Combine(directory, "zapret2.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../escaped.txt");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("unsafe");
        }

        var service = new Zapret2InstallService(
            Path.Combine(directory, "install"),
            downloadUrl: new Uri(archivePath).AbsoluteUri,
            expectedArchiveSha256: null,
            requiredFileHashes: new Dictionary<string, string>());

        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync());
        Assert.False(File.Exists(Path.Combine(directory, "escaped.txt")));
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void Uninstall_RemovesOnlyZapret2Directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var installRoot = Path.Combine(directory, "zapret2");
        CreatePackage(Path.Combine(installRoot, "current", $"zapret2-v{Zapret2InstallService.EngineVersion}"));
        var sibling = Path.Combine(directory, "settings.json");
        File.WriteAllText(sibling, "keep");

        var result = new Zapret2InstallService(installRoot).Uninstall();

        Assert.True(result.IsRemoved);
        Assert.False(Directory.Exists(installRoot));
        Assert.True(File.Exists(sibling));
        Directory.Delete(directory, recursive: true);
    }

    private static void CreatePackage(string packageRoot)
    {
        var binaries = Path.Combine(packageRoot, "binaries", "windows-x86_64");
        var lua = Path.Combine(packageRoot, "lua");
        Directory.CreateDirectory(binaries);
        Directory.CreateDirectory(lua);
        foreach (var file in new[] { "winws2.exe", "cygwin1.dll", "WinDivert.dll", "WinDivert64.sys" })
            File.WriteAllText(Path.Combine(binaries, file), "test");
        File.WriteAllText(Path.Combine(lua, "zapret-lib.lua"), "test");
        File.WriteAllText(Path.Combine(lua, "zapret-antidpi.lua"), "test");
    }
}
