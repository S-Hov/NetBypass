namespace NetBypass.Core.Services;

public sealed class StartupTaskService(ISystemCommandRunner? commandRunner = null)
{
    public const string TaskName = "NetBypass Startup";
    public const string BackgroundArgument = "--background";

    private readonly ISystemCommandRunner _commandRunner = commandRunner ?? new SystemCommandRunner();

    public async Task<StartupTaskResult> SetEnabledAsync(
        bool enabled,
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return new StartupTaskResult(false, "Автозапуск поддерживается только в Windows.");

        if (enabled)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                return new StartupTaskResult(false, "Не удалось определить файл NetBypass для автозапуска.");

            var taskCommand = $"\"{Path.GetFullPath(executablePath)}\" {BackgroundArgument}";
            var result = await _commandRunner.RunAsync(
                "schtasks.exe",
                [
                    "/Create",
                    "/TN",
                    TaskName,
                    "/SC",
                    "ONLOGON",
                    "/DELAY",
                    "0000:10",
                    "/TR",
                    taskCommand,
                    "/RL",
                    "HIGHEST",
                    "/F"
                ],
                cancellationToken,
                requireAdministrator: !_commandRunner.IsAdministrator());

            return result.ExitCode == 0
                ? new StartupTaskResult(true, "NetBypass будет запускать выбранные движки при входе в Windows.")
                : new StartupTaskResult(false, ToFailure("Не удалось включить автозапуск", result));
        }

        var deleteResult = await _commandRunner.RunAsync(
            "schtasks.exe",
            ["/Delete", "/TN", TaskName, "/F"],
            cancellationToken,
            requireAdministrator: !_commandRunner.IsAdministrator());

        // Deletion is intentionally idempotent: a missing task already means startup is disabled.
        return deleteResult.ExitCode == 0 || IsMissingTask(deleteResult.Output)
            ? new StartupTaskResult(true, "Автозапуск NetBypass выключен.")
            : new StartupTaskResult(false, ToFailure("Не удалось выключить автозапуск", deleteResult));
    }

    private static bool IsMissingTask(string output) =>
        output.Contains("cannot find", StringComparison.OrdinalIgnoreCase)
        || output.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
        || output.Contains("не удается найти", StringComparison.OrdinalIgnoreCase)
        || output.Contains("не существует", StringComparison.OrdinalIgnoreCase);

    private static string ToFailure(string prefix, CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.Output)
            ? $"код {result.ExitCode}"
            : result.Output.Trim();
        return $"{prefix}: {details}";
    }
}

public sealed record StartupTaskResult(bool IsSuccess, string Message);
