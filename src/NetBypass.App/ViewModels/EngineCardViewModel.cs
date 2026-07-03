using NetBypass.Core.Models;

namespace NetBypass.App.ViewModels;

public sealed class EngineCardViewModel(
    string name,
    BypassEngineKind kind,
    string state,
    bool isEnabled,
    IReadOnlyList<string> supportedServices,
    string description,
    string nextStep)
{
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
}
