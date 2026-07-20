using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using NetBypass.Core.Models;

namespace NetBypass.Core.Services;

public interface IDohResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string hostname,
        CancellationToken cancellationToken);
}

public interface IEndpointProbe
{
    Task<IReadOnlyList<ProbeResult>> ProbeAsync(
        HealthCheckDefinition healthCheck,
        IPAddress targetAddress,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProbeResult>> ProbeAsync(
        HealthCheckDefinition healthCheck,
        IPAddress targetAddress,
        IProgress<ProbeStage>? stageProgress,
        CancellationToken cancellationToken) =>
        ProbeAsync(healthCheck, targetAddress, cancellationToken);
}

public sealed class CloudflareGoogleDohResolver : IDohResolver
{
    private static readonly Uri[] Endpoints =
    [
        new("https://cloudflare-dns.com/dns-query"),
        new("https://dns.google/resolve")
    ];

    private readonly HttpClient _httpClient;

    public CloudflareGoogleDohResolver(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string hostname,
        CancellationToken cancellationToken)
    {
        var addresses = new HashSet<IPAddress>();

        foreach (var endpoint in Endpoints)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{endpoint}?name={Uri.EscapeDataString(hostname)}&type=A");
                request.Headers.Accept.ParseAdd("application/dns-json");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<DohResponse>(
                    cancellationToken: cancellationToken);

                foreach (var answer in payload?.Answer ?? [])
                {
                    if (answer.Type == 1 && IPAddress.TryParse(answer.Data, out var address))
                        addresses.Add(address);
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException
                    or TaskCanceledException
                    or NotSupportedException)
            {
                // A second resolver may still succeed. The caller receives an
                // empty list only when all configured resolvers fail.
            }
        }

        return addresses.ToArray();
    }

    private sealed record DohResponse(DohAnswer[]? Answer);
    private sealed record DohAnswer(int Type, string Data);
}

public sealed class EndpointProbe(TimeSpan? timeout = null) : IEndpointProbe
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(4);

    public Task<IReadOnlyList<ProbeResult>> ProbeAsync(
        HealthCheckDefinition healthCheck,
        IPAddress targetAddress,
        CancellationToken cancellationToken) =>
        ProbeAsync(healthCheck, targetAddress, null, cancellationToken);

    public async Task<IReadOnlyList<ProbeResult>> ProbeAsync(
        HealthCheckDefinition healthCheck,
        IPAddress targetAddress,
        IProgress<ProbeStage>? stageProgress,
        CancellationToken cancellationToken)
    {
        var results = new List<ProbeResult>();
        var checkedAt = DateTimeOffset.UtcNow;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);

        using var client = new TcpClient(targetAddress.AddressFamily);
        var stopwatch = Stopwatch.StartNew();
        stageProgress?.Report(ProbeStage.Tcp);
        try
        {
            await client.ConnectAsync(
                targetAddress,
                healthCheck.Port,
                timeoutSource.Token);
            stopwatch.Stop();
            results.Add(new ProbeResult(
                ProbeStage.Tcp,
                ProbeStatus.Success,
                stopwatch.Elapsed,
                targetAddress.ToString(),
                null,
                "TCP-соединение установлено",
                checkedAt));
        }
        catch (Exception exception) when (
            exception is SocketException
                or IOException
                or OperationCanceledException)
        {
            stopwatch.Stop();
            results.Add(new ProbeResult(
                ProbeStage.Tcp,
                ProbeStatus.Failed,
                stopwatch.Elapsed,
                targetAddress.ToString(),
                exception.GetType().Name,
                exception is OperationCanceledException
                    ? "Истекло время TCP-подключения"
                    : $"TCP недоступен: {exception.Message}",
                checkedAt));
            return results;
        }

        await using var networkStream = client.GetStream();
        using var tlsStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
        stopwatch.Restart();
        stageProgress?.Report(ProbeStage.Tls);
        try
        {
            await tlsStream.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = healthCheck.Host,
                    CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck
                },
                timeoutSource.Token);
            stopwatch.Stop();
            results.Add(new ProbeResult(
                ProbeStage.Tls,
                ProbeStatus.Success,
                stopwatch.Elapsed,
                targetAddress.ToString(),
                null,
                $"TLS-сертификат для {healthCheck.Host} действителен",
                checkedAt));
        }
        catch (Exception exception) when (
            exception is AuthenticationException
                or IOException
                or OperationCanceledException)
        {
            stopwatch.Stop();
            results.Add(new ProbeResult(
                ProbeStage.Tls,
                ProbeStatus.Failed,
                stopwatch.Elapsed,
                targetAddress.ToString(),
                exception.GetType().Name,
                exception is OperationCanceledException
                    ? "Истекло время TLS-проверки"
                    : $"TLS-проверка не пройдена: {exception.Message}",
                checkedAt));
            return results;
        }

        stopwatch.Restart();
        stageProgress?.Report(ProbeStage.Http);
        try
        {
            var request = Encoding.ASCII.GetBytes(
                $"HEAD / HTTP/1.1\r\nHost: {healthCheck.Host}\r\nUser-Agent: NetBypass-Diagnostics/1.0\r\nConnection: close\r\n\r\n");
            await tlsStream.WriteAsync(request, timeoutSource.Token);
            await tlsStream.FlushAsync(timeoutSource.Token);

            using var reader = new StreamReader(
                tlsStream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            var statusLine = await reader.ReadLineAsync(timeoutSource.Token);
            stopwatch.Stop();
            var statusCode = ParseHttpStatusCode(statusLine);
            var accepted = statusCode.HasValue
                && healthCheck.AcceptedHttpStatuses.Contains(statusCode.Value);
            results.Add(new ProbeResult(
                ProbeStage.Http,
                accepted ? ProbeStatus.Success : ProbeStatus.Warning,
                stopwatch.Elapsed,
                targetAddress.ToString(),
                statusCode?.ToString(),
                statusCode.HasValue
                    ? $"HTTP ответил кодом {statusCode}"
                    : "Не удалось прочитать HTTP-статус",
                checkedAt));
        }
        catch (Exception exception) when (
            exception is IOException
                or OperationCanceledException)
        {
            stopwatch.Stop();
            results.Add(new ProbeResult(
                ProbeStage.Http,
                ProbeStatus.Warning,
                stopwatch.Elapsed,
                targetAddress.ToString(),
                exception.GetType().Name,
                "TCP и TLS доступны, но HTTP-проверка не завершена",
                checkedAt));
        }

        return results;
    }

    private static int? ParseHttpStatusCode(string? statusLine)
    {
        if (string.IsNullOrWhiteSpace(statusLine))
            return null;

        var parts = statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && int.TryParse(parts[1], out var status)
            ? status
            : null;
    }
}

public sealed class NetworkDiagnosticService(
    IDohResolver dohResolver,
    IEndpointProbe endpointProbe)
{
    public async Task<ServiceDiagnosticResult> DiagnoseAsync(
        ServiceProfile profile,
        EndpointSelection? previousSelection = null,
        IProgress<NetworkDiagnosticProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var results = new List<ProbeResult>();
        var resolvedAddresses = new HashSet<IPAddress>();

        foreach (var host in profile.HealthChecks.Select(check => check.Host)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ReportProgress(profile, progress, ProbeStage.Dns, null,
                $"DoH-запрос для {host}");
            var resolved = await dohResolver.ResolveAsync(
                host,
                cancellationToken);
            resolvedAddresses.UnionWith(resolved);
            var dnsResult = new ProbeResult(
                ProbeStage.Dns,
                resolved.Count > 0 ? ProbeStatus.Success : ProbeStatus.Warning,
                null,
                resolved.FirstOrDefault()?.ToString(),
                resolved.Count > 0 ? null : "DohResolutionFailed",
                resolved.Count > 0
                    ? $"{host}: DoH вернул адресов — {resolved.Count}"
                    : $"{host}: DoH не ответил",
                checkedAt);
            results.Add(dnsResult);
            ReportProgress(profile, progress, dnsResult);
        }

        var candidates = BuildCandidates(profile);
        var previousCandidate = previousSelection is null
            ? null
            : candidates.FirstOrDefault(candidate => string.Equals(
                candidate.Address,
                previousSelection.Address,
                StringComparison.OrdinalIgnoreCase));

        CandidateProbeResult? selected = null;
        var checkedCandidates = new List<CandidateProbeResult>();

        if (previousCandidate is not null)
        {
            var previousResult = await ProbeCandidateAsync(
                profile,
                previousCandidate,
                isPreviousSelection: true,
                progress,
                cancellationToken);
            checkedCandidates.Add(previousResult);
            results.AddRange(previousResult.Probes);

            if (previousResult.IsReachable)
                selected = previousResult;
        }

        if (selected is null)
        {
            var remaining = candidates
                .Where(candidate => previousCandidate is null
                    || !string.Equals(
                        candidate.Address,
                        previousCandidate.Address,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var probed = await Task.WhenAll(remaining.Select(candidate =>
                ProbeCandidateAsync(
                    profile,
                    candidate,
                    isPreviousSelection: false,
                    progress,
                    cancellationToken)));
            checkedCandidates.AddRange(probed);
            foreach (var candidate in probed)
                results.AddRange(candidate.Probes);

            selected = checkedCandidates
                .Where(candidate => candidate.IsReachable)
                .OrderBy(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Candidate.Priority)
                .FirstOrDefault();
        }

        var reachable = selected is not null;
        var candidateResults = checkedCandidates
            .Select(candidate => candidate.ToResult())
            .OrderByDescending(candidate => candidate.IsPreviousSelection)
            .ThenByDescending(candidate => candidate.IsReachable)
            .ThenBy(candidate => candidate.TcpLatency ?? TimeSpan.MaxValue)
            .ToArray();

        return new ServiceDiagnosticResult(
            profile.Id,
            profile.Name,
            selected?.Candidate.Address
                ?? previousCandidate?.Address
                ?? candidates.FirstOrDefault()?.Address
                ?? string.Empty,
            reachable,
            resolvedAddresses.Select(address => address.ToString()).ToArray(),
            results,
            checkedAt,
            selected?.Candidate.Address,
            BuildSelectionReason(selected, previousSelection, checkedCandidates),
            selected?.IsPreviousSelection ?? false,
            candidateResults);
    }

    private async Task<CandidateProbeResult> ProbeCandidateAsync(
        ServiceProfile profile,
        EndpointCandidate candidate,
        bool isPreviousSelection,
        IProgress<NetworkDiagnosticProgress>? progress,
        CancellationToken cancellationToken)
    {
        ReportProgress(profile, progress, ProbeStage.Tcp, null,
            $"TCP-подключение к {candidate.Address}:{candidate.Port}");
        var healthCheck = new HealthCheckDefinition(
            candidate.Address,
            candidate.Host,
            candidate.Port,
            candidate.Protocol,
            candidate.AcceptedHttpStatuses);
        var stageProgress = new Progress<ProbeStage>(stage =>
            ReportProgress(
                profile,
                progress,
                stage,
                null,
                StageRunningMessage(stage, candidate)));
        var probes = await endpointProbe.ProbeAsync(
            healthCheck,
            IPAddress.Parse(candidate.Address),
            stageProgress,
            cancellationToken);
        foreach (var probe in probes)
            ReportProgress(profile, progress, probe);
        return new CandidateProbeResult(candidate, isPreviousSelection, probes);
    }

    private static void ReportProgress(
        ServiceProfile profile,
        IProgress<NetworkDiagnosticProgress>? progress,
        ProbeResult result) =>
        ReportProgress(profile, progress, result.Stage, result.Status, result.Message);

    private static void ReportProgress(
        ServiceProfile profile,
        IProgress<NetworkDiagnosticProgress>? progress,
        ProbeStage stage,
        ProbeStatus? status,
        string message) =>
        progress?.Report(new NetworkDiagnosticProgress(
            profile.Id,
            profile.Name,
            stage,
            status,
            message));

    private static string StageRunningMessage(ProbeStage stage, EndpointCandidate candidate) =>
        stage switch
        {
            ProbeStage.Tcp => $"TCP-подключение к {candidate.Address}:{candidate.Port}",
            ProbeStage.Tls => $"TLS-рукопожатие с {candidate.Host}",
            ProbeStage.Http => $"HTTP HEAD-запрос к {candidate.Host}",
            _ => $"DoH-запрос для {candidate.Host}"
        };

    private static IReadOnlyList<EndpointCandidate> BuildCandidates(ServiceProfile profile)
    {
        var candidates = new Dictionary<string, EndpointCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var check in profile.HealthChecks)
        {
            candidates.TryAdd(
                check.TargetAddress,
                new EndpointCandidate(
                    check.TargetAddress,
                    check.Host,
                    check.Port,
                    check.Protocol,
                    0,
                    check.AcceptedHttpStatuses));
        }

        foreach (var relay in profile.RelayCandidates.OrderBy(candidate => candidate.Priority))
        {
            candidates.TryAdd(
                relay.Address,
                new EndpointCandidate(
                    relay.Address,
                    relay.Host,
                    relay.Port,
                    relay.Protocol,
                    relay.Priority,
                    Enumerable.Range(200, 300).ToHashSet()));
        }

        return candidates.Values.ToArray();
    }

    private static string BuildSelectionReason(
        CandidateProbeResult? selected,
        EndpointSelection? previousSelection,
        IReadOnlyCollection<CandidateProbeResult> checkedCandidates)
    {
        if (selected is null)
            return "Ни один адрес не прошёл TCP/TLS-проверку.";

        if (selected.IsPreviousSelection)
            return $"Использован прошлый рабочий адрес {selected.Candidate.Address}: повторная TCP/TLS-проверка пройдена.";

        var failedPrevious = previousSelection is not null
            && checkedCandidates.Any(candidate => candidate.IsPreviousSelection && !candidate.IsReachable);
        var prefix = failedPrevious
            ? "Прошлый адрес не прошёл проверку; "
            : string.Empty;
        var latency = selected.Score == TimeSpan.MaxValue
            ? string.Empty
            : $" Суммарная задержка TCP/TLS: {selected.Score.TotalMilliseconds:0} мс.";
        return $"{prefix}выбран адрес {selected.Candidate.Address}, потому что TCP и TLS доступны.{latency}";
    }

    private sealed record EndpointCandidate(
        string Address,
        string Host,
        int Port,
        string Protocol,
        int Priority,
        IReadOnlySet<int> AcceptedHttpStatuses);

    private sealed record CandidateProbeResult(
        EndpointCandidate Candidate,
        bool IsPreviousSelection,
        IReadOnlyList<ProbeResult> Probes)
    {
        public bool IsReachable =>
            Probes.Any(probe => probe.Stage == ProbeStage.Tcp && probe.Status == ProbeStatus.Success)
            && Probes.Any(probe => probe.Stage == ProbeStage.Tls && probe.Status == ProbeStatus.Success);

        public TimeSpan Score =>
            IsReachable
                ? Probes
                    .Where(probe => probe.Stage is ProbeStage.Tcp or ProbeStage.Tls)
                    .Select(probe => probe.Latency ?? TimeSpan.Zero)
                    .Aggregate(TimeSpan.Zero, (total, latency) => total + latency)
                : TimeSpan.MaxValue;

        public EndpointCandidateResult ToResult()
        {
            var tcp = Probes.FirstOrDefault(probe => probe.Stage == ProbeStage.Tcp);
            var tls = Probes.FirstOrDefault(probe => probe.Stage == ProbeStage.Tls);
            var reason = IsReachable
                ? "TCP и TLS доступны"
                : Probes.LastOrDefault(probe => probe.Status == ProbeStatus.Failed)?.Message
                  ?? "Проверка не пройдена";

            return new EndpointCandidateResult(
                Candidate.Address,
                Candidate.Host,
                IsReachable,
                IsPreviousSelection,
                tcp?.Latency,
                tls?.Latency,
                reason);
        }
    }
}
