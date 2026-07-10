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
}
