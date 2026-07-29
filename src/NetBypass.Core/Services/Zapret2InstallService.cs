using System.IO.Compression;
using System.Security.Cryptography;

namespace NetBypass.Core.Services;

public sealed class Zapret2InstallService
{
    public const string EngineVersion = "1.0.3";
    public const string DefaultDownloadUrl =
        "https://github.com/bol-van/zapret2/releases/download/v1.0.3/zapret2-v1.0.3.zip";
    public const string DefaultArchiveSha256 =
        "734FBEA360AA863CD5C724F8B941116AAA250434D699B1CE99769E5F632A7A77";

    private static readonly IReadOnlyDictionary<string, string> RequiredFileHashes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["binaries/windows-x86_64/winws2.exe"] =
                "31D3188D63757D89A22A16EE397F9BFFAE5E6150E86F00160AB9C50D3A107D4D",
            ["binaries/windows-x86_64/cygwin1.dll"] =
                "103104A52E5293CE418944725DF19E2BF81AD9269B9A120D71D39028E821499B",
            ["binaries/windows-x86_64/WinDivert.dll"] =
                "06C3F201B815A5798816E8C15B925B28F3C38E5ABA31EFEDEC10AF9E598CE723",
            ["binaries/windows-x86_64/WinDivert64.sys"] =
                "8DA085332782708D8767BCACE5327A6EC7283C17CFB85E40B03CD2323A90DDC2",
            ["lua/zapret-lib.lua"] =
                "2740B1BC0E728C4283846DF94783844082EABD503CE1F86E3429159E1B4E8DE3",
            ["lua/zapret-antidpi.lua"] =
                "9EF64F894D920E2CEEA05707899F67B206D5009BA762EB5265F409264B34214C"
        };

    private readonly HttpClient _httpClient;
    private readonly string? _expectedArchiveSha256;
    private readonly IReadOnlyDictionary<string, string>? _requiredFileHashes;

    public Zapret2InstallService(
        string? installRoot = null,
        HttpClient? httpClient = null,
        string? downloadUrl = null,
        string? expectedArchiveSha256 = DefaultArchiveSha256,
        IReadOnlyDictionary<string, string>? requiredFileHashes = null)
    {
        InstallRoot = installRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetBypass",
            "Engines",
            "Zapret2");
        DownloadUrl = downloadUrl ?? DefaultDownloadUrl;
        _expectedArchiveSha256 = expectedArchiveSha256;
        _requiredFileHashes = requiredFileHashes ?? RequiredFileHashes;
        _httpClient = httpClient ?? new HttpClient();
    }

    public string InstallRoot { get; }
    public string DownloadUrl { get; }
    public string CurrentRoot => Path.Combine(InstallRoot, "current", $"zapret2-v{EngineVersion}");
    public string ExecutablePath => Path.Combine(
        CurrentRoot, "binaries", "windows-x86_64", "winws2.exe");
    public string LuaLibraryPath => Path.Combine(CurrentRoot, "lua", "zapret-lib.lua");
    public string LuaAntiDpiPath => Path.Combine(CurrentRoot, "lua", "zapret-antidpi.lua");
    public string QuicBlobPath => Path.Combine(
        CurrentRoot, "files", "fake", "quic_initial_www_google_com.bin");

    public bool IsInstalled() =>
        File.Exists(ExecutablePath)
        && File.Exists(Path.Combine(Path.GetDirectoryName(ExecutablePath)!, "cygwin1.dll"))
        && File.Exists(Path.Combine(Path.GetDirectoryName(ExecutablePath)!, "WinDivert.dll"))
        && File.Exists(Path.Combine(Path.GetDirectoryName(ExecutablePath)!, "WinDivert64.sys"))
        && File.Exists(LuaLibraryPath)
        && File.Exists(LuaAntiDpiPath);

    public async Task<Zapret2InstallResult> InstallAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(InstallRoot);
        var archivePath = Path.Combine(InstallRoot, "zapret2-download.zip");
        var stagingRoot = Path.Combine(InstallRoot, $"staging-{Guid.NewGuid():N}");
        var currentRoot = Path.Combine(InstallRoot, "current");

        try
        {
            progress?.Report($"Скачиваем официальный zapret2 v{EngineVersion}...");
            await DownloadArchiveAsync(archivePath, cancellationToken);
            if (_expectedArchiveSha256 is not null)
                await VerifyHashAsync(archivePath, _expectedArchiveSha256, cancellationToken);

            progress?.Report("Проверяем и распаковываем архив zapret2...");
            Directory.CreateDirectory(stagingRoot);
            ExtractArchiveSafely(archivePath, stagingRoot);

            var stagedPackageRoot = Path.Combine(stagingRoot, $"zapret2-v{EngineVersion}");
            if (!Directory.Exists(stagedPackageRoot))
                throw new InvalidDataException("В архиве zapret2 не найден корневой каталог ожидаемой версии.");

            if (_requiredFileHashes is not null)
            {
                foreach (var pair in _requiredFileHashes)
                {
                    var path = Path.Combine(
                        stagedPackageRoot,
                        pair.Key.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(path))
                        throw new InvalidDataException($"В архиве zapret2 отсутствует {pair.Key}.");
                    await VerifyHashAsync(path, pair.Value, cancellationToken);
                }
            }

            if (Directory.Exists(currentRoot))
                Directory.Delete(currentRoot, recursive: true);
            Directory.CreateDirectory(currentRoot);
            Directory.Move(stagedPackageRoot, Path.Combine(currentRoot, Path.GetFileName(stagedPackageRoot)));

            return new Zapret2InstallResult(
                true,
                $"zapret2 v{EngineVersion} скачан, проверен и готов к работе.",
                ExecutablePath);
        }
        catch
        {
            if (Directory.Exists(currentRoot) && !IsInstalled())
                Directory.Delete(currentRoot, recursive: true);
            throw;
        }
        finally
        {
            if (File.Exists(archivePath))
                File.Delete(archivePath);
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    public Zapret2UninstallResult Uninstall()
    {
        var fullInstallRoot = Path.GetFullPath(InstallRoot);
        var driveRoot = Path.GetPathRoot(fullInstallRoot)?.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (string.Equals(
                fullInstallRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                driveRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Каталог установки zapret2 не может быть корнем диска.");
        }

        if (!Directory.Exists(fullInstallRoot))
            return new Zapret2UninstallResult(true, "zapret2 уже удалён.");

        Directory.Delete(fullInstallRoot, recursive: true);
        return new Zapret2UninstallResult(
            !Directory.Exists(fullInstallRoot),
            "zapret2 и его установочные файлы удалены.");
    }

    private async Task DownloadArchiveAsync(string downloadPath, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(DownloadUrl, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            File.Copy(uri.LocalPath, downloadPath, overwrite: true);
            return;
        }

        using var response = await _httpClient.GetAsync(
            DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(downloadPath);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static async Task VerifyHashAsync(
        string path,
        string expected,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var actual = Convert.ToHexString(hash);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Контрольная сумма {Path.GetFileName(path)} не совпадает. Ожидалась {expected}, получена {actual}.");
        }
    }

    private static void ExtractArchiveSafely(string archivePath, string destinationRoot)
    {
        var fullDestinationRoot = Path.GetFullPath(destinationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!targetPath.StartsWith(fullDestinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Небезопасный путь в архиве zapret2: {entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }
}

public sealed record Zapret2InstallResult(
    bool IsInstalled,
    string Message,
    string? ExecutablePath);

public sealed record Zapret2UninstallResult(bool IsRemoved, string Message);
