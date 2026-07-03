using NetBypass.App.Infrastructure;

namespace NetBypass.App.ViewModels;

public sealed class AntiDpiServiceItemViewModel(
    string id,
    string name,
    string engineName,
    string description,
    bool isSelected) : ObservableObject
{
    private bool _isSelected = isSelected;

    public string Id => id;
    public string Name => name;
    public string EngineName => engineName;
    public string Description => description;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
