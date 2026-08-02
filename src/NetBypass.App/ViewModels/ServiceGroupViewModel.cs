namespace NetBypass.App.ViewModels;

public sealed class ServiceGroupViewModel(
    string name,
    IReadOnlyList<ServiceItemViewModel> services)
{
    public string Name => name;
    public IReadOnlyList<ServiceItemViewModel> Services => services;
    public int Count => services.Count;
    public string CountText => $"{Count} {GetServiceWord(Count)}";

    private static string GetServiceWord(int count)
    {
        var lastTwo = count % 100;
        if (lastTwo is >= 11 and <= 14)
            return "сервисов";

        return (count % 10) switch
        {
            1 => "сервис",
            2 or 3 or 4 => "сервиса",
            _ => "сервисов"
        };
    }
}
