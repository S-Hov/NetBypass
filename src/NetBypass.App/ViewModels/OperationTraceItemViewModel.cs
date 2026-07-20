using NetBypass.App.Infrastructure;
using NetBypass.Core.Models;

namespace NetBypass.App.ViewModels;

public sealed class OperationTraceItemViewModel : ObservableObject
{
    private readonly Dictionary<ProbeStage, ActivityStageState> _states = new();
    private string _currentMessage = "Ожидает проверки";
    private string _statusText = "В очереди";
    private string _statusColor = "#737C91";
    private bool _isRunning;

    public OperationTraceItemViewModel(string serviceId, string serviceName)
    {
        ServiceId = serviceId;
        ServiceName = serviceName;
        foreach (var stage in Enum.GetValues<ProbeStage>())
            _states[stage] = ActivityStageState.Pending;
    }

    public string ServiceId { get; }
    public string ServiceName { get; }
    public string CurrentMessage { get => _currentMessage; private set => SetProperty(ref _currentMessage, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string StatusColor { get => _statusColor; private set => SetProperty(ref _statusColor, value); }
    public bool IsRunning { get => _isRunning; private set => SetProperty(ref _isRunning, value); }
    public string DnsGlyph => Glyph(ProbeStage.Dns);
    public string TcpGlyph => Glyph(ProbeStage.Tcp);
    public string TlsGlyph => Glyph(ProbeStage.Tls);
    public string HttpGlyph => Glyph(ProbeStage.Http);
    public string DnsColor => Color(ProbeStage.Dns);
    public string TcpColor => Color(ProbeStage.Tcp);
    public string TlsColor => Color(ProbeStage.Tls);
    public string HttpColor => Color(ProbeStage.Http);

    public void Update(NetworkDiagnosticProgress progress)
    {
        var incoming = progress.Status switch
        {
            null => ActivityStageState.Running,
            ProbeStatus.Success => ActivityStageState.Success,
            ProbeStatus.Warning => ActivityStageState.Warning,
            ProbeStatus.Failed => ActivityStageState.Failed,
            _ => ActivityStageState.Skipped
        };
        if (_states[progress.Stage] != ActivityStageState.Success || incoming == ActivityStageState.Success)
            _states[progress.Stage] = incoming;

        IsRunning = true;
        StatusText = StageLabel(progress.Stage);
        StatusColor = Color(progress.Stage);
        CurrentMessage = progress.Message;
        RaiseStage(progress.Stage);
    }

    public void Complete(ServiceDiagnosticResult result)
    {
        foreach (var stage in Enum.GetValues<ProbeStage>())
        {
            var probes = result.Probes.Where(probe => probe.Stage == stage).ToArray();
            _states[stage] = probes.Any(probe => probe.Status == ProbeStatus.Success)
                ? ActivityStageState.Success
                : probes.Any(probe => probe.Status == ProbeStatus.Warning)
                    ? ActivityStageState.Warning
                    : probes.Any(probe => probe.Status == ProbeStatus.Failed)
                        ? ActivityStageState.Failed
                        : ActivityStageState.Skipped;
            RaiseStage(stage);
        }

        IsRunning = false;
        StatusText = result.IsReachable ? "Сервис активен" : "Недоступен";
        StatusColor = result.IsReachable ? "#64E5B3" : "#FF8296";
        CurrentMessage = result.Summary;
    }

    private string Glyph(ProbeStage stage) => _states[stage] switch
    {
        ActivityStageState.Running => "●",
        ActivityStageState.Success => "✓",
        ActivityStageState.Warning => "!",
        ActivityStageState.Failed => "×",
        ActivityStageState.Skipped => "—",
        _ => "·"
    };

    private string Color(ProbeStage stage) => _states[stage] switch
    {
        ActivityStageState.Running => "#9B8CFF",
        ActivityStageState.Success => "#64E5B3",
        ActivityStageState.Warning => "#F2B84B",
        ActivityStageState.Failed => "#FF8296",
        _ => "#596174"
    };

    private void RaiseStage(ProbeStage stage)
    {
        OnPropertyChanged(stage switch
        {
            ProbeStage.Dns => nameof(DnsGlyph), ProbeStage.Tcp => nameof(TcpGlyph),
            ProbeStage.Tls => nameof(TlsGlyph), _ => nameof(HttpGlyph)
        });
        OnPropertyChanged(stage switch
        {
            ProbeStage.Dns => nameof(DnsColor), ProbeStage.Tcp => nameof(TcpColor),
            ProbeStage.Tls => nameof(TlsColor), _ => nameof(HttpColor)
        });
    }

    private static string StageLabel(ProbeStage stage) => stage switch
    {
        ProbeStage.Dns => "DoH-запрос",
        ProbeStage.Tcp => "TCP-соединение",
        ProbeStage.Tls => "TLS-рукопожатие",
        _ => "HTTP-проверка"
    };
}

public enum ActivityStageState { Pending, Running, Success, Warning, Failed, Skipped }
