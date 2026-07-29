using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using NetBypass.App.Infrastructure;
using NetBypass.Core.Models;
using NetBypass.Core.Services;

namespace NetBypass.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly HashSet<string> DisabledByDefault =
    [
        "guided-hacking",
        "tria-ge",
        "openbittorrent",
        "rutor",
        "pump-fun"
    ];

    private static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(6);

    private readonly HostsFileService _hostsService = new();
    private readonly SettingsService _settingsService = new();
    private readonly DiagnosticStore _diagnosticStore = new();
    private readonly EndpointSelectionStore _endpointSelectionStore = new();
    private readonly GoodbyeDpiInstallService _goodbyeDpiInstallService = new();
    private readonly GoodbyeDpiRuntimeService _goodbyeDpiRuntimeService;
    private readonly GoodbyeDpiStrategyOptimizer _goodbyeDpiStrategyOptimizer;
    private readonly AntiDpiStrategySelectionStore _antiDpiStrategySelectionStore = new();
    private readonly StartupTaskService _startupTaskService = new();
    private readonly NetworkDiagnosticService _diagnosticService = new(
        new CloudflareGoogleDohResolver(),
        new EndpointProbe());
    private HostsState _hostsState;
    private VerificationState _verificationState;
    private string _operationMessage = string.Empty;
    private AppPage _currentPage = AppPage.Home;
    private bool _isBusy;
    private PowerOperation _powerOperation;
    private int _diagnosticCompleted;
    private int _diagnosticTotal;
    private string _currentDiagnosticService = string.Empty;
    private string _cleanupTitle = string.Empty;
    private bool _isGoodbyeDpiInstalled;
    private bool _isGoodbyeDpiRuntimeEnabled;
    private string _engineOperationMessage = string.Empty;
    private bool _startWithWindows;
    private bool _multiCheckEnabled;
    private int _diagnosticAttempts;
    private bool _isStartupSettingBusy;
    private string _startupOperationMessage = string.Empty;
    private HashSet<string> _unavailableServiceIds = new(StringComparer.OrdinalIgnoreCase);
    private AntiDpiOptimizationResult? _lastAntiDpiOptimizationResult;

    public MainViewModel()
    {
        _goodbyeDpiRuntimeService = new GoodbyeDpiRuntimeService(_goodbyeDpiInstallService);
        var strategyCatalogPath = Path.Combine(
            AppContext.BaseDirectory,
            "EngineProfiles",
            "goodbyedpi.json");
        var strategyCatalog = new AntiDpiStrategyCatalogService().Load(strategyCatalogPath);
        _goodbyeDpiStrategyOptimizer = new GoodbyeDpiStrategyOptimizer(
            _goodbyeDpiRuntimeService,
            strategyCatalog,
            selectionStore: _antiDpiStrategySelectionStore);
        var modulesPath = Path.Combine(AppContext.BaseDirectory, "Modules");
        var profiles = new ServiceProfileLoader().LoadDirectory(modulesPath);
        var settings = _settingsService.Load();
        _isGoodbyeDpiInstalled = _goodbyeDpiInstallService.IsInstalled();
        _isGoodbyeDpiRuntimeEnabled = _goodbyeDpiRuntimeService.IsEnabled();
        _startWithWindows = settings?.StartWithWindows ?? false;
        _multiCheckEnabled = settings?.MultiCheckEnabled ?? true;
        _diagnosticAttempts = Math.Clamp(settings?.DiagnosticAttempts ?? 3, 2, 10);

        Services = new ObservableCollection<ServiceItemViewModel>(
            profiles.Select(profile => new ServiceItemViewModel(
                profile,
                settings?.SelectedModuleIds?.Contains(profile.Id)
                    ?? !DisabledByDefault.Contains(profile.Id))));
        AntiDpiServices = new ObservableCollection<AntiDpiServiceItemViewModel>(
            CreateAntiDpiServices(settings?.SelectedAntiDpiServiceIds));

        foreach (var service in Services)
            service.PropertyChanged += OnServicePropertyChanged;
        foreach (var service in AntiDpiServices)
            service.PropertyChanged += OnAntiDpiServicePropertyChanged;

        Diagnostics = new ObservableCollection<DiagnosticItemViewModel>();
        ServiceActivity = new ObservableCollection<OperationTraceItemViewModel>();
        Engines = new ObservableCollection<EngineCardViewModel>(CreateEngineCards(IsGoodbyeDpiInstalled));
        CleanupItems = new ObservableCollection<string>();
        EngineActivityLog = new ObservableCollection<string>();
        LoadStoredDiagnostics();

        PowerCommand = new AsyncRelayCommand(
            TogglePowerAsync,
            () => !IsBusy && HostsState != HostsState.Corrupted);
        ApplyCommand = new AsyncRelayCommand(
            ApplyFromServicesAsync,
            () => !IsBusy
                  && HasSelectedBypassTarget
                  && HostsState != HostsState.Corrupted);
        DiagnoseCommand = new AsyncRelayCommand(
            DiagnoseSelectedAsync,
            () => !IsBusy && Services.Any(item => item.IsSelected));
        ApplyReachableCommand = new AsyncRelayCommand(
            ApplyReachableServicesAsync,
            () => !IsBusy && _unavailableServiceIds.Count > 0);
        SelectAllCommand = new RelayCommand(() => SetAll(true));
        ClearAllCommand = new RelayCommand(() => SetAll(false));
        ShowHomeCommand = new RelayCommand(() => CurrentPage = AppPage.Home);
        ShowServicesCommand = new RelayCommand(() => CurrentPage = AppPage.Services);
        ShowEnginesCommand = new RelayCommand(() => CurrentPage = AppPage.Engines);
        ShowDiagnosticsCommand = new RelayCommand(() => CurrentPage = AppPage.Diagnostics);
        ShowSettingsCommand = new RelayCommand(() => CurrentPage = AppPage.Settings);
        DownloadGoodbyeDpiCommand = new AsyncRelayCommand(
            DownloadGoodbyeDpiAsync,
            () => !IsBusy && !IsGoodbyeDpiInstalled);
        RemoveGoodbyeDpiCommand = new AsyncRelayCommand(
            RemoveGoodbyeDpiAsync,
            () => !IsBusy && IsGoodbyeDpiInstalled);

        RefreshState();
    }

    public ObservableCollection<ServiceItemViewModel> Services { get; }
    public ObservableCollection<AntiDpiServiceItemViewModel> AntiDpiServices { get; }
    public ObservableCollection<DiagnosticItemViewModel> Diagnostics { get; }
    public ObservableCollection<OperationTraceItemViewModel> ServiceActivity { get; }
    public ObservableCollection<EngineCardViewModel> Engines { get; }
    public ObservableCollection<string> CleanupItems { get; }
    public ObservableCollection<string> EngineActivityLog { get; }
    public AsyncRelayCommand PowerCommand { get; }
    public AsyncRelayCommand ApplyCommand { get; }
    public AsyncRelayCommand DiagnoseCommand { get; }
    public AsyncRelayCommand ApplyReachableCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearAllCommand { get; }
    public RelayCommand ShowHomeCommand { get; }
    public RelayCommand ShowServicesCommand { get; }
    public RelayCommand ShowEnginesCommand { get; }
    public RelayCommand ShowDiagnosticsCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }
    public AsyncRelayCommand DownloadGoodbyeDpiCommand { get; }
    public AsyncRelayCommand RemoveGoodbyeDpiCommand { get; }

    public HostsState HostsState
    {
        get => _hostsState;
        private set
        {
            if (!SetProperty(ref _hostsState, value))
                return;

            RaiseStateProperties();
        }
    }

    public VerificationState VerificationState
    {
        get => _verificationState;
        private set
        {
            if (!SetProperty(ref _verificationState, value))
                return;

            RaiseStateProperties();
        }
    }

    public AppPage CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (!SetProperty(ref _currentPage, value))
                return;

            OnPropertyChanged(nameof(IsHomePage));
            OnPropertyChanged(nameof(IsServicesPage));
            OnPropertyChanged(nameof(IsEnginesPage));
            OnPropertyChanged(nameof(IsDiagnosticsPage));
            OnPropertyChanged(nameof(IsSettingsPage));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;

            RaiseStateProperties();
            OnPropertyChanged(nameof(HasDiagnosticProgress));
            OnPropertyChanged(nameof(DiagnosticButtonText));
            DownloadGoodbyeDpiCommand?.RaiseCanExecuteChanged();
            RemoveGoodbyeDpiCommand?.RaiseCanExecuteChanged();
        }
    }

    public PowerOperation PowerOperation
    {
        get => _powerOperation;
        private set
        {
            if (!SetProperty(ref _powerOperation, value))
                return;

            OnPropertyChanged(nameof(IsConnecting));
            OnPropertyChanged(nameof(IsDisconnecting));
            OnPropertyChanged(nameof(IsPowerTransitioning));
            OnPropertyChanged(nameof(PowerButtonLabel));
            RaiseStateProperties();
        }
    }

    public bool IsHomePage => CurrentPage == AppPage.Home;
    public bool IsServicesPage => CurrentPage == AppPage.Services;
    public bool IsEnginesPage => CurrentPage == AppPage.Engines;
    public bool IsDiagnosticsPage => CurrentPage == AppPage.Diagnostics;
    public bool IsSettingsPage => CurrentPage == AppPage.Settings;
    public bool IsPowerOn =>
        HostsState is HostsState.Active or HostsState.ChangesPending
        || IsGoodbyeDpiRuntimeEnabled;
    public bool IsCorrupted => HostsState == HostsState.Corrupted;
    public bool IsConnecting => PowerOperation == PowerOperation.Connecting;
    public bool IsDisconnecting => PowerOperation == PowerOperation.Disconnecting;
    public bool IsPowerTransitioning => PowerOperation != PowerOperation.None;
    public bool ShowHomeActivity => IsPowerOn || IsPowerTransitioning;
    public bool HasUnavailableServices => _unavailableServiceIds.Count > 0;
    public bool HasAvailabilitySummary => IsPowerOn && SelectedServiceCount > 0;
    public bool HasCleanupItems => CleanupItems.Count > 0;
    public bool HasEngineActivityLog => EngineActivityLog.Count > 0;
    public bool HasServiceActivity => ServiceActivity.Count > 0;
    public bool HasLiveActivity => HasServiceActivity || HasEngineActivityLog;
    public bool HasNoEngineActivityLog => !HasEngineActivityLog;
    public bool HasNoServiceActivity => !HasServiceActivity;
    public string CleanupTitle
    {
        get => _cleanupTitle;
        private set => SetProperty(ref _cleanupTitle, value);
    }
    public bool HasPartialAvailability =>
        HasAvailabilitySummary && AvailableServiceCount < SelectedServiceCount;
    public bool IsGoodbyeDpiInstalled
    {
        get => _isGoodbyeDpiInstalled;
        private set
        {
            if (!SetProperty(ref _isGoodbyeDpiInstalled, value))
                return;

            OnPropertyChanged(nameof(IsAntiDpiServicesEnabled));
            OnPropertyChanged(nameof(IsAntiDpiEngineMissing));
            OnPropertyChanged(nameof(AntiDpiInstallStatus));
            OnPropertyChanged(nameof(GoodbyeDpiRuntimeStatus));
            OnPropertyChanged(nameof(AntiDpiSelectionSummary));
            OnPropertyChanged(nameof(HasSelectedBypassTarget));
            DownloadGoodbyeDpiCommand?.RaiseCanExecuteChanged();
            RemoveGoodbyeDpiCommand?.RaiseCanExecuteChanged();
            RebuildEngineCards();
        }
    }
    public bool IsGoodbyeDpiRuntimeEnabled
    {
        get => _isGoodbyeDpiRuntimeEnabled;
        private set
        {
            if (!SetProperty(ref _isGoodbyeDpiRuntimeEnabled, value))
                return;

            OnPropertyChanged(nameof(IsPowerOn));
            OnPropertyChanged(nameof(GoodbyeDpiRuntimeStatus));
            OnPropertyChanged(nameof(PowerButtonLabel));
            RaiseStateProperties();
        }
    }
    public bool IsAntiDpiServicesEnabled => IsGoodbyeDpiInstalled;
    public bool IsAntiDpiEngineMissing => !IsGoodbyeDpiInstalled;
    public string AntiDpiInstallStatus => IsGoodbyeDpiInstalled
        ? "GoodbyeDPI скачан. Эти сервисы могут автоматически включать Anti-DPI режим."
        : "Чтобы включить эти сервисы, скачайте хотя бы один Anti-DPI движок.";
    public string GoodbyeDpiRuntimeStatus => !IsGoodbyeDpiInstalled
        ? "GoodbyeDPI ещё не скачан."
        : IsGoodbyeDpiRuntimeEnabled
            ? "GoodbyeDPI запущен, а выбранная стратегия прошла TCP/TLS-проверку."
            : "GoodbyeDPI скачан, но фоновый режим сейчас выключен.";
    public bool HasSelectedBypassTarget =>
        Services.Any(item => item.IsSelected)
        || (IsGoodbyeDpiInstalled && AntiDpiServices.Any(item => item.IsSelected));
    public string AntiDpiSelectionSummary =>
        !IsGoodbyeDpiInstalled
            ? "Anti-DPI блок пока неактивен."
            :
        AntiDpiServices.Count(item => item.IsSelected) == 0
            ? "GoodbyeDPI не будет запускаться автоматически для этих сервисов."
            : $"Anti-DPI сервисов выбрано: {AntiDpiServices.Count(item => item.IsSelected)}.";
    public string EngineOperationMessage
    {
        get => _engineOperationMessage;
        private set
        {
            if (SetProperty(ref _engineOperationMessage, value))
                OnPropertyChanged(nameof(HasEngineOperationMessage));
        }
    }
    public bool HasEngineOperationMessage => !string.IsNullOrWhiteSpace(EngineOperationMessage);
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (!SetProperty(ref _startWithWindows, value))
                return;

            OnPropertyChanged(nameof(StartupStatus));
            _ = UpdateStartupSettingAsync(value);
        }
    }
    public bool IsStartupSettingBusy
    {
        get => _isStartupSettingBusy;
        private set
        {
            if (SetProperty(ref _isStartupSettingBusy, value))
                OnPropertyChanged(nameof(IsStartupSettingAvailable));
        }
    }
    public bool IsStartupSettingAvailable => !IsStartupSettingBusy;
    public string StartupOperationMessage
    {
        get => _startupOperationMessage;
        private set
        {
            if (SetProperty(ref _startupOperationMessage, value))
                OnPropertyChanged(nameof(HasStartupOperationMessage));
        }
    }
    public bool HasStartupOperationMessage => !string.IsNullOrWhiteSpace(StartupOperationMessage);
    public string StartupStatus => StartWithWindows
        ? "Включён: после входа в Windows NetBypass незаметно восстановит выбранные движки."
        : "Выключен: после перезагрузки внешние движки нужно будет включить вручную.";
    public bool MultiCheckEnabled
    {
        get => _multiCheckEnabled;
        set
        {
            if (!SetProperty(ref _multiCheckEnabled, value))
                return;

            OnPropertyChanged(nameof(IsDiagnosticAttemptsEnabled));
            SaveDiagnosticSettings();
        }
    }
    public int DiagnosticAttempts
    {
        get => _diagnosticAttempts;
        set
        {
            var normalized = Math.Clamp(value, 2, 10);
            if (!SetProperty(ref _diagnosticAttempts, normalized))
                return;

            SaveDiagnosticSettings();
        }
    }
    public bool IsDiagnosticAttemptsEnabled => MultiCheckEnabled;
    public int SelectedServiceCount => Services.Count(item => item.IsSelected);
    public int AvailableServiceCount
    {
        get
        {
            var snapshot = _diagnosticStore.Load();
            if (snapshot is null)
                return 0;

            var selectedIds = Services.Where(item => item.IsSelected)
                .Select(item => item.Profile.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return snapshot.Services.Count(result =>
                result.IsReachable && selectedIds.Contains(result.ServiceId));
        }
    }
    public string AvailabilitySummary =>
        $"Доступно сервисов: {AvailableServiceCount} из {SelectedServiceCount}";
    public bool HasDiagnosticProgress => IsBusy && DiagnosticTotal > 0;
    public string DiagnosticButtonText => IsBusy ? "Идёт проверка" : "Проверить выбранное";

    public int DiagnosticCompleted
    {
        get => _diagnosticCompleted;
        private set
        {
            if (SetProperty(ref _diagnosticCompleted, value))
            {
                OnPropertyChanged(nameof(DiagnosticProgressText));
                OnPropertyChanged(nameof(DiagnosticProgressPercent));
            }
        }
    }

    public int DiagnosticTotal
    {
        get => _diagnosticTotal;
        private set
        {
            if (SetProperty(ref _diagnosticTotal, value))
            {
                OnPropertyChanged(nameof(DiagnosticProgressText));
                OnPropertyChanged(nameof(DiagnosticProgressPercent));
                OnPropertyChanged(nameof(HasDiagnosticProgress));
            }
        }
    }

    public double DiagnosticProgressPercent =>
        DiagnosticTotal == 0 ? 0 : (double)DiagnosticCompleted / DiagnosticTotal * 100;

    public string CurrentDiagnosticService
    {
        get => _currentDiagnosticService;
        private set
        {
            if (SetProperty(ref _currentDiagnosticService, value))
                OnPropertyChanged(nameof(DiagnosticProgressText));
        }
    }

    public string DiagnosticProgressText => DiagnosticTotal == 0
        ? string.Empty
        : $"Проверено {DiagnosticCompleted} из {DiagnosticTotal}"
          + (string.IsNullOrWhiteSpace(CurrentDiagnosticService)
              ? string.Empty
              : $" · {CurrentDiagnosticService}");

    public string PowerButtonLabel => PowerOperation switch
    {
        PowerOperation.Connecting => "Подключение...",
        PowerOperation.Disconnecting => "Отключение...",
        _ => IsBusy
        ? "Проверка..."
        : IsPowerOn
        ? "Отключить"
        : HostsState switch
        {
            HostsState.Inactive => "Включить",
            _ => "Недоступно"
        }
    };

    public string StateTitle
    {
        get
        {
            return CurrentUiState switch
            {
                UiState.Disabled => "Не настроено",
                UiState.Checking => "Диагностика подключения",
                UiState.Disabling => "Отключение NetBypass",
                UiState.ChangesPending => "Требуется применить изменения",
                UiState.Corrupted => "Файл hosts требует внимания",
                UiState.ActiveVerified => "Все выбранные сервисы доступны",
                UiState.ActiveDegraded => "Записи применены частично",
                UiState.ActiveUnverified => "Записи применены, проверка устарела",
                _ => "Неизвестное состояние"
            };
        }
    }

    public string StateDescription
    {
        get
        {
            return CurrentUiState switch
            {
                UiState.Disabled => "Перед включением NetBypass проверит доступность адресов.",
                UiState.Checking => "Проверяем DoH, TCP и TLS для выбранных сервисов.",
                UiState.Disabling => "Удаляем управляемые записи и проверяем очистку.",
                UiState.ChangesPending => "Откройте «Сервисы» и сохраните выбранный список.",
                UiState.Corrupted => "Используйте восстановление управляемого блока.",
                UiState.ActiveVerified => AvailabilitySummary,
                UiState.ActiveDegraded => AvailabilitySummary,
                UiState.ActiveUnverified => "Откройте диагностику и повторите проверку.",
                _ => string.Empty
            };
        }
    }

    public string StateAccent => CurrentUiState switch
    {
        UiState.Disabled => "#7C5CFC",
        UiState.Checking => "#7C5CFC",
        UiState.Disabling => "#7C5CFC",
        UiState.ActiveVerified => "#61D6A3",
        UiState.ActiveDegraded => "#F2B84B",
        UiState.ActiveUnverified => "#F2B84B",
        UiState.ChangesPending => "#F2B84B",
        UiState.Corrupted => "#FF6B7A",
        _ => "#7C5CFC"
    };

    private UiState CurrentUiState
    {
        get
        {
            if (PowerOperation == PowerOperation.Disconnecting)
                return UiState.Disabling;

            if (IsBusy)
                return UiState.Checking;

            if (HostsState == HostsState.Corrupted)
                return UiState.Corrupted;

            if (IsGoodbyeDpiRuntimeEnabled && HostsState == HostsState.Inactive)
                return UiState.ActiveUnverified;

            if (HostsState == HostsState.Inactive)
                return UiState.Disabled;

            if (HostsState == HostsState.ChangesPending)
                return UiState.ChangesPending;

            if (HostsState == HostsState.Active
                && VerificationState == VerificationState.Verified
                && !HasPartialAvailability)
            {
                return UiState.ActiveVerified;
            }

            if (HostsState == HostsState.Active
                && (VerificationState == VerificationState.Unavailable
                    || HasPartialAvailability))
            {
                return UiState.ActiveDegraded;
            }

            if (HostsState == HostsState.Active)
                return UiState.ActiveUnverified;

            return UiState.Unknown;
        }
    }

    public string OperationMessage
    {
        get => _operationMessage;
        private set
        {
            if (SetProperty(ref _operationMessage, value))
                OnPropertyChanged(nameof(HasOperationMessage));
        }
    }

    public bool HasOperationMessage => !string.IsNullOrWhiteSpace(OperationMessage);

    private async Task DownloadGoodbyeDpiAsync()
    {
        IsBusy = true;
        EngineOperationMessage = "Скачиваем GoodbyeDPI...";
        try
        {
            var result = await _goodbyeDpiInstallService.InstallAsync();
            IsGoodbyeDpiInstalled = result.IsInstalled;
            EngineOperationMessage = result.Message;
            OperationMessage = result.IsInstalled
                ? "GoodbyeDPI скачан. Anti-DPI блок в сервисах активирован."
                : result.Message;
        }
        catch (Exception exception)
        {
            EngineOperationMessage = ToUserMessage("Не удалось скачать GoodbyeDPI", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RemoveGoodbyeDpiAsync()
    {
        var wasPowerOn = IsPowerOn;
        IsBusy = true;
        EngineOperationMessage = "Останавливаем GoodbyeDPI и удаляем его файлы...";
        ClearEngineActivityLog();
        AddEngineActivity("Останавливаем процесс и очищаем WinDivert.");
        try
        {
            await _goodbyeDpiRuntimeService.DisableAsync();
            IsGoodbyeDpiRuntimeEnabled = false;

            foreach (var service in AntiDpiServices)
                service.IsSelected = false;
            _lastAntiDpiOptimizationResult = null;
            _antiDpiStrategySelectionStore.Clear();

            if (wasPowerOn)
            {
                var regularModules = GetExpectedActiveModules(
                        Services.Where(item => item.IsSelected).ToArray())
                    .Where(module => module.Id != "anti-dpi-routing");
                ApplyManagedModules(regularModules, antiDpiAddresses: null);
                DnsCacheService.Flush();
            }

            _settingsService.Save(
                Services.Where(item => item.IsSelected).Select(item => item.Module.Id),
                []);
            var result = _goodbyeDpiInstallService.Uninstall();
            IsGoodbyeDpiInstalled = _goodbyeDpiInstallService.IsInstalled();
            EngineOperationMessage = result.Message;
            OperationMessage = result.Message;
            AddEngineActivity(result.Message);
            RefreshState();
        }
        catch (Exception exception)
        {
            IsGoodbyeDpiInstalled = _goodbyeDpiInstallService.IsInstalled();
            EngineOperationMessage = ToUserMessage("Не удалось удалить GoodbyeDPI", exception);
            OperationMessage = EngineOperationMessage;
            AddEngineActivity(EngineOperationMessage);
            RefreshState();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RestoreBackgroundStateAsync()
    {
        var selectedAntiDpi = AntiDpiServices
            .Where(item => item.IsSelected)
            .ToArray();

        if (IsGoodbyeDpiInstalled && selectedAntiDpi.Length > 0)
        {
            await SyncGoodbyeDpiAsync(selectedAntiDpi);
            var regularModules = GetExpectedActiveModules(
                    Services.Where(item => item.IsSelected).ToArray())
                .Where(module => module.Id != "anti-dpi-routing");
            ApplyManagedModules(
                regularModules,
                _lastAntiDpiOptimizationResult?.Addresses);
            DnsCacheService.Flush();
        }
    }

    private async Task UpdateStartupSettingAsync(bool enabled)
    {
        if (IsStartupSettingBusy)
            return;

        IsStartupSettingBusy = true;
        StartupOperationMessage = enabled
            ? "Включаем автозапуск..."
            : "Выключаем автозапуск...";
        try
        {
            var executablePath = Environment.ProcessPath ?? string.Empty;
            var result = await _startupTaskService.SetEnabledAsync(enabled, executablePath);
            StartupOperationMessage = result.Message;
            if (!result.IsSuccess)
            {
                _startWithWindows = !enabled;
                OnPropertyChanged(nameof(StartWithWindows));
                OnPropertyChanged(nameof(StartupStatus));
                return;
            }

            _settingsService.Save(
                Services.Where(item => item.IsSelected).Select(item => item.Module.Id),
                IsGoodbyeDpiInstalled
                    ? AntiDpiServices.Where(item => item.IsSelected).Select(item => item.Id)
                    : [],
                enabled);
        }
        catch (Exception exception)
        {
            _startWithWindows = !enabled;
            OnPropertyChanged(nameof(StartWithWindows));
            OnPropertyChanged(nameof(StartupStatus));
            StartupOperationMessage = ToUserMessage("Не удалось изменить автозапуск", exception);
        }
        finally
        {
            IsStartupSettingBusy = false;
        }
    }

    public void RestoreConfirmed()
    {
        RunSafely(() =>
        {
            ClearCleanupReport();
            _hostsService.Restore(Services.Select(item => item.Module));
            DnsCacheService.Flush();
            var cleanup = _hostsService.VerifyCleanup(Services.Select(item => item.Module));
            SetCleanupReport("Восстановление выполнено", cleanup, dnsFlushed: true);
            OperationMessage = cleanup.IsClean
                ? "Изменения NetBypass удалены. Остальные записи hosts сохранены."
                : "Восстановление выполнено, но проверка нашла хвосты NetBypass.";
            RefreshState();
        });
    }

    private async Task TogglePowerAsync()
    {
        if (IsPowerOn)
        {
            PowerOperation = PowerOperation.Disconnecting;
            try
            {
                ClearCleanupReport();
                _hostsService.Disable();
                await DisableGoodbyeDpiAsync();
                DnsCacheService.Flush();
                var cleanup = _hostsService.VerifyCleanup(Services.Select(item => item.Module));
                SetCleanupReport("Отключение выполнено", cleanup, dnsFlushed: true);
                OperationMessage = cleanup.IsClean
                    ? "NetBypass отключён. Проверка очистки пройдена."
                    : "NetBypass отключён, но проверка нашла хвосты. Используйте восстановление hosts.";
                RefreshState();
                await Task.Delay(450);
            }
            catch (Exception exception)
            {
                OperationMessage = ToUserMessage("Не удалось отключить NetBypass", exception);
                RefreshState();
            }
            finally
            {
                PowerOperation = PowerOperation.None;
            }
            return;
        }

        PowerOperation = PowerOperation.Connecting;
        try
        {
            await ApplySelectedServicesAsync();
            await Task.Delay(450);
        }
        finally
        {
            PowerOperation = PowerOperation.None;
        }
    }

    private async Task ApplySelectedServicesAsync()
    {
        var selected = Services.Where(item => item.IsSelected).ToArray();
        var selectedAntiDpi = AntiDpiServices.Where(item => item.IsSelected).ToArray();
        IsBusy = true;
        OperationMessage = selected.Length == 0 && selectedAntiDpi.Length > 0
            ? "Подбираем Anti-DPI стратегию и доступные edge-IP..."
            : string.Empty;
        ClearCleanupReport();
        ClearEngineActivityLog();
        _lastAntiDpiOptimizationResult = null;
        try
        {
            ServiceModule[] effectiveModules = [];
            ServiceDiagnosticResult[] failed = [];
            var reachableCount = 0;
            if (selected.Length > 0)
            {
                var results = await DiagnoseWithProgressAsync(selected);
                SaveAndDisplayDiagnostics(results);
                failed = results.Where(result => !result.IsReachable).ToArray();
                var reachableIds = results.Where(result => result.IsReachable)
                    .Select(result => result.ServiceId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var reachable = selected.Where(item => reachableIds.Contains(item.Profile.Id))
                    .ToArray();
                reachableCount = reachable.Length;

                if (reachable.Length == 0 && selectedAntiDpi.Length == 0)
                {
                    VerificationState = VerificationState.Unavailable;
                    OperationMessage =
                        "Не удалось применить записи: ни один выбранный сервис не прошёл проверку.";
                    return;
                }

                var resultById = results.ToDictionary(
                    result => result.ServiceId,
                    StringComparer.OrdinalIgnoreCase);
                effectiveModules = reachable
                    .Select(item => BuildEffectiveModule(item, resultById[item.Profile.Id]))
                    .ToArray();
            }

            _settingsService.Save(
                selected.Select(item => item.Module.Id),
                IsGoodbyeDpiInstalled
                    ? selectedAntiDpi.Select(item => item.Id)
                    : []);
            var engineMessage = await SyncGoodbyeDpiAsync(selectedAntiDpi);
            ApplyManagedModules(
                effectiveModules,
                _lastAntiDpiOptimizationResult?.Addresses);
            DnsCacheService.Flush();

            OperationMessage = selected.Length == 0
                ? selectedAntiDpi.Length == 0
                    ? "Нет выбранных сервисов."
                    : engineMessage
                : failed.Length == 0
                    ? $"Все выбранные сервисы доступны: {reachableCount} из {selected.Length}."
                    : $"Записи применены. Доступно сервисов: {reachableCount} из {selected.Length}.";
            if (!string.IsNullOrWhiteSpace(engineMessage))
            {
                if (!OperationMessage.Contains(engineMessage, StringComparison.Ordinal))
                    OperationMessage += $" {engineMessage}";
            }
            RefreshState();
        }
        catch (Exception exception)
        {
            OperationMessage = ToUserMessage("Не удалось применить выбранные сервисы", exception);
            VerificationState = VerificationState.Unavailable;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyFromServicesAsync()
    {
        CurrentPage = AppPage.Home;
        if (!IsPowerOn)
        {
            await TogglePowerAsync();
            return;
        }

        PowerOperation = PowerOperation.Connecting;
        try
        {
            await ApplySelectedServicesAsync();
            await Task.Delay(450);
        }
        finally
        {
            PowerOperation = PowerOperation.None;
        }
    }

    private async Task DiagnoseSelectedAsync()
    {
        var selected = Services.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
            return;

        IsBusy = true;
        OperationMessage = string.Empty;
        ClearCleanupReport();
        try
        {
            var results = await DiagnoseWithProgressAsync(selected);
            SaveAndDisplayDiagnostics(results);
            VerificationState = results.All(result => result.IsReachable)
                ? VerificationState.Verified
                : VerificationState.Unavailable;
            OperationMessage = results.All(result => result.IsReachable)
                ? "Все выбранные адреса прошли TCP/TLS-проверку."
                : "Часть адресов недоступна. Их можно исключить и применить остальные сервисы.";
        }
        catch (Exception exception)
        {
            OperationMessage = ToUserMessage("Не удалось проверить выбранные сервисы", exception);
            VerificationState = VerificationState.Unavailable;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyReachableServicesAsync()
    {
        if (_unavailableServiceIds.Count == 0)
            return;

        foreach (var service in Services.Where(item =>
                     _unavailableServiceIds.Contains(item.Profile.Id)))
        {
            service.IsSelected = false;
        }

        var remaining = Services.Count(item => item.IsSelected);
        if (remaining == 0)
        {
            OperationMessage = "Все выбранные сервисы недоступны. Применять нечего.";
            return;
        }

        OperationMessage =
            $"Недоступные сервисы исключены. Проверяем оставшиеся: {remaining}.";
        await ApplySelectedServicesAsync();
    }

    private async Task<string> SyncGoodbyeDpiAsync(
        IReadOnlyCollection<AntiDpiServiceItemViewModel> selectedAntiDpi)
    {
        if (!IsGoodbyeDpiInstalled)
        {
            _lastAntiDpiOptimizationResult = null;
            IsGoodbyeDpiRuntimeEnabled = false;
            if (selectedAntiDpi.Count > 0)
                AddEngineActivity("GoodbyeDPI не установлен — проверка движка пропущена.");
            return selectedAntiDpi.Count == 0
                ? string.Empty
                : "GoodbyeDPI ещё не скачан.";
        }

        if (selectedAntiDpi.Count == 0)
        {
            _lastAntiDpiOptimizationResult = null;
            var stopped = await _goodbyeDpiRuntimeService.DisableAsync();
            IsGoodbyeDpiRuntimeEnabled = false;
            EngineOperationMessage = stopped.Message;
            AddEngineActivity(stopped.Message);
            return "Anti-DPI сервисы не выбраны, GoodbyeDPI выключен.";
        }

        AddEngineActivity("Начинаем подбор стратегии и edge-IP.");
        var progress = new Progress<string>(message =>
        {
            EngineOperationMessage = message;
            AddEngineActivity(message);
        });
        var optimized = await _goodbyeDpiStrategyOptimizer.EnableBestAsync(
            selectedAntiDpi.Select(item => item.Id),
            progress);
        _lastAntiDpiOptimizationResult = optimized;
        IsGoodbyeDpiRuntimeEnabled = optimized.IsSuccessful;
        EngineOperationMessage = optimized.Message;
        AddEngineActivity(optimized.Message);
        return optimized.Message;
    }

    private void ApplyManagedModules(
        IEnumerable<ServiceModule> regularModules,
        IReadOnlyDictionary<string, string>? antiDpiAddresses)
    {
        var modules = regularModules.ToList();
        if (antiDpiAddresses is not null && antiDpiAddresses.Count > 0)
            modules.Add(BuildAntiDpiHostsModule(antiDpiAddresses));

        if (modules.Count > 0)
        {
            _hostsService.Apply(modules);
        }
        else if (_hostsService.GetState([]) != HostsState.Inactive)
        {
            _hostsService.Disable();
        }
    }

    private static ServiceModule BuildAntiDpiHostsModule(
        IReadOnlyDictionary<string, string> addresses) =>
        new(
            "anti-dpi-routing",
            "Anti-DPI маршрутизация",
            "Anti-DPI",
            false,
            GoodbyeDpiRuntimeService.BuildHostsEntries(addresses),
            "generated");

    private async Task DisableGoodbyeDpiAsync()
    {
        var stopped = await _goodbyeDpiRuntimeService.DisableAsync();
        IsGoodbyeDpiRuntimeEnabled = false;
        EngineOperationMessage = stopped.Message;
    }

    private async Task<IReadOnlyList<ServiceDiagnosticResult>> DiagnoseWithProgressAsync(
        IReadOnlyCollection<ServiceItemViewModel> selected)
    {
        DiagnosticTotal = selected.Count;
        DiagnosticCompleted = 0;
        CurrentDiagnosticService = string.Empty;
        Diagnostics.Clear();
        ServiceActivity.Clear();
        foreach (var item in selected)
            ServiceActivity.Add(new OperationTraceItemViewModel(item.Profile.Id, item.Name));
        OnPropertyChanged(nameof(HasServiceActivity));
        OnPropertyChanged(nameof(HasNoServiceActivity));
        OnPropertyChanged(nameof(HasLiveActivity));
        var progress = new Progress<NetworkDiagnosticProgress>(UpdateServiceActivity);
        var previousSelections = _endpointSelectionStore.Load();
        var results = new List<ServiceDiagnosticResult>(selected.Count);
        var pendingItems = selected.ToList();
        var maximumAttempts = MultiCheckEnabled ? DiagnosticAttempts : 1;

        for (var attempt = 1; pendingItems.Count > 0; attempt++)
        {
            if (attempt > 1)
            {
                var delay = DiagnosticRetryPolicy.DelayBeforeAttempt(attempt);
                CurrentDiagnosticService =
                    $"повтор {attempt} из {maximumAttempts} через {delay.TotalSeconds:0.#} с";
                await Task.Delay(delay);
            }

            using var semaphore = new SemaphoreSlim(6);
            var pendingTasks = pendingItems.Select(async item =>
            {
                await semaphore.WaitAsync();
                try
                {
                    previousSelections.TryGetValue(item.Profile.Id, out var previousSelection);
                    var result = await _diagnosticService.DiagnoseAsync(
                        item.Profile,
                        previousSelection,
                        progress);
                    return (Item: item, Result: result with
                    {
                        AttemptCount = attempt,
                        MaximumAttempts = maximumAttempts
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            var retryItems = new List<ServiceItemViewModel>();
            while (pendingTasks.Count > 0)
            {
                var completedTask = await Task.WhenAny(pendingTasks);
                pendingTasks.Remove(completedTask);
                var completed = await completedTask;
                CurrentDiagnosticService = completed.Item.Name;

                if (DiagnosticRetryPolicy.ShouldRetry(
                        completed.Result,
                        attempt,
                        maximumAttempts))
                {
                    retryItems.Add(completed.Item);
                    continue;
                }

                results.Add(completed.Result);
                DiagnosticCompleted++;
                Diagnostics.Add(new DiagnosticItemViewModel(completed.Result));
                ServiceActivity.First(item => item.ServiceId == completed.Item.Profile.Id)
                    .Complete(completed.Result);
            }

            pendingItems = retryItems;
        }

        CurrentDiagnosticService = string.Empty;
        return results;
    }

    private void SaveDiagnosticSettings()
    {
        _settingsService.Save(
            Services.Where(item => item.IsSelected).Select(item => item.Module.Id),
            IsGoodbyeDpiInstalled
                ? AntiDpiServices.Where(item => item.IsSelected).Select(item => item.Id)
                : [],
            multiCheckEnabled: MultiCheckEnabled,
            diagnosticAttempts: DiagnosticAttempts);
    }

    private void UpdateServiceActivity(NetworkDiagnosticProgress progress)
    {
        ServiceActivity.FirstOrDefault(item => item.ServiceId == progress.ServiceId)
            ?.Update(progress);
        CurrentDiagnosticService = progress.ServiceName;
    }

    private void SaveAndDisplayDiagnostics(
        IReadOnlyList<ServiceDiagnosticResult> results)
    {
        var snapshot = new DiagnosticSnapshot(DateTimeOffset.UtcNow, results);
        _diagnosticStore.Save(snapshot);
        _endpointSelectionStore.SaveFromDiagnostics(results);
        _unavailableServiceIds = results
            .Where(result => !result.IsReachable)
            .Select(result => result.ServiceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        OnPropertyChanged(nameof(HasUnavailableServices));
        RaiseAvailabilityProperties();
        ApplyReachableCommand.RaiseCanExecuteChanged();
    }

    private void LoadStoredDiagnostics()
    {
        var snapshot = _diagnosticStore.Load();
        if (snapshot is null)
            return;

        foreach (var result in snapshot.Services.OrderBy(result => result.ServiceName))
        {
            Diagnostics.Add(new DiagnosticItemViewModel(result));
            var trace = new OperationTraceItemViewModel(result.ServiceId, result.ServiceName);
            trace.Complete(result);
            ServiceActivity.Add(trace);
        }

        _unavailableServiceIds = snapshot.Services
            .Where(result => !result.IsReachable)
            .Select(result => result.ServiceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        RaiseAvailabilityProperties();
        OnPropertyChanged(nameof(HasServiceActivity));
        OnPropertyChanged(nameof(HasNoServiceActivity));
        OnPropertyChanged(nameof(HasLiveActivity));
    }

    private static IReadOnlyList<AntiDpiServiceItemViewModel> CreateAntiDpiServices(
        IReadOnlySet<string>? selectedIds)
    {
        bool IsSelectedByDefault(string id) =>
            selectedIds is null || selectedIds.Contains(id);

        return
        [
            new AntiDpiServiceItemViewModel(
                "youtube",
                "YouTube",
                "GoodbyeDPI",
                "Будущий запуск Anti-DPI режима для видео и связанных доменов.",
                IsSelectedByDefault("youtube")),
            new AntiDpiServiceItemViewModel(
                "discord",
                "Discord",
                "GoodbyeDPI",
                "Будущий запуск Anti-DPI режима для голосовой связи, клиента и сайта.",
                IsSelectedByDefault("discord"))
        ];
    }

    private IReadOnlyList<EngineCardViewModel> CreateEngineCards(bool isGoodbyeDpiInstalled) =>
    [
        new EngineCardViewModel(
            "GoodbyeDPI",
            BypassEngineKind.AntiDpi,
            isGoodbyeDpiInstalled ? "Скачан" : "Нужно скачать",
            isGoodbyeDpiInstalled,
            ["YouTube", "Discord", "сервисы с DPI-блокировкой"],
            "Windows-движок для обхода DPI. NetBypass будет запускать его как внешний процесс и проверять результат после старта.",
            isGoodbyeDpiInstalled
                ? "Следующий шаг: запуск базового профиля и безопасная остановка."
                : "Сначала скачиваем официальный архив GoodbyeDPI и сохраняем его в папку пользователя.",
            showDownloadButton: !isGoodbyeDpiInstalled,
            showRemoveButton: isGoodbyeDpiInstalled),
        new EngineCardViewModel(
            "zapret / zapret2",
            BypassEngineKind.AntiDpi,
            "В следующих обновлениях",
            false,
            ["YouTube", "Discord", "сложные DPI-сценарии"],
            "Более гибкий Anti-DPI-инструмент с большим количеством стратегий. Подключим после общего lifecycle и журнала ресурсов.",
            "Пока неактивно: сначала докажем безопасный запуск и cleanup на GoodbyeDPI."),
        new EngineCardViewModel(
            "ByeDPI",
            BypassEngineKind.AntiDpi,
            "В следующих обновлениях",
            false,
            ["резервный Anti-DPI режим"],
            "Альтернативный внешний движок. Его удобно держать как запасной вариант, когда появится общий интерфейс адаптеров.",
            "Пока неактивно: добавим после первого рабочего Anti-DPI адаптера.")
    ];

    private void RebuildEngineCards()
    {
        Engines.Clear();
        foreach (var engine in CreateEngineCards(IsGoodbyeDpiInstalled))
            Engines.Add(engine);
    }

    private void SetAll(bool value)
    {
        foreach (var service in Services)
            service.IsSelected = value;
        foreach (var service in AntiDpiServices)
            service.IsSelected = value;
    }

    private void OnServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ServiceItemViewModel.IsSelected))
            return;

        OperationMessage = string.Empty;
        RefreshState();
        ApplyCommand.RaiseCanExecuteChanged();
        DiagnoseCommand.RaiseCanExecuteChanged();
        ApplyReachableCommand.RaiseCanExecuteChanged();
    }

    private void OnAntiDpiServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AntiDpiServiceItemViewModel.IsSelected))
            return;

        OperationMessage = string.Empty;
        OnPropertyChanged(nameof(HasSelectedBypassTarget));
        OnPropertyChanged(nameof(AntiDpiSelectionSummary));
        ApplyCommand.RaiseCanExecuteChanged();
    }

    private void RefreshState()
    {
        var selected = Services.Where(item => item.IsSelected).ToArray();
        HostsState = _hostsService.GetState(GetExpectedActiveModules(selected));
        VerificationState = DetermineVerificationState(selected);
        RaiseAvailabilityProperties();
    }

    private IEnumerable<ServiceModule> GetExpectedActiveModules(
        IReadOnlyCollection<ServiceItemViewModel> selected)
    {
        var snapshot = _diagnosticStore.Load();
        if (snapshot is null)
            return WithStoredAntiDpiModule(selected.Select(item => item.Module));

        var selectedIds = selected.Select(item => item.Profile.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resultIds = snapshot.Services.Select(result => result.ServiceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!selectedIds.SetEquals(resultIds))
            return WithStoredAntiDpiModule(selected.Select(item => item.Module));

        var resultById = snapshot.Services
            .Where(result => result.IsReachable)
            .ToDictionary(result => result.ServiceId, StringComparer.OrdinalIgnoreCase);
        return WithStoredAntiDpiModule(
            selected.Where(item => resultById.ContainsKey(item.Profile.Id))
                .Select(item => BuildEffectiveModule(item, resultById[item.Profile.Id])));
    }

    private IEnumerable<ServiceModule> WithStoredAntiDpiModule(
        IEnumerable<ServiceModule> regularModules)
    {
        var modules = regularModules.ToList();
        var selection = _antiDpiStrategySelectionStore.Load();
        var selectedAntiDpiIds = AntiDpiServices
            .Where(item => item.IsSelected)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selection?.Addresses is { Count: > 0 }
            && selection.ServiceIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(selectedAntiDpiIds))
            modules.Add(BuildAntiDpiHostsModule(selection.Addresses));
        return modules;
    }

    private static ServiceModule BuildEffectiveModule(
        ServiceItemViewModel item,
        ServiceDiagnosticResult result)
    {
        var selectedAddress = result.SelectedAddress ?? result.TargetAddress;
        if (string.IsNullOrWhiteSpace(selectedAddress))
            return item.Module;

        var entries = item.Module.Entries
            .Select(entry => entry with { Address = selectedAddress })
            .ToArray();
        return item.Module with { Entries = entries };
    }

    private VerificationState DetermineVerificationState(
        IReadOnlyCollection<ServiceItemViewModel> selected)
    {
        // Диагностика описывает доступность адресов, но не состояние NetBypass.
        // После удаления управляемого блока отключённый экран должен быть таким же,
        // как при первом запуске, независимо от сохранённых результатов проверки.
        if (HostsState == HostsState.Inactive)
            return VerificationState.NotChecked;

        var snapshot = _diagnosticStore.Load();
        if (snapshot is null
            || DateTimeOffset.UtcNow - snapshot.CreatedAt > VerificationLifetime)
        {
            return VerificationState.NotChecked;
        }

        var selectedIds = selected.Select(item => item.Profile.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resultIds = snapshot.Services.Select(result => result.ServiceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!selectedIds.SetEquals(resultIds))
            return VerificationState.NotChecked;

        return snapshot.Services.All(result => result.IsReachable)
            ? VerificationState.Verified
            : VerificationState.Unavailable;
    }

    private void RaiseStateProperties()
    {
        OnPropertyChanged(nameof(StateTitle));
        OnPropertyChanged(nameof(StateDescription));
        OnPropertyChanged(nameof(StateAccent));
        OnPropertyChanged(nameof(CurrentUiState));
        OnPropertyChanged(nameof(PowerButtonLabel));
        OnPropertyChanged(nameof(IsPowerOn));
        OnPropertyChanged(nameof(ShowHomeActivity));
        OnPropertyChanged(nameof(IsCorrupted));
        PowerCommand?.RaiseCanExecuteChanged();
        ApplyCommand?.RaiseCanExecuteChanged();
        DiagnoseCommand?.RaiseCanExecuteChanged();
        ApplyReachableCommand?.RaiseCanExecuteChanged();
        RaiseAvailabilityProperties();
    }

    private void RaiseAvailabilityProperties()
    {
        OnPropertyChanged(nameof(SelectedServiceCount));
        OnPropertyChanged(nameof(AvailableServiceCount));
        OnPropertyChanged(nameof(AvailabilitySummary));
        OnPropertyChanged(nameof(HasAvailabilitySummary));
        OnPropertyChanged(nameof(HasPartialAvailability));
        OnPropertyChanged(nameof(HasSelectedBypassTarget));
        OnPropertyChanged(nameof(AntiDpiSelectionSummary));
        OnPropertyChanged(nameof(AntiDpiInstallStatus));
    }

    private void RunSafely(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            OperationMessage = ToUserMessage("Операция не выполнена", exception);
            RefreshState();
        }
    }

    private void ClearCleanupReport()
    {
        CleanupTitle = string.Empty;
        CleanupItems.Clear();
        OnPropertyChanged(nameof(HasCleanupItems));
    }

    private void ClearEngineActivityLog()
    {
        EngineActivityLog.Clear();
        OnPropertyChanged(nameof(HasEngineActivityLog));
        OnPropertyChanged(nameof(HasNoEngineActivityLog));
        OnPropertyChanged(nameof(HasLiveActivity));
    }

    private void AddEngineActivity(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        EngineActivityLog.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        while (EngineActivityLog.Count > 9)
            EngineActivityLog.RemoveAt(0);
        OnPropertyChanged(nameof(HasEngineActivityLog));
        OnPropertyChanged(nameof(HasNoEngineActivityLog));
        OnPropertyChanged(nameof(HasLiveActivity));
    }

    private void SetCleanupReport(
        string title,
        CleanupVerificationResult report,
        bool dnsFlushed)
    {
        CleanupTitle = title;
        CleanupItems.Clear();

        foreach (var item in report.CompletedChecks)
            CleanupItems.Add($"✓ {item}");

        if (dnsFlushed)
            CleanupItems.Add("✓ DNS-кеш Windows очищен.");

        foreach (var issue in report.Issues)
            CleanupItems.Add($"! {issue}");

        OnPropertyChanged(nameof(HasCleanupItems));
    }

    private static string ToUserMessage(string prefix, Exception exception)
    {
        var hint = exception switch
        {
            UnauthorizedAccessException =>
                "Запустите NetBypass от имени администратора и проверьте, не блокирует ли hosts антивирус.",
            FileNotFoundException =>
                "Системный файл hosts не найден.",
            InvalidDataException =>
                "В hosts найден повреждённый блок NetBypass. Используйте восстановление hosts.",
            IOException =>
                "Windows или другая программа сейчас удерживает файл. Закройте лишние процессы и повторите попытку.",
            _ => exception.Message
        };

        return $"{prefix}: {hint}";
    }
}

public enum AppPage
{
    Home,
    Services,
    Engines,
    Diagnostics,
    Settings
}

public enum VerificationState
{
    NotChecked,
    Verified,
    Unavailable
}

public enum PowerOperation
{
    None,
    Connecting,
    Disconnecting
}

public enum UiState
{
    Unknown,
    Disabled,
    Checking,
    Disabling,
    ActiveVerified,
    ActiveDegraded,
    ActiveUnverified,
    ChangesPending,
    Corrupted
}
