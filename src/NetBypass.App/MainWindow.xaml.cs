using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using NetBypass.App.ViewModels;

namespace NetBypass.App;

public partial class MainWindow : Window
{
    private Border RestoreOverlay => this.FindControl<Border>(nameof(RestoreOverlay))!;
    private Border RemoveEngineOverlay => this.FindControl<Border>(nameof(RemoveEngineOverlay))!;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        try
        {
            DataContext = new MainViewModel();
        }
        catch (Exception exception)
        {
            Title = "Ошибка запуска NetBypass";
            Content = new TextBlock
            {
                Text = $"Не удалось запустить NetBypass:\n{exception.Message}",
                Margin = new Avalonia.Thickness(32),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };
        }
    }

    private void RestoreHosts_Click(object? sender, RoutedEventArgs e) =>
        RestoreOverlay.IsVisible = true;

    private void CancelRestore_Click(object? sender, RoutedEventArgs e) =>
        RestoreOverlay.IsVisible = false;

    private void ConfirmRestore_Click(object? sender, RoutedEventArgs e)
    {
        RestoreOverlay.IsVisible = false;
        if (DataContext is MainViewModel viewModel)
            viewModel.RestoreConfirmed();
    }

    private void RemoveEngine_Click(object? sender, RoutedEventArgs e) =>
        RemoveEngineOverlay.IsVisible = true;

    private void CancelRemoveEngine_Click(object? sender, RoutedEventArgs e) =>
        RemoveEngineOverlay.IsVisible = false;

    private void ConfirmRemoveEngine_Click(object? sender, RoutedEventArgs e)
    {
        RemoveEngineOverlay.IsVisible = false;
        if (DataContext is MainViewModel viewModel
            && viewModel.RemoveGoodbyeDpiCommand.CanExecute(null))
        {
            viewModel.RemoveGoodbyeDpiCommand.Execute(null);
        }
    }
}
