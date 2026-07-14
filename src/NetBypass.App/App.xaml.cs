using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NetBypass.App.ViewModels;
using NetBypass.Core.Services;

namespace NetBypass.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Debug.WriteLine($"Unhandled NetBypass exception: {args.ExceptionObject}");

        if (!IsAdministrator())
        {
            RestartAsAdministrator(desktop.Args ?? []);
            desktop.Shutdown();
            return;
        }

        if ((desktop.Args ?? []).Contains(StartupTaskService.BackgroundArgument, StringComparer.OrdinalIgnoreCase))
        {
            _ = RestoreBackgroundStateAsync(desktop);
            base.OnFrameworkInitializationCompleted();
            return;
        }

        desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RestoreBackgroundStateAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            await new MainViewModel().RestoreBackgroundStateAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Не удалось восстановить фоновые движки: {exception}");
        }
        finally
        {
            desktop.Shutdown();
        }
    }

    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
            return true;

        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RestartAsAdministrator(IEnumerable<string> arguments)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Не удалось определить путь к NetBypass.");

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = string.Join(" ", arguments.Select(QuoteArgument)),
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            Debug.WriteLine("Запуск с правами администратора отменён.");
        }
    }

    private static string QuoteArgument(string argument) =>
        $"\"{argument.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
