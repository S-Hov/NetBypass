using System.IO.Compression;

namespace NetBypass.Core.Services;

public sealed class GoodbyeDpiInstallService
{
    public const string DefaultDownloadUrl =
        "https://github.com/ValdikSS/GoodbyeDPI/releases/download/0.2.2/goodbyedpi-0.2.2.zip";

    private readonly HttpClient _httpClient;

    public GoodbyeDpiInstallService(
        string? installRoot = null,
        HttpClient? httpClient = null,
        string? downloadUrl = null)
    {
        InstallRoot = installRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetBypass",
            "Engines",
            "GoodbyeDPI");
        DownloadUrl = downloadUrl ?? DefaultDownloadUrl;
        _httpClient = httpClient ?? new HttpClient();
    }

    public string InstallRoot { get; }
    public string DownloadUrl { get; }

    public string? FindExecutable()
    {
        return FindExecutables()
            .OrderByDescending(Is64BitExecutable)
            .ThenBy(path => path.Length)
            .FirstOrDefault();
    }

    public IReadOnlyList<string> FindExecutables()
    {
        if (!Directory.Exists(InstallRoot))
            return [];

        return Directory.EnumerateFiles(InstallRoot, "goodbyedpi.exe", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool IsInstalled() => FindExecutable() is not null;

    private static bool Is64BitExecutable(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => string.Equals(part, "x86_64", StringComparison.OrdinalIgnoreCase));

    public async Task<GoodbyeDpiInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(InstallRoot);
        var downloadPath = Path.Combine(InstallRoot, "goodbyedpi-download.zip");
        var extractPath = Path.Combine(InstallRoot, "current");

        try
        {
            await DownloadArchiveAsync(downloadPath, cancellationToken);

            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, recursive: true);
            Directory.CreateDirectory(extractPath);

            ZipFile.ExtractToDirectory(downloadPath, extractPath, overwriteFiles: true);
            var executable = FindExecutable();
            if (executable is null)
            {
                return new GoodbyeDpiInstallResult(
                    false,
                    "Архив скачан, но goodbyedpi.exe внутри не найден.",
                    null);
            }

            return new GoodbyeDpiInstallResult(
                true,
                "GoodbyeDPI скачан и готов к подключению.",
                executable);
        }
        finally
        {
            if (File.Exists(downloadPath))
                File.Delete(downloadPath);
        }
    }

    public GoodbyeDpiUninstallResult Uninstall()
    {
        var fullInstallRoot = Path.GetFullPath(InstallRoot);
        if (string.Equals(
                fullInstallRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetPathRoot(fullInstallRoot)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Каталог установки движка не может быть корнем диска.");
        }

        if (!Directory.Exists(fullInstallRoot))
            return new GoodbyeDpiUninstallResult(true, "GoodbyeDPI уже удалён.");

        Directory.Delete(fullInstallRoot, recursive: true);
        return new GoodbyeDpiUninstallResult(
            !Directory.Exists(fullInstallRoot),
            "GoodbyeDPI и его файлы удалены.");
    }

    private async Task DownloadArchiveAsync(
        string downloadPath,
        CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(DownloadUrl, UriKind.Absolute, out var uri)
            && uri.IsFile)
        {
            File.Copy(uri.LocalPath, downloadPath, overwrite: true);
            return;
        }

        using var response = await _httpClient.GetAsync(DownloadUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(downloadPath);
        await source.CopyToAsync(destination, cancellationToken);
    }
}

public sealed record GoodbyeDpiInstallResult(
    bool IsInstalled,
    string Message,
    string? ExecutablePath);

public sealed record GoodbyeDpiUninstallResult(bool IsRemoved, string Message);
