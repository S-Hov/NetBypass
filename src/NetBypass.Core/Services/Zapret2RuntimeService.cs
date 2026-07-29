using System.Text;
using NetBypass.Core.Models;

namespace NetBypass.Core.Services;

public sealed class Zapret2RuntimeService
{
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

    private readonly Zapret2InstallService _installService;
    private readonly ISystemCommandRunner _commandRunner;

    public Zapret2RuntimeService(
        Zapret2InstallService installService,
        string? runtimeRoot = null,
        ISystemCommandRunner? commandRunner = null)
    {
        _installService = installService;
        RuntimeRoot = runtimeRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetBypass",
            "Runtime",
            "Zapret2");
        _commandRunner = commandRunner ?? new SystemCommandRunner();
    }

    public string RuntimeRoot { get; }
    public string HostlistPath => Path.Combine(RuntimeRoot, "selected-hosts.txt");
    public string ActiveConfigPath => Path.Combine(RuntimeRoot, "active.conf");

    public bool IsEnabled() =>
        _installService.IsInstalled()
        && _commandRunner.IsProcessRunning(_installService.ExecutablePath);

    public EngineAvailability CheckAvailability()
    {
        if (!_installService.IsInstalled())
            return new EngineAvailability(BypassEngineState.NotInstalled, "zapret2 не скачан.");
        if (!OperatingSystem.IsWindows())
            return new EngineAvailability(BypassEngineState.Unavailable, "zapret2 runtime поддерживается только на Windows.");
        if (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
            != System.Runtime.InteropServices.Architecture.X64)
        {
            return new EngineAvailability(
                BypassEngineState.Unavailable,
                "Первая версия интеграции zapret2 поддерживает только Windows x64.");
        }

        return new EngineAvailability(
            IsEnabled() ? BypassEngineState.Running : BypassEngineState.Available,
            IsEnabled() ? "zapret2 работает." : "zapret2 готов к запуску.",
            _installService.ExecutablePath);
    }

    public async Task<EngineRunResult> EnableAsync(
        IEnumerable<string> selectedServiceIds,
        IReadOnlyList<string> strategyArguments,
        bool forceRestart = false,
        CancellationToken cancellationToken = default)
    {
        var serviceIds = selectedServiceIds
            .Where(DomainsByService.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (serviceIds.Length == 0)
            return new EngineRunResult(false, "Anti-DPI сервисы не выбраны.");

        var availability = CheckAvailability();
        if (availability.State is BypassEngineState.NotInstalled or BypassEngineState.Unavailable)
            return new EngineRunResult(false, availability.Message);

        if (!forceRestart && IsEnabled())
        {
            return new EngineRunResult(
                true,
                $"zapret2 уже работает для сервисов: {string.Join(", ", serviceIds)}.");
        }

        if (forceRestart)
            await DisableAsync(cancellationToken);

        Directory.CreateDirectory(RuntimeRoot);
        var domains = BuildHostlist(serviceIds);
        await File.WriteAllLinesAsync(HostlistPath, domains, new UTF8Encoding(false), cancellationToken);

        IReadOnlyList<string> arguments;
        try
        {
            arguments = BuildArguments(strategyArguments, HostlistPath);
        }
        catch
        {
            CleanupRuntimeFiles();
            throw;
        }

        await File.WriteAllLinesAsync(
            ActiveConfigPath,
            arguments.Select(FormatConfigArgument),
            new UTF8Encoding(false),
            cancellationToken);

        var dryRunArguments = new[] { "--dry-run" }.Concat(arguments).ToArray();
        var dryRun = await _commandRunner.RunAsync(
            _installService.ExecutablePath,
            dryRunArguments,
            cancellationToken,
            requireAdministrator: false);
        if (dryRun.ExitCode != 0)
        {
            CleanupRuntimeFiles();
            return new EngineRunResult(
                false,
                ToCommandFailure("Конфигурация zapret2 не прошла предварительную проверку", dryRun));
        }

        var start = await _commandRunner.StartDetachedAsync(
            _installService.ExecutablePath,
            arguments,
            Path.GetDirectoryName(_installService.ExecutablePath),
            cancellationToken,
            requireAdministrator: !_commandRunner.IsAdministrator());
        if (start.ExitCode != 0)
        {
            CleanupRuntimeFiles();
            return new EngineRunResult(false, ToCommandFailure("Не удалось запустить zapret2", start));
        }

        return new EngineRunResult(
            true,
            $"zapret2 v{Zapret2InstallService.EngineVersion} включён для сервисов: {string.Join(", ", serviceIds)}.",
            start.ProcessId);
    }

    public Task<EngineStopResult> DisableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_installService.IsInstalled())
            _commandRunner.StopProcessesByPath(_installService.ExecutablePath);
        CleanupRuntimeFiles();
        return Task.FromResult(new EngineStopResult(true, "zapret2 остановлен."));
    }

    public static IReadOnlyList<string> BuildHostlist(IEnumerable<string> selectedServiceIds) =>
        selectedServiceIds
            .Where(DomainsByService.ContainsKey)
            .SelectMany(id => DomainsByService[id])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<string> BuildArguments(
        IEnumerable<string> strategyArguments,
        string hostlistPath)
    {
        var arguments = new List<string>
        {
            "--comment=NetBypass",
            "--wf-tcp-out=443",
            $"--lua-init=@{_installService.LuaLibraryPath}",
            $"--lua-init=@{_installService.LuaAntiDpiPath}"
        };

        foreach (var rawArgument in strategyArguments)
        {
            if (string.IsNullOrWhiteSpace(rawArgument)
                || rawArgument.Contains('\0')
                || rawArgument.Contains('\r')
                || rawArgument.Contains('\n'))
            {
                throw new InvalidDataException("Стратегия zapret2 содержит недопустимый аргумент.");
            }

            var argument = rawArgument
                .Replace("{hostlist}", hostlistPath, StringComparison.OrdinalIgnoreCase)
                .Replace("{quic-blob}", _installService.QuicBlobPath, StringComparison.OrdinalIgnoreCase);
            if (argument.Contains('{') || argument.StartsWith('@'))
                throw new InvalidDataException($"Стратегия zapret2 содержит неизвестный шаблон: {rawArgument}.");
            if (argument.StartsWith("--wf-", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("--lua-init", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("--debug", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("--daemon", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("--pidfile", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("--writable", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Параметром {rawArgument} управляет runtime zapret2.");
            }

            arguments.Add(argument);
        }

        return arguments;
    }

    private void CleanupRuntimeFiles()
    {
        if (File.Exists(HostlistPath))
            File.Delete(HostlistPath);
        if (File.Exists(ActiveConfigPath))
            File.Delete(ActiveConfigPath);
    }

    private static string FormatConfigArgument(string argument) =>
        argument.Any(char.IsWhiteSpace)
            ? $"\"{argument.Replace("\"", "\\\"")}\""
            : argument;

    private static string ToCommandFailure(string prefix, CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.Output)
            ? $"код {result.ExitCode}"
            : result.Output.Trim();
        return $"{prefix}: {details}";
    }
}
