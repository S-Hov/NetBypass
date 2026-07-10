using System.Diagnostics;
using System.ComponentModel;
using System.Security.Principal;
using NetBypass.Core.Models;

namespace NetBypass.Core.Services;

public sealed class GoodbyeDpiRuntimeService
{
    public const string LegacyTaskName = "NetBypass GoodbyeDPI";

    private static readonly string[] WinDivertServiceNames =
    [
        "WinDivert",
        "WinDivert1.4",
        "WinDivert14",
        "WinDivert2.2",
        "WinDivert22"
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DomainsByService =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["youtube"] =
            [
                "youtube.com",
                "www.youtube.com",
                "m.youtube.com",
                "youtu.be",
                "googlevideo.com",
                "youtubei.googleapis.com",
                "youtube-nocookie.com",
                "ytimg.com",
                "ggpht.com"
            ],
            ["discord"] =
            [
                "discord.com",
                "www.discord.com",
                "discord.gg",
                "discordapp.com",
                "discordapp.net",
                "discord.media"
            ]
        };

    private readonly GoodbyeDpiInstallService _installService;
    private readonly ISystemCommandRunner _commandRunner;

    public GoodbyeDpiRuntimeService(
        GoodbyeDpiInstallService installService,
        string? runtimeRoot = null,
        ISystemCommandRunner? commandRunner = null)
    {
        _installService = installService;
        RuntimeRoot = runtimeRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetBypass",
            "Runtime",
            "GoodbyeDPI");
        _commandRunner = commandRunner ?? new SystemCommandRunner();
    }

    public string RuntimeRoot { get; }
    public string BlacklistPath => Path.Combine(RuntimeRoot, "blacklist.txt");

    public bool IsEnabled()
    {
        var executable = _installService.FindExecutable();
        return executable is not null && _commandRunner.IsProcessRunning(executable);
    }

    public async Task<EngineRunResult> EnableAsync(
        IEnumerable<string> selectedServiceIds,
        IReadOnlyList<string>? strategyArguments = null,
        bool forceRestart = false,
        CancellationToken cancellationToken = default)
    {
        var serviceIds = selectedServiceIds
            .Where(id => DomainsByService.ContainsKey(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (serviceIds.Length == 0)
            return new EngineRunResult(false, "Anti-DPI сервисы не выбраны.");

        var executable = _installService.FindExecutable();
        if (executable is null)
            return new EngineRunResult(false, "GoodbyeDPI не скачан.");

        await RemoveLegacyTaskAsync(cancellationToken);

        if (!forceRestart && _commandRunner.IsProcessRunning(executable))
        {
            return new EngineRunResult(
                true,
                $"GoodbyeDPI уже работает для сервисов: {string.Join(", ", serviceIds)}.");
        }

        if (forceRestart)
        {
            StopAllInstalledProcesses();
            await CleanupWinDivertDriversAsync(cancellationToken);
        }

        Directory.CreateDirectory(RuntimeRoot);
        var domains = BuildBlacklist(serviceIds);
        await File.WriteAllLinesAsync(BlacklistPath, domains, cancellationToken);

        var startResult = await _commandRunner.StartDetachedAsync(
            executable,
            strategyArguments is null
                ? BuildArguments(BlacklistPath)
                : ResolveArguments(strategyArguments, BlacklistPath),
            Path.GetDirectoryName(executable),
            cancellationToken,
            requireAdministrator: !_commandRunner.IsAdministrator());
        if (startResult.ExitCode != 0)
        {
            CleanupRuntimeFiles();
            return new EngineRunResult(false, ToCommandFailure("Не удалось запустить GoodbyeDPI", startResult));
        }

        return new EngineRunResult(
            true,
            $"GoodbyeDPI включён для сервисов: {string.Join(", ", serviceIds)}.",
            startResult.ProcessId);
    }

    public async Task<EngineStopResult> DisableAsync(CancellationToken cancellationToken = default)
    {
        StopAllInstalledProcesses();
        await CleanupWinDivertDriversAsync(cancellationToken);

        await RemoveLegacyTaskAsync(cancellationToken);

        CleanupRuntimeFiles();

        return new EngineStopResult(true, "GoodbyeDPI остановлен.");
    }

    public static IReadOnlyList<string> BuildBlacklist(IEnumerable<string> selectedServiceIds) =>
        selectedServiceIds
            .Where(DomainsByService.ContainsKey)
            .SelectMany(id => DomainsByService[id])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<HostEntry> BuildHostsEntries(
        IReadOnlyDictionary<string, string> addressesByService) =>
        addressesByService
            .Where(pair => DomainsByService.ContainsKey(pair.Key))
            .SelectMany(pair => DomainsByService[pair.Key]
                .Select(domain => new HostEntry(pair.Value, domain)))
            .GroupBy(entry => entry.Hostname, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(entry => entry.Hostname, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<string> BuildArguments(string blacklistPath) =>
    [
        "-5",
        "--blacklist",
        blacklistPath,
        "--dns-addr",
        "77.88.8.8",
        "--dns-port",
        "1253",
        "--dnsv6-addr",
        "2a02:6b8::feed:0ff",
        "--dnsv6-port",
        "1253"
    ];

    public static IReadOnlyList<string> ResolveArguments(
        IEnumerable<string> arguments,
        string blacklistPath) =>
        arguments
            .Select(argument => string.Equals(
                argument,
                "{blacklist}",
                StringComparison.OrdinalIgnoreCase)
                ? blacklistPath
                : argument)
            .ToArray();

    private void StopAllInstalledProcesses()
    {
        foreach (var executable in _installService.FindExecutables())
            _commandRunner.StopProcessesByPath(executable);
    }

    private void CleanupRuntimeFiles()
    {
        if (File.Exists(BlacklistPath))
            File.Delete(BlacklistPath);
    }

    private async Task RemoveLegacyTaskAsync(CancellationToken cancellationToken)
    {
        await _commandRunner.RunAsync(
            "schtasks.exe",
            ["/Delete", "/TN", LegacyTaskName, "/F"],
            cancellationToken,
            requireAdministrator: !_commandRunner.IsAdministrator());
    }

    private async Task CleanupWinDivertDriversAsync(CancellationToken cancellationToken)
    {
        foreach (var serviceName in WinDivertServiceNames)
        {
            await _commandRunner.RunAsync(
                "sc.exe",
                ["stop", serviceName],
                cancellationToken,
                requireAdministrator: !_commandRunner.IsAdministrator());
            await _commandRunner.RunAsync(
                "sc.exe",
                ["delete", serviceName],
                cancellationToken,
                requireAdministrator: !_commandRunner.IsAdministrator());
        }
    }

    private static string ToCommandFailure(string prefix, CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.Output)
            ? $"код {result.ExitCode}"
            : result.Output.Trim();
        return $"{prefix}: {details}";
    }
}

public interface ISystemCommandRunner
{
    bool IsAdministrator();

    Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool requireAdministrator = false);

    Task<CommandResult> RunPowerShellScriptAsync(
        string scriptPath,
        CancellationToken cancellationToken,
        bool requireAdministrator = false);

    Task<CommandResult> StartDetachedAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        bool requireAdministrator = false);

    void StopProcessesByPath(string executablePath);
    bool IsProcessRunning(string executablePath);
}

public sealed record CommandResult(
    int ExitCode,
    string Output,
    int? ProcessId = null);

public sealed class SystemCommandRunner : ISystemCommandRunner
{
    public bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool requireAdministrator = false)
    {
        if (requireAdministrator)
            return await RunElevatedAsync(fileName, arguments, cancellationToken);

        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Не удалось запустить {fileName}.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new CommandResult(
            process.ExitCode,
            (await outputTask) + (await errorTask),
            process.Id);
    }

    public async Task<CommandResult> StartDetachedAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        bool requireAdministrator = false)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = requireAdministrator,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? string.Empty
        };
        if (requireAdministrator)
            startInfo.Verb = "runas";
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        var process = StartProcess(startInfo, fileName);
        await Task.Delay(800, cancellationToken);
        process.Refresh();
        if (process.HasExited)
        {
            var exitCode = process.ExitCode;
            process.Dispose();
            return new CommandResult(
                exitCode == 0 ? 1 : exitCode,
                "GoodbyeDPI завершился сразу после запуска. Обычно это означает, что нет прав администратора или WinDivert не смог загрузиться.");
        }

        var processId = process.Id;
        process.Dispose();
        return new CommandResult(0, string.Empty, processId);
    }

    public async Task<CommandResult> RunPowerShellScriptAsync(
        string scriptPath,
        CancellationToken cancellationToken,
        bool requireAdministrator = false)
    {
        var arguments = new[]
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath
        };

        if (!requireAdministrator)
            return await RunAsync("powershell.exe", arguments, cancellationToken);

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = StartProcess(startInfo, "powershell.exe");
        await process.WaitForExitAsync(cancellationToken);
        return new CommandResult(process.ExitCode, string.Empty, process.Id);
    }

    private static async Task<CommandResult> RunElevatedAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = StartProcess(startInfo, fileName);
        await process.WaitForExitAsync(cancellationToken);
        return new CommandResult(process.ExitCode, string.Empty, process.Id);
    }

    private static Process StartProcess(ProcessStartInfo startInfo, string fileName)
    {
        try
        {
            return Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Не удалось запустить {fileName}.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException(
                "Пользователь отменил запрос прав администратора.",
                exception);
        }
    }

    public void StopProcessesByPath(string executablePath)
    {
        var fullPath = Path.GetFullPath(executablePath);
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executablePath)))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                }
            }
            catch
            {
                // Some system processes do not expose MainModule to the current user.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public bool IsProcessRunning(string executablePath)
    {
        var fullPath = Path.GetFullPath(executablePath);
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executablePath)))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, fullPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                return true;
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }
}
