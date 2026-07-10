using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using NetBypass.App.ViewModels;
using NetBypass.Core.Services;

namespace NetBypass.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        if (!IsAdministrator())
        {
            RestartAsAdministrator(e.Args);
            Shutdown();
            return;
        }

        if (e.Args.Contains(StartupTaskService.BackgroundArgument, StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var viewModel = new MainViewModel();
                await viewModel.RestoreBackgroundStateAsync();
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Не удалось восстановить фоновые движки: {exception}");
            }
            finally
            {
                Shutdown();
            }
            return;
        }

        new MainWindow().Show();
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"NetBypass столкнулся с ошибкой:\n\n{e.Exception.Message}",
            "Ошибка NetBypass",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RestartAsAdministrator(IEnumerable<string> arguments)
    {
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
            MessageBox.Show(
                "Для изменения системного hosts нужны права администратора.",
                "NetBypass",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private static string QuoteArgument(string argument) =>
        $"\"{argument.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
