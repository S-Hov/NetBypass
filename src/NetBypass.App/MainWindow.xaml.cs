using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using NetBypass.App.ViewModels;
using System.Windows.Input;

namespace NetBypass.App;

public partial class MainWindow : Window
{
    private ICommand? _pendingEngineRemoval;
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

    private void RemoveEngine_Click(object? sender, RoutedEventArgs e)
    {
        _pendingEngineRemoval = (sender as Button)?.Tag as ICommand;
        RemoveEngineOverlay.IsVisible = _pendingEngineRemoval is not null;
    }

    private void CancelRemoveEngine_Click(object? sender, RoutedEventArgs e)
    {
        RemoveEngineOverlay.IsVisible = false;
        _pendingEngineRemoval = null;
    }

    private void ConfirmRemoveEngine_Click(object? sender, RoutedEventArgs e)
    {
        RemoveEngineOverlay.IsVisible = false;
        var command = _pendingEngineRemoval;
        _pendingEngineRemoval = null;
        if (command?.CanExecute(null) == true)
            command.Execute(null);
    }
}
