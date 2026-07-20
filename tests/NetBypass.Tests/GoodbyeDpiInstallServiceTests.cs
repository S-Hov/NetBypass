using System.IO.Compression;
using NetBypass.Core.Services;
using Xunit;

namespace NetBypass.Tests;

public sealed class GoodbyeDpiInstallServiceTests
{
    [Fact]
    public async Task InstallAsync_ExtractsArchiveAndFindsExecutable()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var sourceDirectory = Path.Combine(directory, "source", "x86_64");
        var installDirectory = Path.Combine(directory, "install");
        var archivePath = Path.Combine(directory, "goodbyedpi.zip");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, "goodbyedpi.exe"), "demo");
        ZipFile.CreateFromDirectory(Path.Combine(directory, "source"), archivePath);
        var service = new GoodbyeDpiInstallService(
            installDirectory,
            downloadUrl: new Uri(archivePath).AbsoluteUri);

        var result = await service.InstallAsync();

        Assert.True(result.IsInstalled);
        Assert.NotNull(result.ExecutablePath);
        Assert.True(File.Exists(result.ExecutablePath));
        Assert.True(service.IsInstalled());
    }

    [Fact]
    public void IsInstalled_ReturnsFalseWhenExecutableIsMissing()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var service = new GoodbyeDpiInstallService(directory);

        Assert.False(service.IsInstalled());
        Assert.Null(service.FindExecutable());
    }

    [Fact]
    public void FindExecutable_PrefersX64Binary()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var x86 = Path.Combine(directory, "current", "x86");
        var x64 = Path.Combine(directory, "current", "x86_64");
        Directory.CreateDirectory(x86);
        Directory.CreateDirectory(x64);
        File.WriteAllText(Path.Combine(x86, "goodbyedpi.exe"), "x86");
        File.WriteAllText(Path.Combine(x64, "goodbyedpi.exe"), "x64");

        var executable = new GoodbyeDpiInstallService(directory).FindExecutable();

        Assert.NotNull(executable);
        Assert.Contains("x86_64", executable, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Uninstall_RemovesOnlyTheEngineInstallDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var installDirectory = Path.Combine(directory, "install");
        Directory.CreateDirectory(Path.Combine(installDirectory, "current", "x86_64"));
        File.WriteAllText(
            Path.Combine(installDirectory, "current", "x86_64", "goodbyedpi.exe"),
            "demo");
        var siblingFile = Path.Combine(directory, "settings.json");
        File.WriteAllText(siblingFile, "keep");

        var result = new GoodbyeDpiInstallService(installDirectory).Uninstall();

        Assert.True(result.IsRemoved);
        Assert.False(Directory.Exists(installDirectory));
        Assert.True(File.Exists(siblingFile));
        Directory.Delete(directory, recursive: true);
    }
}
