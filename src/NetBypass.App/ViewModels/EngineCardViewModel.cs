using NetBypass.Core.Models;
using System.Windows.Input;

namespace NetBypass.App.ViewModels;

public sealed class EngineCardViewModel(
    string id,
    string name,
    BypassEngineKind kind,
    string state,
    bool isEnabled,
    IReadOnlyList<string> supportedServices,
    string description,
    string nextStep,
    bool showDownloadButton = false,
    bool showRemoveButton = false,
    bool isSelected = false,
    ICommand? downloadCommand = null,
    ICommand? removeCommand = null,
    ICommand? selectCommand = null)
{
    public string Id => id;
    public string Name => name;
    public BypassEngineKind Kind => kind;
    public string Category => kind switch
    {
        BypassEngineKind.AntiDpi => "Anti-DPI",
        _ => "Движки"
    };
    public string State => state;
    public bool IsEnabled => isEnabled;
    public string StatusColor => isEnabled ? "#61D6A3" : "#8D909D";
    public string SupportedServicesText => supportedServices.Count == 0
        ? "Сервисы будут добавлены позже"
        : string.Join(", ", supportedServices);
    public string Description => description;
    public string NextStep => nextStep;
    public bool ShowDownloadButton => showDownloadButton;
    public bool ShowRemoveButton => showRemoveButton;
    public bool IsSelected => isSelected;
    public bool ShowSelectButton => IsEnabled && !IsSelected && selectCommand is not null;
    public string SelectButtonText => IsSelected ? "Используется" : "Использовать";
    public ICommand? DownloadCommand => downloadCommand;
    public ICommand? RemoveCommand => removeCommand;
    public ICommand? SelectCommand => selectCommand;
}
